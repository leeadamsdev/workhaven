namespace Workhaven.Api.Infrastructure.Email;

internal enum EmailDeliveryStatus
{
    Accepted,
    Retry,
    Rejected
}

internal sealed record EmailDeliveryResult(EmailDeliveryStatus Status, TimeSpan? RetryAfter = null);
