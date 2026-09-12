using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Workhaven.Api.Features.Identity.Registration;

internal static class RegistrationEndpoint
{
    private const string RateLimitPolicy = "registration";

    public static IServiceCollection AddRegistration(this IServiceCollection services)
    {
        services.AddScoped<RegistrationService>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
            options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });
        return services;
    }

    public static RouteHandlerBuilder MapRegistration(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/auth/register", HandleAsync).RequireRateLimiting(RateLimitPolicy);

    private static async Task<Results<NoContent, ValidationProblem>> HandleAsync(
        RegisterRequest request, RegistrationService registration)
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

        var result = await registration.RegisterAsync(request.Email!, request.Password!);
        if (result.Succeeded)
        {
            return TypedResults.NoContent();
        }

        return TypedResults.ValidationProblem(result.Errors
            .GroupBy(error => error.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "email")
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
    }
}
