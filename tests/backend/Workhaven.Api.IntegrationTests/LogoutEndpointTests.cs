using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Xunit;
using static Workhaven.Api.IntegrationTests.AuthenticationTestSupport;

namespace Workhaven.Api.IntegrationTests;

public sealed class LogoutEndpointTests(IdentityDatabaseFixture fixture) : IClassFixture<IdentityDatabaseFixture>
{
    [Theory]
    [InlineData("Development", "http://localhost", "Workhaven.Auth", false)]
    [InlineData("Development", "https://localhost", "Workhaven.Auth", true)]
    [InlineData("Production", "https://localhost", "__Host-Workhaven.Auth", true)]
    public async Task LogoutClearsTheBrowserSessionAndAllowsSigningInAgain(string environment, string origin, string cookieName, bool secure)
    {
        await using var factory = CreateFactory(environment);
        var user = await CreateUserAsync(factory);
        using var client = Client(factory, origin);
        await SignInAsync(client, user);
        await AssertAuthenticationAsync(client, HttpStatusCode.OK);

        using var response = await LogoutAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(response.Headers.CacheControl?.NoStore);
        var cookies = SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList());
        var cookie = Assert.Single(cookies, cookie => cookie.Name == cookieName);
        Assert.Equal(string.Empty, cookie.Value.ToString());
        Assert.True(cookie.Expires < DateTimeOffset.UtcNow);
        Assert.Equal("/", cookie.Path.ToString());
        Assert.False(cookie.Domain.HasValue);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(secure, cookie.Secure);
        await AssertAuthenticationAsync(client, HttpStatusCode.Unauthorized);

        // Identity changes invalidate request tokens; a fresh anonymous token also permits a repeated logout.
        await SetCsrfAsync(client);
        using var repeated = await LogoutAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        await SignInAsync(client, user);
        await AssertAuthenticationAsync(client, HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("other-browser")]
    [InlineData("before-login")]
    public async Task InvalidCsrfCannotSignTheBrowserOut(string scenario)
    {
        await using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var client = Client(factory);
        var anonymousToken = await SetCsrfAsync(client);
        using var login = await LoginAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await SetCsrfAsync(client);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        if (scenario == "invalid")
        {
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "invalid");
        }
        else if (scenario == "other-browser")
        {
            using var other = Client(factory);
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", await SetCsrfAsync(other));
        }
        else if (scenario == "before-login")
        {
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", anonymousToken);
        }

        using var response = await LogoutAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await AssertAuthenticationAsync(client, HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCannotSignTheBrowserOut()
    {
        await using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var client = Client(factory);
        await SignInAsync(client, user);
        using var response = await client.GetAsync("/api/auth/logout", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        await AssertAuthenticationAsync(client, HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnonymousLogoutStillRequiresCsrfAndIsSafeToRepeat()
    {
        await using var factory = CreateFactory();
        using var client = Client(factory);
        using var missing = await LogoutAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        await SetCsrfAsync(client);
        using var first = await LogoutAsync(client);
        using var repeated = await LogoutAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        await AssertAuthenticationAsync(client, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LogoutLeavesOtherBrowsersAndAccountCredentialsIntact()
    {
        await using var factory = CreateFactory();
        var user = await CreateUserAsync(factory);
        using var first = Client(factory);
        using var second = Client(factory);
        await SignInAsync(first, user);
        await SignInAsync(second, user);
        using var response = await LogoutAsync(first);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertAuthenticationAsync(first, HttpStatusCode.Unauthorized);
        await AssertAuthenticationAsync(second, HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var stored = await users.FindByIdAsync(user.Id);
        Assert.NotNull(stored);
        Assert.True(stored.EmailConfirmed);
        Assert.Equal(user.SecurityStamp, stored.SecurityStamp);
        Assert.True(await users.CheckPasswordAsync(stored, Password));
    }

    private WebApplicationFactory<Program> CreateFactory(string environment = "Development") =>
        AuthenticationTestSupport.CreateFactory(fixture.Database.Settings, new EphemeralDataProtectionProvider(), environment);

    private static async Task SignInAsync(HttpClient client, IdentityUser user)
    {
        await SetCsrfAsync(client);
        using var response = await LoginAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await SetCsrfAsync(client);
    }

    private static Task<HttpResponseMessage> LogoutAsync(HttpClient client) =>
        client.PostAsync("/api/auth/logout", content: null, TestContext.Current.CancellationToken);

    private static async Task AssertAuthenticationAsync(HttpClient client, HttpStatusCode expected)
    {
        using var response = await client.GetAsync("/test/auth", TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
