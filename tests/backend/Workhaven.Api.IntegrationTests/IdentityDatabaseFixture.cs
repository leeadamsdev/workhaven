using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed class IdentityDatabaseFixture : IAsyncLifetime
{
    internal PostgreSqlDatabase Database { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Database.Container.StartAsync(TestContext.Current.CancellationToken);
        await using var factory = ApiFactory.Create(Database.Settings);
        await Database.ApplyMigrationsAsync(factory.Services);
    }

    public ValueTask DisposeAsync() => Database.DisposeAsync();
}
