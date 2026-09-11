using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task GetHealthReturnsOkWithHealthyStatus()
    {
        await using var factory = CreateFactory();
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
        await using var factory = CreateFactory(new Dictionary<string, string?>
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
        var appPassword = $"test;'\"\\={Guid.NewGuid():N}";
        const string initScriptPath = "/docker-entrypoint-initdb.d/10-workhaven-app.sh";
        var initScript = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Postgres", "init-app-user.sh"), cancellationToken);
        await using var postgres = new PostgreSqlBuilder("postgres:18.6-trixie")
            .WithDatabase("workhaven")
            .WithUsername("postgres")
            .WithEnvironment("POSTGRES_APP_PASSWORD", appPassword)
            .WithEnvironment("POSTGRES_INITDB_ARGS", "--auth-host=scram-sha-256")
            .WithResourceMapping(initScript, initScriptPath)
            .Build();
        await postgres.StartAsync(cancellationToken);

        var repeatedInitialization = await postgres.ExecAsync(["sh", initScriptPath], cancellationToken);
        Assert.Equal(0, repeatedInitialization.ExitCode);

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString());
        var settings = new Dictionary<string, string?>
        {
            ["Database:Host"] = connectionString.Host,
            ["Database:Port"] = connectionString.Port.ToString(CultureInfo.InvariantCulture),
            ["Database:Name"] = connectionString.Database,
            ["Database:Username"] = "workhaven_app",
            ["Database:Password"] = appPassword
        };
        await using var factory = CreateFactory(settings);
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
        await using (var invalidFactory = CreateFactory(invalidCredentials))
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

    private static WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Host"] = "127.0.0.1",
            ["Database:Port"] = "1",
            ["Database:Name"] = "workhaven",
            ["Database:Username"] = "workhaven_app",
            ["Database:Password"] = "unused"
        };
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings)));
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
