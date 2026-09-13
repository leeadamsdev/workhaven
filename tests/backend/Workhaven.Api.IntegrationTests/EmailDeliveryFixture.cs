using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed class EmailDeliveryFixture : IAsyncLifetime
{
    internal PostgreSqlDatabase Database { get; } = new();

    internal IContainer Mailpit { get; } = new ContainerBuilder("axllent/mailpit:v1.31.1")
        .WithPortBinding(8025, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/readyz")))
        .Build();

    internal string MailpitUrl => $"http://{Mailpit.Hostname}:{Mailpit.GetMappedPublicPort(8025)}";

    internal Dictionary<string, string?> Settings
    {
        get
        {
            var settings = Database.Settings;
            settings["Mailpit:BaseUrl"] = MailpitUrl;
            return settings;
        }
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Database.Container.StartAsync(TestContext.Current.CancellationToken),
            Mailpit.StartAsync(TestContext.Current.CancellationToken));
        await using var factory = ApiFactory.Create(Database.Settings);
        await Database.ApplyMigrationsAsync(factory.Services);
    }

    public async ValueTask DisposeAsync()
    {
        await Mailpit.DisposeAsync();
        await Database.DisposeAsync();
    }
}
