using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Workhaven.Api.Infrastructure.Email;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed class ResendEmailDeliveryTests
{
    [Fact]
    public async Task SendsThePreparedMessageWithAuthenticationAndAnIdempotencyKey()
    {
        var deliveryId = Guid.NewGuid();
        var message = new EmailMessage("Workhaven <onboarding@resend.dev>", "recipient@example.test", "Confirm your email",
            "A link with + / = & characters", "<p>Confirm your email</p>");
        using var handler = new CallbackHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.resend.com/emails", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer test-key", request.Headers.Authorization?.ToString());
            Assert.Equal($"confirmation/{deliveryId:D}", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            Assert.NotNull(request.Content);
            Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal(message.From, body.RootElement.GetProperty("from").GetString());
            Assert.Equal(message.Recipient, body.RootElement.GetProperty("to")[0].GetString());
            Assert.Equal(message.Subject, body.RootElement.GetProperty("subject").GetString());
            Assert.Equal(message.Text, body.RootElement.GetProperty("text").GetString());
            Assert.Equal(message.Html, body.RootElement.GetProperty("html").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        var delivery = CreateDelivery(client);

        var result = await delivery.SendAsync(message, deliveryId, TestContext.Current.CancellationToken);
        Assert.Equal(EmailDeliveryStatus.Accepted, result.Status);
    }

    [Fact]
    public async Task PreviouslyPreparedPlainTextMessageKeepsItsOriginalRequestBody()
    {
        var message = JsonSerializer.Deserialize<EmailMessage>("""
            {"From":"Workhaven <sender@example.test>","Recipient":"recipient@example.test","Subject":"Confirm your email","Text":"A link with + / = & characters"}
            """);
        Assert.NotNull(message);
        Assert.Null(message.Html);
        using var originalContent = System.Net.Http.Json.JsonContent.Create(new
        {
            from = message.From,
            to = new[] { message.Recipient },
            subject = message.Subject,
            text = message.Text
        });
        var originalBody = await originalContent.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var handler = new CallbackHandler(async (request, cancellationToken) =>
        {
            Assert.NotNull(request.Content);
            Assert.Equal(originalBody, await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        Assert.Equal(EmailDeliveryStatus.Accepted,
            (await CreateDelivery(client).SendAsync(message, Guid.NewGuid(), TestContext.Current.CancellationToken)).Status);
    }

    [Theory]
    [InlineData(408, "", true)]
    [InlineData(429, "", true)]
    [InlineData(500, "", true)]
    [InlineData(503, "", true)]
    [InlineData(400, "", false)]
    [InlineData(401, "", false)]
    [InlineData(403, "", false)]
    [InlineData(422, "", false)]
    [InlineData(302, "", false)]
    [InlineData(409, "{\"name\":\"concurrent_idempotent_requests\"}", true)]
    [InlineData(409, "{\"name\":\"resource_locked\"}", true)]
    [InlineData(409, "{\"name\":\"invalid_idempotent_request\"}", false)]
    [InlineData(409, "{\"name\":17}", false)]
    [InlineData(409, "[]", false)]
    [InlineData(409, "not-json", false)]
    public async Task DistinguishesRetryableResponsesFromPermanentRejections(int status, string body, bool retry)
    {
        using var handler = new CallbackHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        using var client = new HttpClient(handler);
        var result = await CreateDelivery(client).SendAsync(
            new("sender@example.test", "recipient@example.test", "Subject", "Body"), Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.Equal(retry ? EmailDeliveryStatus.Retry : EmailDeliveryStatus.Rejected, result.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreservesRetryAfterAsDeltaOrHttpDate(bool date)
    {
        using var handler = new CallbackHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = date
                ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(2))
                : new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var result = await CreateDelivery(client).SendAsync(
            new("sender@example.test", "recipient@example.test", "Subject", "Body"), Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.Equal(EmailDeliveryStatus.Retry, result.Status);
        Assert.NotNull(result.RetryAfter);
        Assert.InRange(result.RetryAfter.Value.TotalSeconds, 118, 120);
    }

    [Fact]
    public async Task PropagatesCancellationToTheHttpRequest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new CallbackHandler(async (_, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var sending = CreateDelivery(client).SendAsync(
            new("sender@example.test", "recipient@example.test", "Subject", "Body"), Guid.NewGuid(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
    }

    private static ResendEmailDelivery CreateDelivery(HttpClient client) =>
        new(client, Options.Create(new ResendOptions { ApiKey = "test-key", From = "new-sender@example.test" }));

    private sealed class CallbackHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }
}
