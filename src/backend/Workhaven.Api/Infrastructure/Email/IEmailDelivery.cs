namespace Workhaven.Api.Infrastructure.Email;

internal interface IEmailDelivery
{
    EmailProvider Provider { get; }
    string From { get; }

    Task<EmailDeliveryResult> SendAsync(EmailMessage message, Guid deliveryId, CancellationToken cancellationToken);
}
