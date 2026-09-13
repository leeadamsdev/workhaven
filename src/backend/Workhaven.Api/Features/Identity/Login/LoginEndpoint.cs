using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Workhaven.Api.Features.Identity.Login;

internal static class LoginEndpoint
{
    public static RouteHandlerBuilder MapLogin(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/auth/login", HandleAsync)
            .AddEndpointFilter<CsrfValidationFilter>()
            .RequireRateLimiting(IdentityRateLimiting.Login);

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        LoginRequest request, LoginService login)
    {
        var email = request.Email?.Trim();
        if (request.Email?.Any(char.IsControl) != true && email is { Length: > 0 and <= 256 } &&
            new EmailAddressAttribute().IsValid(email) && request.Password is { Length: > 0 and <= 128 } &&
            await login.LoginAsync(email, request.Password))
        {
            return TypedResults.NoContent();
        }

        return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized,
            title: "Sign-in failed", detail: "Unable to sign in with these credentials.");
    }
}
