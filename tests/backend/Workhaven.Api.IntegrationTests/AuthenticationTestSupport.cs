using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

internal static class AuthenticationTestSupport
{
    public const string Password = " a long test passphrase ";

    public static WebApplicationFactory<Program> CreateFactory(
        Dictionary<string, string?> settings, IDataProtectionProvider protection, string environment = "Development") =>
        ApiFactory.Create(settings, environment).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton(protection)));

    public static HttpClient Client(WebApplicationFactory<Program> factory, string origin = "http://localhost") =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(origin), AllowAutoRedirect = false });

    public static async Task<IdentityUser> CreateUserAsync(WebApplicationFactory<Program> factory, bool confirmed = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var email = $"session-{Guid.NewGuid():N}@example.test";
        var user = new IdentityUser(email) { Email = email, EmailConfirmed = confirmed };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        return user;
    }

    public static async Task<string> SetCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf", TestContext.Current.CancellationToken);
        Assert.NotNull(csrf);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.RequestToken);
        return csrf.RequestToken;
    }

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string? email, string? password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);

    internal sealed record CsrfResponse(string RequestToken);

}
