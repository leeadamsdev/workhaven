using Microsoft.Extensions.Options;

namespace Workhaven.Api.Infrastructure.Email;

internal sealed class MailpitEmailDelivery(HttpClient client, IOptions<MailpitOptions> options) : IEmailDelivery
{
    public async Task SendAsync(string recipient, string subject, string text, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(new Uri(new Uri(options.Value.BaseUrl), "/api/v1/send"), new
        {
            From = new { Email = "no-reply@workhaven.test", Name = "Workhaven" },
            To = new[] { new { Email = recipient } },
            Subject = subject,
            Text = text
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
