using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Workhaven.Api.Features.Identity;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed class RegistrationEndpointTests(RegistrationDatabaseFixture fixture)
    : IClassFixture<RegistrationDatabaseFixture>
{
    private const string Endpoint = "/api/auth/register";
    private const string Password = "a long test passphrase";

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task RegistrationPersistsUnconfirmedAccountWithoutSigningIn(string environment)
    {
        var email = $"member-{Guid.NewGuid():N}@example.test";
        await using var factory = ApiFactory.Create(fixture.Database.Settings, environment);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(Endpoint, new
        {
            email = $"  {email}  ",
            password = Password,
            emailConfirmed = true,
            id = "client-supplied-id"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(response.Headers.Contains("Set-Cookie"));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.Equal(email, user.UserName);
        Assert.Equal(email, user.Email);
        Assert.Equal(email.ToUpperInvariant(), user.NormalizedEmail);
        Assert.NotEqual("client-supplied-id", user.Id);
        Assert.False(user.EmailConfirmed);
        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.True(scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value.SignIn.RequireConfirmedEmail);
    }

    [Theory]
    [InlineData(null, Password, "email")]
    [InlineData("", Password, "email")]
    [InlineData("   ", Password, "email")]
    [InlineData("not-an-email", Password, "email")]
    [InlineData("member\n@example.test", Password, "email")]
    [InlineData("member@example.test", null, "password")]
    [InlineData("member@example.test", "", "password")]
    [InlineData("member@example.test", "               ", "password")]
    [InlineData("member@example.test", "fourteen chars", "password")]
    public async Task InvalidFieldsReturnValidationProblemsWithoutCreatingUsers(string? email, string? password, string field)
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await context.Users.CountAsync(cancellationToken);

        using var response = await client.PostAsJsonAsync(Endpoint, new { email, password }, cancellationToken);

        await AssertValidationProblemAsync(response, field);
        Assert.Equal(countBefore, await context.Users.CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData("Development", "member\u0000@example.test")]
    [InlineData("Production", "member\u0000@example.test")]
    [InlineData("Development", "\tmember@example.test")]
    [InlineData("Production", "\tmember@example.test")]
    [InlineData("Development", "member\u007f@example.test")]
    [InlineData("Production", "member\u007f@example.test")]
    [InlineData("Development", "member@example.test\u0085")]
    [InlineData("Production", "member@example.test\u0085")]
    public async Task EmailControlCharactersAreRejectedWithoutCreatingUsers(string environment, string email)
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings, environment);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var countBefore = await context.Users.CountAsync(cancellationToken);

        using var response = await client.PostAsJsonAsync(Endpoint, new { email, password = Password }, cancellationToken);

        await AssertValidationProblemAsync(response, "email");
        Assert.Equal(countBefore, await context.Users.CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData("email")]
    [InlineData("password")]
    public async Task OversizedFieldsAreRejected(string field)
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(Endpoint, new
        {
            email = field == "email" ? new string('a', 245) + "@example.test" : "member@example.test",
            password = field == "password" ? new string('a', 129) : Password
        }, TestContext.Current.CancellationToken);

        await AssertValidationProblemAsync(response, field);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(128)]
    public async Task PasswordLengthBoundariesAndWhitespaceArePreserved(int length)
    {
        var email = $"password-{Guid.NewGuid():N}@example.test";
        var password = " " + new string('a', length - 2) + " ";
        await using var factory = ApiFactory.Create(fixture.Database.Settings);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(Endpoint, new { email, password }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(await users.CheckPasswordAsync(user, password));
        Assert.False(await users.CheckPasswordAsync(user, password.Trim()));
    }

    [Fact]
    public async Task DuplicateRegistrationDoesNotDiscloseOrChangeExistingAccount()
    {
        var email = $"duplicate-{Guid.NewGuid():N}@example.test";
        await using var factory = ApiFactory.Create(fixture.Database.Settings);
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        using var first = await client.PostAsJsonAsync(Endpoint, new { email, password = Password }, cancellationToken);
        using var duplicate = await client.PostAsJsonAsync(Endpoint, new
        {
            email = email.ToUpperInvariant(),
            password = "a different test passphrase"
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(first.StatusCode, duplicate.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(cancellationToken), await duplicate.Content.ReadAsStringAsync(cancellationToken));
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.False(await users.CheckPasswordAsync(user, "a different test passphrase"));
    }

    [Fact]
    public async Task CompetingRegistrationsBothReturnNoContentAndPersistOnlyOneAccount()
    {
        var email = $"race-{Guid.NewGuid():N}@example.test";
        var interceptor = new CompetingInsertsInterceptor();
        await using var factory = ApiFactory.Create(fixture.Database.Settings).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.ConfigureDbContext<WorkhavenIdentityDbContext>(
                options => options.AddInterceptors(interceptor))));
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstRequest = client.PostAsJsonAsync(Endpoint, new { email, password = Password }, cancellationToken);
        var secondRequest = client.PostAsJsonAsync(Endpoint, new { email = email.ToUpperInvariant(), password = Password }, cancellationToken);
        using var first = await firstRequest;
        using var second = await secondRequest;

        Assert.Equal(2, interceptor.Arrivals);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        var normalizedEmail = email.ToUpperInvariant();
        Assert.Equal(1, await context.Users.CountAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken));
    }

    [Fact]
    public async Task DatabaseRejectsDuplicateEmailEvenWithDifferentUsernames()
    {
        var email = $"unique-{Guid.NewGuid():N}@example.test";
        await using var factory = ApiFactory.Create(fixture.Database.Settings);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        var normalizedEmail = email.ToUpperInvariant();
        context.Users.Add(new IdentityUser($"first-{Guid.NewGuid():N}") { Email = email, NormalizedEmail = normalizedEmail });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.Users.Add(new IdentityUser($"second-{Guid.NewGuid():N}") { Email = email, NormalizedEmail = normalizedEmail });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal("EmailIndex", postgresException.ConstraintName);
    }

    [Theory]
    [InlineData("Development", "{", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("Production", "{", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("Development", "null", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("Production", "null", "application/json", HttpStatusCode.BadRequest)]
    [InlineData("Production", "{}", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    public async Task InvalidRequestBodiesReturnProblemDetails(string environment, string body, string contentType, HttpStatusCode status)
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings, environment);
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, contentType);
        using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, status);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task DatabaseFailureReturnsGenericProblemDetails(string environment)
    {
        var settings = fixture.Database.Settings;
        settings["Database:Password"] = "incorrect-database-password";
        await using var factory = ApiFactory.Create(settings, environment);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(Endpoint, new
        {
            email = $"failure-{Guid.NewGuid():N}@example.test",
            password = Password
        }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(settings["Database:Password"]!, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegistrationIsRateLimitedWithoutBlockingHealthChecks()
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings);
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            // An untrusted forwarding header must not bypass the limiter.
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"192.0.2.{attempt + 1}");
            using var response = await client.PostAsJsonAsync(Endpoint, new { }, cancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync(Endpoint, new { }, cancellationToken);
        await AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var health = await client.GetAsync("/health", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string field)
    {
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(field, out var errors));
        Assert.NotEqual(0, errors.GetArrayLength());
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
    }

    private sealed class CompetingInsertsInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public int Arrivals => _arrivals;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO identity.\"AspNetUsers\"", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                {
                    _bothArrived.TrySetResult();
                }

                await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }
}
