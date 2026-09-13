using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Workhaven.Api.Features.Identity.CurrentUser;
using Xunit;
using static Workhaven.Api.IntegrationTests.AuthenticationTestSupport;

namespace Workhaven.Api.IntegrationTests;

public sealed class CurrentUserEndpointTests(IdentityDatabaseFixture fixture) : IClassFixture<IdentityDatabaseFixture>
{
    [Theory]
    [InlineData("Development", "http://localhost")]
    [InlineData("Development", "https://localhost")]
    [InlineData("Production", "https://localhost")]
    public async Task SignedInBrowserReceivesOnlyItsAccountIdAndEmail(string environment, string origin)
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings, environment);
        var user = await CreateUserAsync(factory);
        using var client = Client(factory, origin);
        await SignInAsync(client, user);

        using var response = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(["email", "id"], body.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(user.Id, body.RootElement.GetProperty("id").GetString());
        Assert.Equal(user.Email, body.RootElement.GetProperty("email").GetString());
    }

    [Theory]
    [InlineData("Development", "http://localhost")]
    [InlineData("Production", "https://localhost")]
    public async Task AnonymousBrowserReceivesAnUncacheableUnauthorizedResponse(string environment, string origin)
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings, environment);
        using var client = Client(factory, origin);
        using var response = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AccountSelectionComesFromTheSessionRegardlessOfQueryParameters()
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings);
        var firstUser = await CreateUserAsync(factory);
        var secondUser = await CreateUserAsync(factory);
        using var first = Client(factory);
        using var second = Client(factory);
        await SignInAsync(first, firstUser);
        await SignInAsync(second, secondUser);

        var firstAccount = await first.GetFromJsonAsync<CurrentUserResponse>(
            $"/api/auth/me?userId={secondUser.Id}", TestContext.Current.CancellationToken);
        var secondAccount = await second.GetFromJsonAsync<CurrentUserResponse>(
            $"/api/auth/me?userId={firstUser.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(new CurrentUserResponse(firstUser.Id, firstUser.Email), firstAccount);
        Assert.Equal(new CurrentUserResponse(secondUser.Id, secondUser.Email), secondAccount);
    }

    [Fact]
    public async Task DeletedAccountCannotBeReturnedFromAnExistingCookie()
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings);
        var user = await CreateUserAsync(factory);
        using var client = Client(factory);
        await SignInAsync(client, user);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var stored = await users.FindByIdAsync(user.Id);
            Assert.NotNull(stored);
            Assert.True((await users.DeleteAsync(stored)).Succeeded);
        }

        using var response = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AccountDetailsReflectTheDatabaseRatherThanOldCookieClaims()
    {
        await using var factory = ApiFactory.Create(fixture.Database.Settings);
        var user = await CreateUserAsync(factory);
        using var client = Client(factory);
        await SignInAsync(client, user);
        var updatedEmail = $"updated-{Guid.NewGuid():N}@example.test";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var stored = await users.FindByIdAsync(user.Id);
            Assert.NotNull(stored);
            stored.Email = updatedEmail;
            Assert.True((await users.UpdateAsync(stored)).Succeeded);
        }

        var account = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(new CurrentUserResponse(user.Id, updatedEmail), account);
    }

    private static async Task SignInAsync(HttpClient client, IdentityUser user)
    {
        await SetCsrfAsync(client);
        using var response = await LoginAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
    }
}
