namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal sealed class PendingConfirmationEmail
{
    public required string UserId { get; init; }

    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;

    public int Attempts { get; set; }
}
