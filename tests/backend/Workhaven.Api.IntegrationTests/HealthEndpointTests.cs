using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task GetHealthReturnsOkWithHealthyStatus()
    {
        await using var factory = ApiFactory.Create();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var cancellationToken = TestContext.Current.CancellationToken;

        using var response = await client.GetAsync("/health", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("Host", null)]
    [InlineData("Port", "0")]
    [InlineData("Port", "65536")]
    [InlineData("Name", "")]
    [InlineData("Username", " ")]
    [InlineData("Password", "")]
    public async Task StartupRejectsInvalidDatabaseConfiguration(string property, string? value)
    {
        await using var factory = ApiFactory.Create(new Dictionary<string, string?>
        {
            [$"Database:{property}"] = value
        });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(exception.Failures, failure => failure.Contains(property, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadinessTracksDatabaseAvailabilityAndRejectsInvalidCredentials()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlDatabase();
        var postgres = database.Container;
        await postgres.StartAsync(cancellationToken);

        var repeatedInitialization = await postgres.ExecAsync(["sh", PostgreSqlDatabase.InitScriptPath], cancellationToken);
        Assert.Equal(0, repeatedInitialization.ExitCode);

        var settings = database.Settings;
        await using var factory = ApiFactory.Create(settings);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await AssertHealthAsync(client, "/health/ready", HttpStatusCode.OK, "Healthy");

        await using (var command = factory.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand("""
            SELECT NOT (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)
                AND NOT has_schema_privilege(current_user, 'public', 'CREATE')
            FROM pg_roles WHERE rolname = current_user
            """))
        {
            Assert.Equal(true, await command.ExecuteScalarAsync(cancellationToken));
        }

        var invalidCredentials = new Dictionary<string, string?>(settings)
        {
            ["Database:Password"] = "incorrect-password"
        };
        await using (var invalidFactory = ApiFactory.Create(invalidCredentials))
        using (var invalidClient = invalidFactory.CreateClient())
        {
            await AssertHealthAsync(invalidClient, "/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy");
            await AssertHealthAsync(invalidClient, "/health", HttpStatusCode.OK, "Healthy");
        }

        // Pausing preserves the mapped port and exercises the health-check timeout.
        await postgres.PauseAsync(cancellationToken);
        try
        {
            var readinessTimer = Stopwatch.StartNew();
            await AssertHealthAsync(client, "/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy");
            Assert.True(readinessTimer.Elapsed < TimeSpan.FromSeconds(8));
            await AssertHealthAsync(client, "/health", HttpStatusCode.OK, "Healthy");
        }
        finally
        {
            await postgres.UnpauseAsync(CancellationToken.None);
        }

        await AssertHealthAsync(client, "/health/ready", HttpStatusCode.OK, "Healthy");
    }

    private static async Task AssertHealthAsync(HttpClient client, string path, HttpStatusCode statusCode, string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(body, await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
