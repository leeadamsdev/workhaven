using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Workhaven.Api.Infrastructure.Email;

namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal sealed partial class ConfirmationEmailDelivery(
    WorkhavenIdentityDbContext database,
    UserManager<IdentityUser> users,
    IEmailDelivery delivery,
    IDataProtectionProvider protection,
    IOptions<ConfirmationEmailOptions> options,
    IOptions<DataProtectionTokenProviderOptions> tokenOptions,
    ILogger<ConfirmationEmailDelivery> logger)
{
    public async Task<bool> SendNextAsync(CancellationToken cancellationToken)
    {
        var result = await ProcessNextAsync(cancellationToken);
        if (result == ProcessingResult.Prepared)
        {
            // Preparation must commit before any HTTP request. Reacquire a row lock for delivery;
            // another worker may already have claimed the prepared message in the meantime.
            await ProcessNextAsync(cancellationToken);
        }

        return result != ProcessingResult.Empty;
    }

    private async Task<ProcessingResult> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var pending = await database.PendingConfirmationEmails.FromSqlRaw("""
            SELECT * FROM identity."PendingConfirmationEmails"
            WHERE "FailedAt" IS NULL AND "NextAttemptAt" <= now()
            ORDER BY "NextAttemptAt", "UserId"
            LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var email = pending.SingleOrDefault();
        if (email is null)
        {
            return ProcessingResult.Empty;
        }

        var user = await database.Users.AsNoTracking().SingleAsync(user => user.Id == email.UserId, cancellationToken);
        if (user.EmailConfirmed)
        {
            database.PendingConfirmationEmails.Remove(email);
        }
        else if (email.ProtectedMessage is null)
        {
            var expiresAt = DateTimeOffset.UtcNow.Add(tokenOptions.Value.TokenLifespan);
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            // A fragment keeps the token out of HTTP access logs and Referer headers.
            var link = $"{options.Value.PageUrl}#userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
            var message = new PreparedEmail(delivery.Provider,
                ConfirmationEmailTemplate.Create(delivery.From, user.Email!, link, expiresAt));
            email.ProtectedMessage = Protector(email.UserId).Protect(JsonSerializer.Serialize(message));
            email.DeliveryId = Guid.NewGuid();
            email.PreparedAt = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            database.Entry(email).State = EntityState.Detached;
            return ProcessingResult.Prepared;
        }
        else if (email.PreparedAt is null || email.DeliveryId is null ||
                 DateTimeOffset.UtcNow >= email.PreparedAt.Value.AddHours(23))
        {
            // Resend forgets idempotency keys after 24h. Leave headroom for an in-flight request,
            // and require an explicit resend instead of risking duplicates or an expired token.
            Fail(email, "retry-window-expired");
        }
        else
        {
            await DeliverAsync(email, cancellationToken);
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        database.Entry(email).State = EntityState.Detached;
        return ProcessingResult.Processed;
    }

    private async Task DeliverAsync(PendingConfirmationEmail email, CancellationToken cancellationToken)
    {
        PreparedEmail? prepared;
        try
        {
            prepared = JsonSerializer.Deserialize<PreparedEmail>(Protector(email.UserId).Unprotect(email.ProtectedMessage!));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            // Never regenerate an uncertain send with a new token/key after losing encryption keys.
            Fail(email, "payload-unavailable");
            return;
        }

        if (prepared is null || prepared.Provider != delivery.Provider)
        {
            Fail(email, "provider-changed");
            return;
        }

        EmailDeliveryResult result;
        try
        {
            result = await delivery.SendAsync(prepared.Message, email.DeliveryId!.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException ||
                                          exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            result = new(EmailDeliveryStatus.Retry);
        }

        email.Attempts++;
        switch (result.Status)
        {
            case EmailDeliveryStatus.Accepted:
                database.PendingConfirmationEmails.Remove(email);
                break;
            case EmailDeliveryStatus.Rejected:
                Fail(email, "provider-rejected");
                break;
            case EmailDeliveryStatus.Retry:
                var backoff = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(email.Attempts - 1, 6))));
                var delay = result.RetryAfter > backoff ? result.RetryAfter.Value : backoff;
                var deadline = email.PreparedAt!.Value.AddHours(23);
                var remaining = deadline - DateTimeOffset.UtcNow;
                email.NextAttemptAt = delay >= remaining ? deadline : DateTimeOffset.UtcNow.Add(delay);
                LogDeliveryRetry(logger, email.DeliveryId);
                break;
        }
    }

    private IDataProtector Protector(string userId) => protection.CreateProtector("Workhaven.ConfirmationEmail.v1", userId);

    private void Fail(PendingConfirmationEmail email, string code)
    {
        email.FailedAt = DateTimeOffset.UtcNow;
        email.FailureCode = code;
        email.ProtectedMessage = null;
        LogDeliveryFailure(logger, email.DeliveryId, code);
    }

    private sealed record PreparedEmail(EmailProvider Provider, EmailMessage Message);

    private enum ProcessingResult { Empty, Prepared, Processed }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Confirmation email {DeliveryId} failed; a retry has been scheduled.")]
    private static partial void LogDeliveryRetry(ILogger logger, Guid? deliveryId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Confirmation email {DeliveryId} stopped ({FailureCode}); an explicit resend is required.")]
    private static partial void LogDeliveryFailure(ILogger logger, Guid? deliveryId, string failureCode);
}
