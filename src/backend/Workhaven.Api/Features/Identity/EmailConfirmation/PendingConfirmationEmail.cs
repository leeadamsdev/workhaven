namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal sealed class PendingConfirmationEmail
{
    public required string UserId { get; init; }

    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;

    public int Attempts { get; set; }

    public Guid? DeliveryId { get; set; }

    public DateTimeOffset? PreparedAt { get; set; }

    public string? ProtectedMessage { get; set; }

    public DateTimeOffset? FailedAt { get; set; }

    public string? FailureCode { get; set; }
}
