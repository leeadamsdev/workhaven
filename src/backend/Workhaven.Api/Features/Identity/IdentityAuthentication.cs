using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace Workhaven.Api.Features.Identity;

internal static class IdentityAuthentication
{
    public static void AddIdentityAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
        var development = environment.IsDevelopment();
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
            {
                options.Cookie.Name = development ? "Workhaven.Auth" : "__Host-Workhaven.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = development ? "Workhaven.Csrf" : "__Host-Workhaven.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
    }

    public static RouteHandlerBuilder MapCsrfToken(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            // The cookie stays HttpOnly; clients send this request token in X-CSRF-TOKEN.
            var tokens = antiforgery.GetAndStoreTokens(context);
            return TypedResults.Ok(new CsrfTokenResponse(tokens.RequestToken!));
        });

    private sealed record CsrfTokenResponse(string RequestToken);
}
