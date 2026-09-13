using Microsoft.AspNetCore.Identity;

namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal sealed class EmailConfirmationService(UserManager<IdentityUser> users)
{
    public async Task<bool> ConfirmAsync(string userId, string token)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return false;
        }

        // Already-confirmed accounts must still present a valid token.
        return (await users.ConfirmEmailAsync(user, token)).Succeeded;
    }
}
