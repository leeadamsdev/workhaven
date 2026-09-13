using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Workhaven.Api.Features.Identity;
using Workhaven.Api.Features.Identity.EmailConfirmation;
using Workhaven.Api.Infrastructure.Email;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed class ResendConfirmationEmailTests(IdentityDatabaseFixture fixture)
    : IClassFixture<IdentityDatabaseFixture>, IAsyncLifetime
{
    private const string Password = "a long test passphrase";
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

    public async ValueTask InitializeAsync()
    {
        // Only the database owned by this Testcontainers fixture is reset.
        await using var factory = ApiFactory.Create(fixture.Database.Settings);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>().Users
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task RegistrationUsesResendAndItsLinkConfirmsTheAccount(string environment)
    {
        string? body = null;
        await using var factory = CreateFactory(async (request, ct) =>
        {
            body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK);
        }, environment).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(2))));
        var email = await RegisterAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            Assert.IsType<ResendEmailDelivery>(scope.ServiceProvider.GetRequiredService<IEmailDelivery>());
        }

        var beforeSending = DateTimeOffset.UtcNow;
        Assert.True(await SendNextAsync(factory));
        Assert.NotNull(body);
        using var sent = JsonDocument.Parse(body);
        Assert.Equal(email, sent.RootElement.GetProperty("to")[0].GetString());
        var text = sent.RootElement.GetProperty("text").GetString()!;
        var link = new Uri(text.Split('\n').Single(line => line.StartsWith("http", StringComparison.Ordinal)));
        var html = sent.RootElement.GetProperty("html").GetString();
        Assert.NotNull(html);
        Assert.Contains($"href=\"{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(link.AbsoluteUri)}\"", html, StringComparison.Ordinal);
        var expiryLine = text.Split('\n').Single(line => line.StartsWith("This link expires", StringComparison.Ordinal));
        var expiry = DateTimeOffset.ParseExact(expiryLine, "'This link expires on' d MMMM yyyy 'at' HH:mm 'UTC.'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        Assert.InRange(expiry, beforeSending.AddHours(2).AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2));
        Assert.Contains(expiryLine, html, StringComparison.Ordinal);
        Assert.Equal(environment == "Production" ? "https" : "http", link.Scheme);
        Assert.Empty(link.Query);
        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(link.Fragment[1..]);
        using var client = factory.CreateClient();
        using var confirmed = await client.PostAsJsonAsync("/api/auth/confirm-email",
            new { userId = values["userId"].ToString(), token = values["token"].ToString() }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.Null(await PendingAsync(factory));
    }

    [Fact]
    public async Task AmbiguousNetworkFailureRetriesIdenticalContentAfterRestartAndConfigurationChanges()
    {
        var requests = new List<(string Key, string Body)>();
        await using (var failing = CreateFactory(async (request, ct) =>
        {
            requests.Add((Assert.Single(request.Headers.GetValues("Idempotency-Key")), await request.Content!.ReadAsStringAsync(ct)));
            throw new HttpRequestException("Simulated lost response.");
        }))
        {
            var email = await RegisterAsync(failing);
            Assert.True(await SendNextAsync(failing));
            var pending = await PendingAsync(failing);
            Assert.NotNull(pending);
            Assert.Equal(1, pending.Attempts);
            Assert.NotNull(pending.ProtectedMessage);
            Assert.DoesNotContain(email, pending.ProtectedMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("token=", pending.ProtectedMessage, StringComparison.Ordinal);
            Assert.False(await SendNextAsync(failing));
            await ChangePendingAsync(failing, row => row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        await using var restarted = CreateFactory(async (request, ct) =>
        {
            requests.Add((Assert.Single(request.Headers.GetValues("Idempotency-Key")), await request.Content!.ReadAsStringAsync(ct)));
            return new(HttpStatusCode.OK);
        }, settings: new()
        {
            ["Resend:From"] = "different@example.test",
            ["Resend:ApiKey"] = "rotated-test-key",
            ["ConfirmationEmail:PageUrl"] = "https://changed.example.test/confirm-email"
        });
        Assert.True(await SendNextAsync(restarted));
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0], requests[1]);
        using var sent = JsonDocument.Parse(requests[0].Body);
        Assert.False(string.IsNullOrWhiteSpace(sent.RootElement.GetProperty("html").GetString()));
        Assert.Null(await PendingAsync(restarted));
    }

    [Fact]
    public async Task AcceptedSendFollowedByDatabaseFailureReusesTheDurablyPreparedRequest()
    {
        var requests = new List<(string Key, string Body)>();
        async Task<HttpResponseMessage> AcceptAsync(HttpRequestMessage request, CancellationToken ct)
        {
            requests.Add((Assert.Single(request.Headers.GetValues("Idempotency-Key")), await request.Content!.ReadAsStringAsync(ct)));
            // An independent connection must see the preparation before the provider accepts it.
            await using var reader = ApiFactory.Create(fixture.Database.Settings);
            var pending = await PendingAsync(reader);
            Assert.NotNull(pending?.ProtectedMessage);
            Assert.Equal($"confirmation/{pending.DeliveryId:D}", requests[^1].Key);
            return new(HttpStatusCode.OK);
        }

        await using (var failing = CreateFactory(AcceptAsync).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<WorkhavenIdentityDbContext>(options => options.AddInterceptors(new RejectQueueDelete())))))
        {
            await RegisterAsync(failing);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => SendNextAsync(failing));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.NotNull(await PendingAsync(failing));
        }

        await using var restarted = CreateFactory(AcceptAsync);
        Assert.True(await SendNextAsync(restarted));
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0], requests[1]);
        Assert.Null(await PendingAsync(restarted));
    }

    [Fact]
    public async Task PermanentRejectionStopsRetryingAndOnlyTheOriginalPasswordCanRequestAFreshSend()
    {
        var keys = new List<string>();
        var reject = true;
        await using var factory = CreateFactory((request, _) =>
        {
            keys.Add(Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            return Task.FromResult(new HttpResponseMessage(reject ? HttpStatusCode.Forbidden : HttpStatusCode.OK));
        });
        var email = await RegisterAsync(factory);
        Assert.True(await SendNextAsync(factory));
        var failed = await PendingAsync(factory);
        Assert.NotNull(failed?.FailedAt);
        Assert.Equal("provider-rejected", failed.FailureCode);
        Assert.Null(failed.ProtectedMessage);
        Assert.False(await SendNextAsync(factory));
        await RegisterAsync(factory, email, "a different test passphrase");
        Assert.False(await SendNextAsync(factory));

        // A stopped row must not block another account's delivery.
        reject = false;
        await RegisterAsync(factory);
        Assert.True(await SendNextAsync(factory));
        Assert.NotNull((await PendingAsync(factory))?.FailedAt);

        await RegisterAsync(factory, email);
        Assert.True(await SendNextAsync(factory));
        Assert.Equal(3, keys.Count);
        Assert.NotEqual(keys[0], keys[2]);
        Assert.Null(await PendingAsync(factory));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(172800)]
    public async Task RetryAfterIsRespectedWithoutExtendingTheIdempotencyWindow(int seconds)
    {
        await using var factory = CreateFactory((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            return Task.FromResult(response);
        });
        await RegisterAsync(factory);
        var started = DateTimeOffset.UtcNow;
        Assert.True(await SendNextAsync(factory));
        var pending = await PendingAsync(factory);
        Assert.NotNull(pending);
        Assert.NotNull(pending.PreparedAt);
        Assert.InRange(pending.NextAttemptAt, started.AddSeconds(Math.Min(seconds, 23 * 3600)), pending.PreparedAt.Value.AddHours(23));
        Assert.False(await SendNextAsync(factory));
    }

    [Theory]
    [InlineData("expired", "retry-window-expired")]
    [InlineData("lost-keys", "payload-unavailable")]
    [InlineData("changed-provider", "provider-changed")]
    public async Task UnsafeRetriesStopWithoutAnotherHttpRequest(string scenario, string failureCode)
    {
        var calls = 0;
        await using var first = CreateFactory((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        await RegisterAsync(first);
        Assert.True(await SendNextAsync(first));
        await ChangePendingAsync(first, row =>
        {
            row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            if (scenario == "expired")
            {
                row.PreparedAt = DateTimeOffset.UtcNow.AddHours(-24);
            }
        });

        await using var restarted = CreateFactory((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            if (scenario == "lost-keys")
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            }
            else if (scenario == "changed-provider")
            {
                services.RemoveAll<IEmailDelivery>();
                services.AddHttpClient<IEmailDelivery, MailpitEmailDelivery>();
            }
        }));
        Assert.True(await SendNextAsync(restarted));
        Assert.Equal(1, calls);
        var pending = await PendingAsync(restarted);
        Assert.NotNull(pending?.FailedAt);
        Assert.Equal(failureCode, pending.FailureCode);
        Assert.Null(pending.ProtectedMessage);
        Assert.False(await SendNextAsync(restarted));
    }

    [Theory]
    [InlineData("Resend:ApiKey", "")]
    [InlineData("Resend:ApiKey", "bad\nkey")]
    [InlineData("Resend:From", "")]
    [InlineData("Resend:From", "not-an-address")]
    [InlineData("Resend:From", "sender@example.test\r\nBcc:other@example.test")]
    [InlineData("ConfirmationEmail:PageUrl", "http://localhost:5173/confirm-email")]
    public async Task InvalidProductionConfigurationFailsAtStartup(string key, string value)
    {
        var settings = fixture.Database.Settings;
        settings[key] = value;
        await using var factory = ApiFactory.Create(settings, "Production");
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Theory]
    [InlineData("Production", "Mailpit")]
    [InlineData("Development", "99")]
    public async Task InvalidProviderSelectionFailsAtStartup(string environment, string provider)
    {
        await using var factory = ApiFactory.Create(new() { ["Email:Provider"] = provider }, environment);
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    private WebApplicationFactory<Program> CreateFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback,
        string environment = "Production", Dictionary<string, string?>? settings = null)
    {
        var configuration = fixture.Database.Settings;
        if (environment == "Development")
        {
            configuration["Email:Provider"] = "Resend";
        }

        foreach (var setting in settings ?? [])
        {
            configuration[setting.Key] = setting.Value;
        }

        return ApiFactory.Create(configuration, environment, useTestEmailDelivery: false).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(_protection);
            // Replace only the transport; exercise the application's actual provider registration.
            services.AddHttpClient(nameof(IEmailDelivery))
                .ConfigurePrimaryHttpMessageHandler(() => new CallbackHandler(callback));
        }));
    }

    private static async Task<string> RegisterAsync(WebApplicationFactory<Program> factory, string? email = null, string password = Password)
    {
        email ??= $"resend-{Guid.NewGuid():N}@example.test";
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return email;
    }

    private static async Task<bool> SendNextAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConfirmationEmailDelivery>().SendNextAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<PendingConfirmationEmail?> PendingAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>().PendingConfirmationEmails
            .AsNoTracking().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    private static async Task ChangePendingAsync(WebApplicationFactory<Program> factory, Action<PendingConfirmationEmail> change)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        change(await database.PendingConfirmationEmails.SingleAsync(TestContext.Current.CancellationToken));
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class CallbackHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request, cancellationToken);
    }

    private sealed class RejectQueueDelete : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE FROM identity.\"PendingConfirmationEmails\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Simulated failure after provider acceptance.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
