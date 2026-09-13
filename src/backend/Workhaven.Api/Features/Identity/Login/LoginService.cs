using Microsoft.AspNetCore.Identity;

namespace Workhaven.Api.Features.Identity.Login;

internal sealed class LoginService(SignInManager<IdentityUser> signIn)
{
    public async Task<bool> LoginAsync(string email, string password)
    {
        var result = await signIn.PasswordSignInAsync(email, password, isPersistent: false, lockoutOnFailure: true);
        // Unknown accounts, incorrect passwords, unconfirmed accounts and lockouts share one response.
        return result.Succeeded;
    }
}
