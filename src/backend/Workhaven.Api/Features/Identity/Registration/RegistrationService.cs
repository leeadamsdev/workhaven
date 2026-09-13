using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Workhaven.Api.Features.Identity.EmailConfirmation;
using Workhaven.Api.Infrastructure.Email;

namespace Workhaven.Api.Features.Identity.Registration;

internal sealed class RegistrationService(
    UserManager<IdentityUser> users, WorkhavenIdentityDbContext database, IEmailDelivery? delivery = null)
{
    public async Task<IdentityResult> RegisterAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (delivery is null)
        {
            return IdentityResult.Failed(new IdentityError { Code = "EmailDeliveryUnavailable" });
        }

        var user = new IdentityUser(email) { Email = email };
        var result = await CreateAccountAsync(user, password, cancellationToken);
        if (result.Succeeded || !result.Errors.All(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return result;
        }

        var existing = await users.FindByEmailAsync(email);
        // Only the original password can request another confirmation, preventing registration from
        // confirming an account whose password was chosen by someone else. Never replace credentials.
        if (existing is not null && !existing.EmailConfirmed && await users.CheckPasswordAsync(existing, password))
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity."PendingConfirmationEmails" ("UserId", "NextAttemptAt", "Attempts")
                VALUES ({existing.Id}, {DateTimeOffset.UtcNow}, 0)
                ON CONFLICT ("UserId") DO NOTHING
                """, cancellationToken);
        }

        return IdentityResult.Success;
    }

    private async Task<IdentityResult> CreateAccountAsync(IdentityUser user, string password, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            SchemaName: "identity",
            TableName: "AspNetUsers",
            ConstraintName: "EmailIndex" or "UserNameIndex"
        })
        {
            // A competing registration can insert after Identity's uniqueness checks.
            database.Entry(user).State = EntityState.Detached;
            return IdentityResult.Failed(users.ErrorDescriber.DuplicateEmail(user.Email!));
        }

        if (result.Succeeded)
        {
            database.PendingConfirmationEmails.Add(new PendingConfirmationEmail { UserId = user.Id });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }
}
