using System.Globalization;
using System.Threading.RateLimiting;

namespace Workhaven.Api.Features.Identity;

internal static class IdentityRateLimiting
{
    public const string Registration = "registration";
    public const string EmailConfirmation = "email-confirmation";
    public const string Login = "login";

    public static IServiceCollection AddIdentityRateLimiting(this IServiceCollection services) =>
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
            options.AddPolicy(Registration, CreatePartition);
            options.AddPolicy(EmailConfirmation, CreatePartition);
            options.AddPolicy(Login, CreatePartition);
        });

    private static RateLimitPartition<string> CreatePartition(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
}
