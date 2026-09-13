using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Workhaven.Api.Infrastructure.Email;

internal sealed class ResendEmailDelivery(HttpClient client, IOptions<ResendOptions> options) : IEmailDelivery
{
    // Keep previously prepared plain-text requests byte-for-byte compatible with their idempotency keys.
    private static readonly JsonSerializerOptions MessageJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EmailProvider Provider => EmailProvider.Resend;
    public string From => options.Value.From;

    public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, Guid deliveryId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        request.Headers.Add("Idempotency-Key", $"confirmation/{deliveryId:D}");
        request.Content = JsonContent.Create(new
        {
            from = message.From,
            to = new[] { message.Recipient },
            subject = message.Subject,
            text = message.Text,
            html = message.Html
        }, options: MessageJsonOptions);

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new(EmailDeliveryStatus.Accepted);
        }

        var retry = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500;
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // A reused key with different content must not be retried. An in-flight send can be.
            try
            {
                using var error = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                retry = error?.RootElement.ValueKind == JsonValueKind.Object &&
                        error.RootElement.TryGetProperty("name", out var name) &&
                        name.ValueKind == JsonValueKind.String &&
                        name.GetString() is "concurrent_idempotent_requests" or "resource_locked";
            }
            catch (JsonException)
            {
                // An unrecognised conflict needs investigation, not a new idempotency key.
                retry = false;
            }
        }

        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
        {
            retryAfter = date - DateTimeOffset.UtcNow;
        }

        return new(retry ? EmailDeliveryStatus.Retry : EmailDeliveryStatus.Rejected, retryAfter);
    }
}
