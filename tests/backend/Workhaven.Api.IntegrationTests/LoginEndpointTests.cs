using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed class LoginEndpointTests(IdentityDatabaseFixture fixture) : IClassFixture<IdentityDatabaseFixture>
{
    private const string Password = " a long test passphrase ";
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

    [Theory]
    [InlineData("Development", "http://localhost", "Workhaven.Auth", "Workhaven.Csrf", false)]
    [InlineData("Development", "https://localhost", "Workhaven.Auth", "Workhaven.Csrf", true)]
    [InlineData("Production", "https://localhost", "__Host-Workhaven.Auth", "__Host-Workhaven.Csrf", true)]
    public async Task ConfirmedAccountReceivesASecureSessionThatAuthenticatesLaterRequests(
        string environment, string origin, string authName, string csrfName, bool secure)
    {
        await using var factory = CreateFactory(environment);
        var user = await CreateUserAsync(factory);
        using var client = Client(factory, origin);
        using var csrfResponse = await client.GetAsync("/api/auth/csrf", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        Assert.True(csrfResponse.Headers.CacheControl?.NoStore);
        var csrfCookie = Assert.Single(csrfResponse.Headers.GetValues("Set-Cookie"));
        AssertCookie(csrfCookie, csrfName, "strict", secure);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(csrf);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.RequestToken);

        using var response = await LoginAsync(client, $"  {user.Email!.ToUpperInvariant()}  ", Password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        AssertCookie(cookie, authName, "lax", secure);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);
        using var authenticated = await client.GetAsync("/test/auth", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.Equal(user.Id, await authenticated.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Tokens issued before login must be refreshed after the browser's identity changes.
        using var staleCsrf = await LoginAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.BadRequest, staleCsrf.StatusCode);
        await SetCsrfAsync(client);
        using var repeated = await LoginAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrong-password")]
    [InlineData("unconfirmed")]
    [InlineData("locked")]
    public async Task RejectedCredentialsHaveTheSameResponseAndNeverIssueAnAuthCookie(string scenario)
    {
        await using var factory = CreateFactory();
        var user = await CreateUserAsync(factory, confirmed: scenario != "unconfirmed");
        if (scenario == "locked")
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var stored = await users.FindByIdAsync(user.Id);
            Assert.NotNull(stored);
            Assert.True((await users.SetLockoutEndDateAsync(stored, DateTimeOffset.UtcNow.AddMinutes(5))).Succeeded);
        }

        using var client = Client(factory);
        await SetCsrfAsync(client);
        using var response = await LoginAsync(client, scenario == "unknown" ? "unknown@example.test" : user.Email,
            scenario == "wrong-password" ? "a different password" : Password);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(new ProblemResponse("Sign-in failed", "Unable to sign in with these credentials."), problem);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var protectedResponse = await client.GetAsync("/test/auth", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
        Assert.Null(protectedResponse.Headers.Location);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("different-browser")]
    public async Task CsrfRejectionDoesNotAttemptThePasswordOrIssueAnAuthCookie(string scenario)
    {
        await using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var client = Client(factory);
        if (scenario == "invalid")
        {
            await SetCsrfAsync(client);
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "invalid");
        }
        else if (scenario == "different-browser")
        {
            using var other = Client(factory);
            var token = await SetCsrfAsync(other);
            await SetCsrfAsync(client);
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        }

        using var response = await LoginAsync(client, user.Email, "wrong password");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(0, (await ReadUserAsync(factory, user.Id)).AccessFailedCount);
    }

    [Theory]
    [InlineData(null, Password)]
    [InlineData("not-an-email", Password)]
    [InlineData("\tmember@example.test", Password)]
    [InlineData("member@example.test", null)]
    [InlineData("member@example.test", "")]
    public async Task MalformedCredentialsAreRejected(string? email, string? password)
    {
        await using var factory = CreateFactory();
        using var client = Client(factory);
        await SetCsrfAsync(client);
        using var response = await LoginAsync(client, email, password);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task FailedAttemptsLockTheAccountAcrossApplicationRestarts()
    {
        IdentityUser user;
        await using (var factory = CreateFactory())
        {
            user = await CreateUserAsync(factory);
            using var client = Client(factory);
            await SetCsrfAsync(client);
            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var failed = await LoginAsync(client, user.Email, "wrong password");
                Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            }
        }

        await using var restarted = CreateFactory();
        var locked = await ReadUserAsync(restarted, user.Id);
        Assert.True(locked.LockoutEnd > DateTimeOffset.UtcNow);
        using var restartedClient = Client(restarted);
        await SetCsrfAsync(restartedClient);
        using var blocked = await LoginAsync(restartedClient, user.Email, Password);
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);

        await using (var scope = restarted.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var stored = await users.FindByIdAsync(user.Id);
            Assert.NotNull(stored);
            Assert.True((await users.SetLockoutEndDateAsync(stored, DateTimeOffset.UtcNow.AddMinutes(-1))).Succeeded);
        }

        using var success = await LoginAsync(restartedClient, user.Email, Password);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
    }

    [Fact]
    public async Task SuccessfulLoginResetsEarlierFailedAttempts()
    {
        await using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var client = Client(factory);
        await SetCsrfAsync(client);
        using var failure = await LoginAsync(client, user.Email, "wrong password");
        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        Assert.Equal(1, (await ReadUserAsync(factory, user.Id)).AccessFailedCount);
        using var success = await LoginAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.Equal(0, (await ReadUserAsync(factory, user.Id)).AccessFailedCount);
    }

    [Fact]
    public async Task OversizedCredentialsAreRejectedWithoutAttemptingThePassword()
    {
        await using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var client = Client(factory);
        await SetCsrfAsync(client);
        using var longPassword = await LoginAsync(client, user.Email, new string('a', 129));
        using var longEmail = await LoginAsync(client, new string('a', 250) + "@example.test", Password);
        Assert.Equal(HttpStatusCode.Unauthorized, longPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, longEmail.StatusCode);
        Assert.Equal(0, (await ReadUserAsync(factory, user.Id)).AccessFailedCount);
    }

    [Fact]
    public async Task LoginRateLimitBoundsUnknownAccountAttempts()
    {
        await using var factory = CreateFactory();
        using var client = Client(factory);
        await SetCsrfAsync(client);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await LoginAsync(client, "unknown@example.test", Password);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var limited = await LoginAsync(client, "unknown@example.test", Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.False(limited.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task CookieSurvivesRestartButIsRejectedAfterSecurityStampChanges()
    {
        IdentityUser user;
        string cookie;
        await using (var factory = CreateFactory())
        {
            user = await CreateUserAsync(factory);
            using var client = Client(factory);
            await SetCsrfAsync(client);
            using var response = await LoginAsync(client, user.Email, Password);
            cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];
        }

        await using var restarted = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));
        using var authenticated = Client(restarted);
        authenticated.DefaultRequestHeaders.Add("Cookie", cookie);
        using var before = await authenticated.GetAsync("/test/auth", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        await using (var scope = restarted.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var stored = await users.FindByIdAsync(user.Id);
            Assert.NotNull(stored);
            Assert.True((await users.UpdateSecurityStampAsync(stored)).Succeeded);
        }

        using var after = await authenticated.GetAsync("/test/auth", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task ExpiredOrTamperedCookiesCannotAuthenticate()
    {
        var clock = new TestClock();
        await using var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock)));
        var user = await CreateUserAsync(factory);
        using var client = Client(factory);
        await SetCsrfAsync(client);
        using var response = await LoginAsync(client, user.Email, Password);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var tampered = Client(factory);
        tampered.DefaultRequestHeaders.Add("Cookie", cookie + "tampered");
        using var rejected = await tampered.GetAsync("/test/auth", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        clock.UtcNow = clock.UtcNow.AddHours(9);
        using var expired = await client.GetAsync("/test/auth", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory(string environment = "Development") =>
        ApiFactory.Create(fixture.Database.Settings, environment).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(_protection);
            services.AddTransient<IStartupFilter, AuthenticationProbe>();
        }));

    private static HttpClient Client(WebApplicationFactory<Program> factory, string origin = "http://localhost") =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(origin), AllowAutoRedirect = false });

    private static async Task<IdentityUser> CreateUserAsync(WebApplicationFactory<Program> factory, bool confirmed = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var email = $"login-{Guid.NewGuid():N}@example.test";
        var user = new IdentityUser(email) { Email = email, EmailConfirmed = confirmed };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        return user;
    }

    private static async Task<IdentityUser> ReadUserAsync(WebApplicationFactory<Program> factory, string id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return Assert.IsType<IdentityUser>(await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().FindByIdAsync(id));
    }

    private static async Task<string> SetCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf", TestContext.Current.CancellationToken);
        Assert.NotNull(csrf);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.RequestToken);
        return csrf.RequestToken;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string? email, string? password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);

    private static void AssertCookie(string cookie, string name, string sameSite, bool secure)
    {
        Assert.StartsWith(name + "=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=" + sameSite, cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(secure, cookie.Contains("; secure", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record CsrfResponse(string RequestToken);
    private sealed record ProblemResponse(string Title, string Detail);

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class AuthenticationProbe : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path != "/test/auth")
                {
                    await continuation(context);
                    return;
                }

                // Exercise the real cookie handler without adding a test-only production endpoint.
                var result = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
                if (!result.Succeeded)
                {
                    await context.ChallengeAsync(IdentityConstants.ApplicationScheme);
                    return;
                }

                await context.Response.WriteAsync(result.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!, context.RequestAborted);
            });
            next(app);
        };
    }
}
