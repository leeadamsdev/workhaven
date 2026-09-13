namespace Workhaven.Api.Infrastructure.Email;

internal interface IEmailDelivery
{
    Task SendAsync(string recipient, string subject, string text, CancellationToken cancellationToken);
}
