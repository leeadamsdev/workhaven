using System.Globalization;
using Npgsql;
using Testcontainers.PostgreSql;

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

    public ValueTask DisposeAsync() => Container.DisposeAsync();
}
