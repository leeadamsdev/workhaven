using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Workhaven.Api.Features.Identity.Registration;

internal static class RegistrationEndpoint
{
    public static IServiceCollection AddRegistration(this IServiceCollection services) =>
        services.AddScoped<RegistrationService>();

    public static RouteHandlerBuilder MapRegistration(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/auth/register", HandleAsync).RequireRateLimiting(IdentityRateLimiting.Registration);

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> HandleAsync(
        RegisterRequest request, RegistrationService registration, CancellationToken cancellationToken)
    {
        if (request.Email?.Any(char.IsControl) == true)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["The email address must not contain control characters."]
            });
        }

        request.Email = request.Email?.Trim();
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true))
        {
            var errors = validationResults
                .SelectMany(result => result.MemberNames.Select(member => new
                {
                    Field = JsonNamingPolicy.CamelCase.ConvertName(member),
                    Message = result.ErrorMessage ?? "The value is invalid."
                }))
                .GroupBy(error => error.Field)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
            return TypedResults.ValidationProblem(errors);
        }

        var result = await registration.RegisterAsync(request.Email!, request.Password!, cancellationToken);
        if (result.Succeeded)
        {
            return TypedResults.NoContent();
        }

        if (result.Errors.Any(error => error.Code == "EmailDeliveryUnavailable"))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Registration is unavailable", detail: "Please try again later.");
        }

        return TypedResults.ValidationProblem(result.Errors
            .GroupBy(error => error.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "email")
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
    }
}
