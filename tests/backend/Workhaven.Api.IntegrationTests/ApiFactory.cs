using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
                .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings)));
    }
}
