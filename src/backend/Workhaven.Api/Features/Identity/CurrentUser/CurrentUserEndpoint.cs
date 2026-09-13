using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Workhaven.Api.Features.Identity.CurrentUser;

internal static class CurrentUserEndpoint
{
    public static RouteHandlerBuilder MapCurrentUser(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/auth/me", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<CurrentUserResponse>, UnauthorizedHttpResult>> HandleAsync(
        HttpContext context, UserManager<IdentityUser> users)
    {
        context.Response.Headers.CacheControl = "no-store";
        var user = await users.GetUserAsync(context.User);
        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new CurrentUserResponse(user.Id, user.Email));
    }
}
