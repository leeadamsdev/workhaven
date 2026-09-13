using Workhaven.Api.Infrastructure.Email;

namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal static class ConfirmationEmailConfiguration
{
    public static IServiceCollection AddConfirmationEmailDelivery(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<ConfirmationEmailOptions>()
            .Bind(configuration.GetSection("ConfirmationEmail"))
            .ValidateDataAnnotations()
            .Validate(options => Uri.TryCreate(options.PageUrl, UriKind.Absolute, out var uri) &&
                string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo) &&
                (uri.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback),
                "ConfirmationEmail:PageUrl must be HTTPS (or loopback HTTP in Development), without credentials, query or fragment.");

        if (environment.IsDevelopment())
        {
            services.AddOptions<ConfirmationEmailOptions>().ValidateOnStart();
            services.AddOptions<MailpitOptions>()
                .Bind(configuration.GetSection("Mailpit"))
                .ValidateDataAnnotations()
                .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                    string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
                    uri.AbsolutePath == "/", "Mailpit:BaseUrl must be an HTTP(S) origin without credentials, query or fragment.")
                .ValidateOnStart();
            services.AddHttpClient<IEmailDelivery, MailpitEmailDelivery>(client => client.Timeout = TimeSpan.FromSeconds(5));
            services.AddScoped<ConfirmationEmailDelivery>();
            services.AddHostedService<ConfirmationEmailWorker>();
        }

        return services;
    }
}
