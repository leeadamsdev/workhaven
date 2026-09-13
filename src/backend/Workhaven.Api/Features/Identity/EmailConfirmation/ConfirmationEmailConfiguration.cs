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
                "ConfirmationEmail:PageUrl must be HTTPS (or loopback HTTP in Development), without credentials, query or fragment.")
            .ValidateOnStart();

        var provider = configuration.GetValue("Email:Provider",
            environment.IsDevelopment() ? EmailProvider.Mailpit : EmailProvider.Resend);
        if (provider == EmailProvider.Mailpit && environment.IsDevelopment())
        {
            services.AddOptions<MailpitOptions>()
                .Bind(configuration.GetSection("Mailpit"))
                .ValidateDataAnnotations()
                .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                    string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
                    uri.AbsolutePath == "/", "Mailpit:BaseUrl must be an HTTP(S) origin without credentials, query or fragment.")
                .ValidateOnStart();
            services.AddHttpClient<IEmailDelivery, MailpitEmailDelivery>(client => client.Timeout = TimeSpan.FromSeconds(5));
        }
        else if (provider == EmailProvider.Resend)
        {
            services.AddOptions<ResendOptions>()
                .Bind(configuration.GetSection("Resend"))
                .ValidateDataAnnotations()
                .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey) &&
                    options.ApiKey.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'),
                    "Resend:ApiKey must contain only ASCII letters, digits, underscores or hyphens.")
                .Validate(options => !string.IsNullOrWhiteSpace(options.From) &&
                    !options.From.Any(char.IsControl) && System.Net.Mail.MailAddress.TryCreate(options.From, out _),
                    "Resend:From must be a valid sender address without control characters.")
                .ValidateOnStart();
            services.AddHttpClient<IEmailDelivery, ResendEmailDelivery>(client => client.Timeout = TimeSpan.FromSeconds(5))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        }
        else
        {
            throw new InvalidOperationException("Email:Provider must be Resend, or Mailpit in Development only.");
        }

        services.AddScoped<ConfirmationEmailDelivery>();
        services.AddHostedService<ConfirmationEmailWorker>();

        return services;
    }
}
