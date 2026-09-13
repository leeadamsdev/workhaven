using Microsoft.Extensions.Options;

namespace Workhaven.Api.Infrastructure.Email;

internal sealed class MailpitEmailDelivery(HttpClient client, IOptions<MailpitOptions> options) : IEmailDelivery
{
    public EmailProvider Provider => EmailProvider.Mailpit;
    public string From => "Workhaven <no-reply@workhaven.test>";

    public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, Guid deliveryId, CancellationToken cancellationToken)
    {
        var sender = new System.Net.Mail.MailAddress(message.From);
        using var response = await client.PostAsJsonAsync(new Uri(new Uri(options.Value.BaseUrl), "/api/v1/send"), new
        {
            From = new { Email = sender.Address, Name = sender.DisplayName },
            To = new[] { new { Email = message.Recipient } },
            message.Subject,
            message.Text,
            HTML = message.Html
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return new(EmailDeliveryStatus.Accepted);
    }
}
