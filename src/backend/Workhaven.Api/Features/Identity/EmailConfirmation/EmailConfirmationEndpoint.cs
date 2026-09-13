using Microsoft.AspNetCore.Http.HttpResults;

namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal static class EmailConfirmationEndpoint
{
    public static IServiceCollection AddEmailConfirmation(this IServiceCollection services) =>
        services.AddScoped<EmailConfirmationService>();

    public static RouteHandlerBuilder MapEmailConfirmation(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/auth/confirm-email", HandleAsync)
            .RequireRateLimiting(IdentityRateLimiting.EmailConfirmation);

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        ConfirmEmailRequest request, EmailConfirmationService confirmation)
    {
        if (!string.IsNullOrWhiteSpace(request.UserId) && request.UserId.Length <= 128 &&
            !request.UserId.Any(char.IsControl) &&
            !string.IsNullOrWhiteSpace(request.Token) && request.Token.Length <= 4096 &&
            await confirmation.ConfirmAsync(request.UserId, request.Token))
        {
            return TypedResults.NoContent();
        }

        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid email confirmation",
            detail: "The confirmation link is invalid or has expired.");
    }
}
