using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Workhaven.Api.Features.Identity;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed class EmailConfirmationEndpointTests(IdentityDatabaseFixture fixture)
    : IClassFixture<IdentityDatabaseFixture>
{
    private const string Endpoint = "/api/auth/confirm-email";

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ValidTokenConfirmsOnlyItsAccountAndCanBeRepeatedWithoutSigningIn(string environment)
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings, environment);
        var (userId, token) = await CreateAccountAsync(factory);
        var (otherUserId, _) = await CreateAccountAsync(factory);
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        using var first = await client.PostAsJsonAsync(Endpoint, new { userId, token }, cancellationToken);
        using var repeated = await client.PostAsJsonAsync(Endpoint, new { userId, token }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Empty(await first.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(await repeated.Content.ReadAsStringAsync(cancellationToken));
        Assert.False(first.Headers.Contains("Set-Cookie"));
        Assert.False(repeated.Headers.Contains("Set-Cookie"));
        await AssertConfirmedAsync(factory, userId, expected: true);
        await AssertConfirmedAsync(factory, otherUserId, expected: false);

        using var invalidRepeat = await client.PostAsJsonAsync(Endpoint, new { userId, token = "invalid" }, cancellationToken);
        await AssertInvalidConfirmationAsync(invalidRepeat);
        await AssertConfirmedAsync(factory, userId, expected: true);
    }

    [Theory]
    [InlineData("Development", "malformed")]
    [InlineData("Production", "malformed")]
    [InlineData("Development", "tampered")]
    [InlineData("Production", "tampered")]
    [InlineData("Development", "expired")]
    [InlineData("Production", "expired")]
    [InlineData("Development", "wrong-purpose")]
    [InlineData("Production", "wrong-purpose")]
    [InlineData("Development", "revoked")]
    [InlineData("Production", "revoked")]
    public async Task InvalidTokensDoNotConfirmAccount(string environment, string scenario)
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings, environment);
        var (userId, token) = await CreateAccountAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByIdAsync(userId);
            Assert.NotNull(user);
            switch (scenario)
            {
                case "malformed":
                    token = "not-a-valid-token";
                    break;
                case "tampered":
                    var bytes = Convert.FromBase64String(token);
                    bytes[^1] ^= 1;
                    token = Convert.ToBase64String(bytes);
                    break;
                case "expired":
                    // Expire a real token deterministically without sleeping or replacing its validator.
                    scope.ServiceProvider.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>()
                        .Value.TokenLifespan = TimeSpan.FromMinutes(-1);
                    break;
                case "wrong-purpose":
                    token = await users.GeneratePasswordResetTokenAsync(user);
                    break;
                case "revoked":
                    Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
                    break;
            }
        }

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(Endpoint, new { userId, token }, TestContext.Current.CancellationToken);

        await AssertInvalidConfirmationAsync(response);
        await AssertConfirmedAsync(factory, userId, expected: false);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task WrongAccountAndUnknownAccountReturnTheSameError(string environment)
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings, environment);
        var (userId, token) = await CreateAccountAsync(factory);
        var (otherUserId, _) = await CreateAccountAsync(factory);
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        using var wrongAccount = await client.PostAsJsonAsync(Endpoint, new { userId = otherUserId, token }, cancellationToken);
        using var unknownAccount = await client.PostAsJsonAsync(Endpoint, new { userId = Guid.NewGuid().ToString(), token }, cancellationToken);

        await AssertInvalidConfirmationAsync(wrongAccount);
        await AssertInvalidConfirmationAsync(unknownAccount);
        await AssertConfirmedAsync(factory, userId, expected: false);
        await AssertConfirmedAsync(factory, otherUserId, expected: false);
    }

    [Theory]
    [InlineData(null, "token")]
    [InlineData("", "token")]
    [InlineData(" ", "token")]
    [InlineData("user\u0000", "token")]
    [InlineData("user\n", "token")]
    [InlineData("user", null)]
    [InlineData("user", "")]
    [InlineData("user", " ")]
    public async Task MissingOrUnsafeFieldsAreRejectedBeforeDatabaseAccess(string? userId, string? token)
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(Endpoint, new { userId, token }, TestContext.Current.CancellationToken);

        await AssertInvalidConfirmationAsync(response);
    }

    [Theory]
    [InlineData("userId")]
    [InlineData("token")]
    public async Task OversizedFieldsAreRejectedBeforeDatabaseAccess(string field)
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(Endpoint, new
        {
            userId = field == "userId" ? new string('a', 129) : "user",
            token = field == "token" ? new string('a', 4097) : "token"
        }, TestContext.Current.CancellationToken);

        await AssertInvalidConfirmationAsync(response);
    }

    [Theory]
    [InlineData("Development", "{", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("Production", "{", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("Development", "null", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("Production", "null", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("Development", "{}", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("Production", "{}", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    public async Task InvalidRequestBodiesReturnProblemDetails(string environment, string body, string contentType, HttpStatusCode status)
    {
        await using var factory = ApiFactory.Create(environment: environment);
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, contentType);
        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, status);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task GetCannotConfirmAnAccount(string environment)
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings, environment);
        var (userId, token) = await CreateAccountAsync(factory);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"{Endpoint}?userId={userId}&token={Uri.EscapeDataString(token)}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed);
        await AssertConfirmedAsync(factory, userId, expected: false);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task DatabaseFailureReturnsGenericProblemDetails(string environment)
    {
        await using var factory = ApiFactory.Create(environment: environment);
        using var client = factory.CreateClient();
        const string token = "sensitive-confirmation-token";
        using var response = await client.PostAsJsonAsync(Endpoint, new
        {
            userId = Guid.NewGuid().ToString(),
            token
        }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ConfirmationRateLimitIsIndependentOfRegistrationAndHealth(string environment)
    {
        await using var factory = ApiFactory.Create(environment: environment);
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"192.0.2.{attempt + 1}");
            using var response = await client.PostAsJsonAsync(Endpoint, new { }, cancellationToken);
            await AssertInvalidConfirmationAsync(response);
        }

        using var rejected = await client.PostAsJsonAsync(Endpoint, new { }, cancellationToken);
        await AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var registration = await client.PostAsJsonAsync("/api/auth/register", new { }, cancellationToken);
        await AssertProblemAsync(registration, HttpStatusCode.BadRequest);
        using var health = await client.GetAsync("/health", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task PersistedKeysAllowConfirmationAfterApplicationRestart(string environment)
    {
        var keyDirectory = Directory.CreateTempSubdirectory("workhaven-test-keys-");
        try
        {
            string userId;
            string token;
            await using (var first = CreateFactoryWithPersistedKeys(keyDirectory, environment))
            {
                (userId, token) = await CreateAccountAsync(first);
            }

            Assert.NotEmpty(keyDirectory.GetFiles("key-*.xml"));
            await using var restarted = CreateFactoryWithPersistedKeys(keyDirectory, environment);
            using var client = restarted.CreateClient();
            using var response = await client.PostAsJsonAsync(Endpoint, new { userId, token }, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            await AssertConfirmedAsync(restarted, userId, expected: true);
        }
        finally
        {
            keyDirectory.Delete(recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateFactoryWithPersistedKeys(DirectoryInfo keyDirectory, string environment) =>
        ApiFactory.Create(fixture.Database.Settings, environment).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDataProtectionProvider>();
                services.AddDataProtection().PersistKeysToFileSystem(keyDirectory);
            }));

    private static async Task<(string UserId, string Token)> CreateAccountAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var email = $"confirmation-{Guid.NewGuid():N}@example.test";
        var user = new IdentityUser(email) { Email = email };
        Assert.True((await users.CreateAsync(user, "a long test passphrase")).Succeeded);
        Assert.False(user.EmailConfirmed);
        return (user.Id, await users.GenerateEmailConfirmationTokenAsync(user));
    }

    private static async Task AssertConfirmedAsync(WebApplicationFactory<Program> factory, string userId, bool expected)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        var user = await context.Users.AsNoTracking().SingleAsync(user => user.Id == userId, TestContext.Current.CancellationToken);
        Assert.Equal(expected, user.EmailConfirmed);
    }

    private static async Task AssertInvalidConfirmationAsync(HttpResponseMessage response)
    {
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Invalid email confirmation", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("The confirmation link is invalid or has expired.", body.RootElement.GetProperty("detail").GetString());
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
    }
}
