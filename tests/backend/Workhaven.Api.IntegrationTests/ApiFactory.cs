using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workhaven.Api.Features.Identity.EmailConfirmation;
using Workhaven.Api.Infrastructure.Email;

namespace Workhaven.Api.IntegrationTests;

internal static class ApiFactory
{
    public static WebApplicationFactory<Program> Create(
        Dictionary<string, string?>? overrides = null, string environment = "Development")
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
            builder.UseEnvironment(environment)
                .ConfigureServices(services =>
                {
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    var worker = services.SingleOrDefault(descriptor => descriptor.ImplementationType == typeof(ConfirmationEmailWorker));
                    if (worker is not null)
                    {
                        services.Remove(worker);
                    }

                    services.RemoveAll<IEmailDelivery>();
                    services.AddSingleton<IEmailDelivery, TestEmailDelivery>();
                })
                .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings)));
    }

    private sealed class TestEmailDelivery : IEmailDelivery
    {
        public Task SendAsync(string recipient, string subject, string text, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test must explicitly configure email delivery before sending.");
    }
}
