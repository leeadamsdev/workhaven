using System.Data.Common;
using System.Net;
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

public sealed class ConfirmationEmailDeliveryTests(EmailDeliveryFixture fixture)
    : IClassFixture<EmailDeliveryFixture>, IAsyncLifetime
{
    private const string Password = "a long test passphrase";
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

    public async ValueTask InitializeAsync()
    {
        // This fixture owns an isolated Testcontainers database; each test starts with an empty queue.
        await using var factory = ApiFactory.Create(fixture.Settings);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>().Users
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RegistrationQueuesEmailAndDeliveredLinkConfirmsTheAccount()
    {
        await using var factory = CreateFactory();
        var email = await RegisterAsync(factory);
        Assert.True(await SendNextAsync(factory));
        var link = await GetConfirmationLinkAsync(email);
        Assert.Equal("http://localhost:5173/confirm-email", link.GetLeftPart(UriPartial.Path));
        Assert.Empty(link.Query);
        var parameters = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(link.Fragment[1..]);
        var userId = parameters["userId"].ToString();
        var token = parameters["token"].ToString();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/confirm-email", new { userId, token }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        Assert.True((await database.Users.SingleAsync(TestContext.Current.CancellationToken)).EmailConfirmed);
        Assert.False(await database.PendingConfirmationEmails.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeliveryFailureSurvivesRestartAndCanBeRetried()
    {
        var settings = fixture.Settings;
        settings["Mailpit:BaseUrl"] = "http://127.0.0.1:1";
        string email;
        await using (var failing = CreateFactory(settings))
        {
            email = await RegisterAsync(failing);
            Assert.True(await SendNextAsync(failing));
            await using var scope = failing.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
            var pending = await database.PendingConfirmationEmails.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, pending.Attempts);
            Assert.True(pending.NextAttemptAt > DateTimeOffset.UtcNow);
            Assert.False((await database.Users.SingleAsync(TestContext.Current.CancellationToken)).EmailConfirmed);
            Assert.False(await SendNextAsync(failing));
            pending.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var restarted = CreateFactory();
        Assert.True(await SendNextAsync(restarted));
        Assert.NotNull(await GetConfirmationLinkAsync(email));
        Assert.False(await SendNextAsync(restarted));
    }

    [Fact]
    public async Task RejectedSendIsRetainedForRetry()
    {
        await using var factory = CreateFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddHttpClient<IEmailDelivery, MailpitEmailDelivery>()
                .ConfigurePrimaryHttpMessageHandler(() => new RejectingHandler())));
        await RegisterAsync(factory);
        Assert.True(await SendNextAsync(factory));
        await using var scope = factory.Services.CreateAsyncScope();
        var pending = await scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>()
            .PendingConfirmationEmails.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, pending.Attempts);
    }

    [Fact]
    public async Task RetryRequiresTheOriginalPasswordAndNeverChangesCredentials()
    {
        await using var factory = CreateFactory();
        var email = await RegisterAsync(factory);
        Assert.True(await SendNextAsync(factory));
        using var client = factory.CreateClient();
        using var wrongPassword = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "a different test passphrase" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, wrongPassword.StatusCode);
        Assert.False(await SendNextAsync(factory));

        using var correctPassword = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = Password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, correctPassword.StatusCode);
        Assert.True(await SendNextAsync(factory));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.False(await users.CheckPasswordAsync(user, "a different test passphrase"));
    }

    [Fact]
    public async Task ConfirmationBeforeDeliveryDiscardsThePendingEmail()
    {
        await using var factory = CreateFactory();
        var email = await RegisterAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.True((await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user))).Succeeded);
        }

        Assert.True(await SendNextAsync(factory));
        Assert.False(await SendNextAsync(factory));
        using var client = factory.CreateClient();
        using var repeated = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.False(await SendNextAsync(factory));
    }

    [Fact]
    public async Task AccountCreationRollsBackIfQueueInsertionFails()
    {
        await using var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<WorkhavenIdentityDbContext>(options => options.AddInterceptors(new RejectQueueInsert()))));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "atomic@example.test", password = Password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        Assert.False(await database.Users.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await database.PendingConfirmationEmails.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentDispatchersCannotSendTheSamePendingEmail()
    {
        var blocking = new BlockingDelivery();
        await using var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IEmailDelivery>(blocking)));
        await RegisterAsync(factory);
        var first = SendNextAsync(factory);
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        try
        {
            Assert.False(await SendNextAsync(factory));
        }
        finally
        {
            blocking.Release.TrySetResult();
        }

        Assert.True(await first);
        Assert.Equal(1, blocking.Calls);
    }

    [Fact]
    public async Task BackgroundWorkerDeliversQueuedEmail()
    {
        await using var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHostedService<ConfirmationEmailWorker>()));
        var email = await RegisterAsync(factory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>()
                    .PendingConfirmationEmails.AnyAsync(timeout.Token))
            {
                break;
            }

            await Task.Delay(100, timeout.Token);
        }

        Assert.NotNull(await GetConfirmationLinkAsync(email));
    }

    [Fact]
    public async Task CancellationLeavesTheEmailAvailableForAnotherWorker()
    {
        var blocking = new BlockingDelivery();
        await using (var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IEmailDelivery>(blocking))))
        {
            await RegisterAsync(factory);
            await using var scope = factory.Services.CreateAsyncScope();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var sending = scope.ServiceProvider.GetRequiredService<ConfirmationEmailDelivery>().SendNextAsync(cancellation.Token);
            await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        }

        await using var restarted = CreateFactory();
        await using (var scope = restarted.Services.CreateAsyncScope())
        {
            var pending = await scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>()
                .PendingConfirmationEmails.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, pending.Attempts);
        }

        Assert.True(await SendNextAsync(restarted));
    }

    [Theory]
    [InlineData("Mailpit:BaseUrl", "ftp://localhost")]
    [InlineData("Mailpit:BaseUrl", "http://user:password@localhost")]
    [InlineData("ConfirmationEmail:PageUrl", "http://external.example/confirm-email")]
    [InlineData("ConfirmationEmail:PageUrl", "https://example.test/confirm-email#token")]
    [InlineData("ConfirmationEmail:PageUrl", "https://example.test/confirm-email?next=elsewhere")]
    public async Task InvalidDeliveryConfigurationFailsAtStartup(string key, string value)
    {
        var settings = fixture.Settings;
        settings[key] = value;
        await using var factory = ApiFactory.Create(settings);
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task MissingProductionCredentialsFailAtStartup()
    {
        var settings = fixture.Settings;
        settings["Resend:ApiKey"] = string.Empty;
        await using var factory = ApiFactory.Create(settings, "Production");
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    private WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?>? settings = null) =>
        ApiFactory.Create(settings ?? fixture.Settings).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(_protection);
            services.RemoveAll<IEmailDelivery>();
            services.AddHttpClient<IEmailDelivery, MailpitEmailDelivery>(client => client.Timeout = TimeSpan.FromSeconds(2));
        }));

    private static async Task<string> RegisterAsync(WebApplicationFactory<Program> factory)
    {
        var email = $"delivery-{Guid.NewGuid():N}@example.test";
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = Password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return email;
    }

    private static async Task<bool> SendNextAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConfirmationEmailDelivery>()
            .SendNextAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Uri> GetConfirmationLinkAsync(string email)
    {
        using var client = new HttpClient { BaseAddress = new Uri(fixture.MailpitUrl) };
        using var search = await client.GetFromJsonAsync<JsonDocument>(
            $"/api/v1/search?query={Uri.EscapeDataString($"to:{email}")}", TestContext.Current.CancellationToken);
        Assert.NotNull(search);
        var messageId = search.RootElement.GetProperty("messages")[0].GetProperty("ID").GetString();
        using var message = await client.GetFromJsonAsync<JsonDocument>($"/api/v1/message/{messageId}", TestContext.Current.CancellationToken);
        Assert.NotNull(message);
        Assert.Equal("Confirm your Workhaven email", message.RootElement.GetProperty("Subject").GetString());
        var text = message.RootElement.GetProperty("Text").GetString();
        Assert.NotNull(text);
        var link = text.Split('\n').Single(line => line.StartsWith("http", StringComparison.Ordinal)).Trim();
        var html = message.RootElement.GetProperty("HTML").GetString();
        Assert.NotNull(html);
        Assert.Contains($"href=\"{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(link)}\"", html, StringComparison.Ordinal);
        return new Uri(link);
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class RejectQueueInsert : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO identity.\"PendingConfirmationEmails\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Simulated queue failure.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class BlockingDelivery : IEmailDelivery
    {
        public EmailProvider Provider => EmailProvider.Mailpit;
        public string From => "Workhaven <no-reply@example.test>";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, Guid deliveryId, CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(EmailDeliveryStatus.Accepted);
        }
    }
}
