using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Workhaven.Api.Features.Identity;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed class IdentityPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlDatabase _database = new();
    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("The test database has not started.");

    public async ValueTask InitializeAsync()
    {
        await _database.Container.StartAsync(TestContext.Current.CancellationToken);
        _factory = ApiFactory.Create(_database.Settings);
        await ApplyMigrationsAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    [Fact]
    public async Task UserAndPasswordSurviveApplicationRestartAndMigrationRerun()
    {
        const string password = "Persistence-test-42!";
        string userId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser("member@example.test") { Email = "member@example.test" };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddClaimAsync(user, new Claim("display_name", "Test member"))).Succeeded);
            userId = user.Id;
        }

        await ApplyMigrationsAsync();

        await using var restartedFactory = ApiFactory.Create(_database.Settings);
        await using var restartedScope = restartedFactory.Services.CreateAsyncScope();
        var restartedUsers = restartedScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var persistedUser = await restartedUsers.FindByNameAsync("MEMBER@EXAMPLE.TEST");

        Assert.NotNull(persistedUser);
        Assert.Equal(userId, persistedUser.Id);
        Assert.Equal("member@example.test", persistedUser.Email);
        Assert.NotNull(persistedUser.PasswordHash);
        Assert.NotEqual(password, persistedUser.PasswordHash);
        Assert.True(await restartedUsers.CheckPasswordAsync(persistedUser, password));
        Assert.False(await restartedUsers.CheckPasswordAsync(persistedUser, "wrong-password"));
        Assert.Contains(await restartedUsers.GetClaimsAsync(persistedUser),
            claim => claim.Type == "display_name" && claim.Value == "Test member");

        Assert.True((await restartedUsers.DeleteAsync(persistedUser)).Succeeded);
        var context = restartedScope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        Assert.False(await context.Users.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await context.UserClaims.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DuplicateNormalizedUsernamesAreRejectedByIdentityAndDatabase()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.True((await users.CreateAsync(new IdentityUser("member@example.test"))).Succeeded);

        var duplicate = await users.CreateAsync(new IdentityUser("MEMBER@example.test"));
        Assert.False(duplicate.Succeeded);
        Assert.Contains(duplicate.Errors, error => error.Code == "DuplicateUserName");

        var context = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        context.Users.Add(new IdentityUser("MEMBER@example.test") { NormalizedUserName = "MEMBER@EXAMPLE.TEST" });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal("UserNameIndex", postgresException.ConstraintName);
    }

    [Fact]
    public async Task StaleUserUpdateDoesNotOverwriteConcurrentChanges()
    {
        await using var firstScope = Factory.Services.CreateAsyncScope();
        var firstManager = firstScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var firstUser = new IdentityUser("member@example.test");
        Assert.True((await firstManager.CreateAsync(firstUser)).Succeeded);

        await using var secondScope = Factory.Services.CreateAsyncScope();
        var secondManager = secondScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var staleUser = await secondManager.FindByIdAsync(firstUser.Id);
        Assert.NotNull(staleUser);

        Assert.True((await firstManager.SetPhoneNumberAsync(firstUser, "01234567890")).Succeeded);
        var staleUpdate = await secondManager.SetPhoneNumberAsync(staleUser, "09876543210");
        Assert.False(staleUpdate.Succeeded);
        Assert.Contains(staleUpdate.Errors, error => error.Code == "ConcurrencyFailure");

        await using var verificationScope = Factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        var persistedUser = await context.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("01234567890", persistedUser.PhoneNumber);
    }

    [Theory]
    [InlineData("CREATE TABLE identity.forbidden (id integer)")]
    [InlineData("ALTER TABLE identity.\"AspNetUsers\" ADD COLUMN forbidden integer")]
    [InlineData("TRUNCATE identity.\"AspNetUsers\"")]
    [InlineData("DELETE FROM public.\"__EFMigrationsHistory\"")]
    public async Task ApplicationAccountCannotManageSchemaOrMigrationHistory(string sql)
    {
        await using var command = Factory.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand(sql);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    private async Task ApplyMigrationsAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkhavenIdentityDbContext>();
        Assert.False(context.Database.HasPendingModelChanges());
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        var cancellationToken = TestContext.Current.CancellationToken;
        await _database.Container.CopyAsync(Encoding.UTF8.GetBytes(script), "/database/migrations.sql", ct: cancellationToken);
        var result = await _database.Container.ExecAsync(["sh", "/database/migrate.sh"], cancellationToken);
        Assert.True(result.ExitCode == 0, result.Stderr);
    }
}
