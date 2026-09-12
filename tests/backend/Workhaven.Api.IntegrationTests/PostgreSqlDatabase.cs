using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Workhaven.Api.Features.Identity;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

internal sealed class PostgreSqlDatabase : IAsyncDisposable
{
    public const string InitScriptPath = "/docker-entrypoint-initdb.d/10-workhaven-app.sh";

    private readonly string _appPassword = $"test;'\"\\={Guid.NewGuid():N}";

    public PostgreSqlContainer Container { get; }

    public PostgreSqlDatabase()
    {
        var initScript = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Postgres", "init-app-user.sh"));
        Container = new PostgreSqlBuilder("postgres:18.6-trixie")
            .WithDatabase("workhaven")
            .WithUsername("postgres")
            .WithEnvironment("POSTGRES_APP_PASSWORD", _appPassword)
            .WithEnvironment("POSTGRES_INITDB_ARGS", "--auth-host=scram-sha-256")
            .WithResourceMapping(initScript, InitScriptPath)
            .WithResourceMapping(initScript, "/database/init-app-user.sh")
            .WithResourceMapping(
                File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Postgres", "migrate.sh")), "/database/migrate.sh")
            .Build();
    }

    public Dictionary<string, string?> Settings
    {
        get
        {
            var connectionString = new NpgsqlConnectionStringBuilder(Container.GetConnectionString());
            return new Dictionary<string, string?>
            {
                ["Database:Host"] = connectionString.Host,
                ["Database:Port"] = connectionString.Port.ToString(CultureInfo.InvariantCulture),
                ["Database:Name"] = connectionString.Database,
                ["Database:Username"] = "workhaven_app",
                ["Database:Password"] = _appPassword
            };
        }
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        Assert.False(context.Database.HasPendingModelChanges());
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        var cancellationToken = TestContext.Current.CancellationToken;
        await Container.CopyAsync(Encoding.UTF8.GetBytes(script), "/database/migrations.sql", ct: cancellationToken);
        var result = await Container.ExecAsync(["sh", "/database/migrate.sh"], cancellationToken);
        Assert.True(result.ExitCode == 0, result.Stderr);
    }

    public ValueTask DisposeAsync() => Container.DisposeAsync();
}
