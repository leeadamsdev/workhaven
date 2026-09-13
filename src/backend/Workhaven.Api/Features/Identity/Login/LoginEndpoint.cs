using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Workhaven.Api.Features.Identity.Login;

internal static class LoginEndpoint
{
    public static RouteHandlerBuilder MapLogin(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/auth/login", HandleAsync).RequireRateLimiting(IdentityRateLimiting.Login);

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        LoginRequest request, HttpContext context, IAntiforgery antiforgery, LoginService login)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request", detail: "Refresh the page and try again.");
        }

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
