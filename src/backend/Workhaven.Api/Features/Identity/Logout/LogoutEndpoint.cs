using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Workhaven.Api.Features.Identity.Logout;

internal static class LogoutEndpoint
{
    public static RouteHandlerBuilder MapLogout(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/auth/logout", HandleAsync).AddEndpointFilter<CsrfValidationFilter>();

    private static async Task<NoContent> HandleAsync(SignInManager<IdentityUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }
}
