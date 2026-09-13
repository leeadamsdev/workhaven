using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Workhaven.Api.Infrastructure.Email;

namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal sealed partial class ConfirmationEmailDelivery(
    WorkhavenIdentityDbContext database,
    UserManager<IdentityUser> users,
    IEmailDelivery delivery,
    IOptions<ConfirmationEmailOptions> options,
    ILogger<ConfirmationEmailDelivery> logger)
{
    public async Task<bool> SendNextAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Keep the row locked through delivery so concurrent API instances cannot claim the same send.
        var pending = await database.PendingConfirmationEmails.FromSqlRaw("""
            SELECT * FROM identity."PendingConfirmationEmails"
            WHERE "NextAttemptAt" <= now()
            ORDER BY "NextAttemptAt", "UserId"
            LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var email = pending.SingleOrDefault();
        if (email is null)
        {
            return false;
        }

        var user = await database.Users.SingleAsync(user => user.Id == email.UserId, cancellationToken);
        if (!user.EmailConfirmed)
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            // A fragment keeps the token out of HTTP access logs and Referer headers.
            var link = $"{options.Value.PageUrl}#userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
            var text = $"Confirm your Workhaven email address:\n\n{link}\n\nIf you didn't request this, you can ignore this email.";
            try
            {
                await delivery.SendAsync(user.Email!, "Confirm your Workhaven email", text, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException ||
                                              exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                email.Attempts++;
                email.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(email.Attempts - 1, 6))));
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                LogDeliveryRetry(logger);
                return true;
            }
        }

        // Delivery is at least once: a crash after sending can resend, and confirmation is safe to repeat.
        database.PendingConfirmationEmails.Remove(email);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Confirmation email delivery failed; a retry has been scheduled.")]
    private static partial void LogDeliveryRetry(ILogger logger);
}
