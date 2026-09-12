using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Workhaven.Api.Features.Identity.Registration;

internal sealed class RegistrationService(UserManager<IdentityUser> users)
{
    public async Task<IdentityResult> RegisterAsync(string email, string password)
    {
        var user = new IdentityUser(email) { Email = email };
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
            return IdentityResult.Success;
        }

        // Keep the response identical for existing accounts; never change their credentials.
        return result.Succeeded || result.Errors.All(error => error.Code is "DuplicateEmail" or "DuplicateUserName")
            ? IdentityResult.Success
            : result;
    }
}
