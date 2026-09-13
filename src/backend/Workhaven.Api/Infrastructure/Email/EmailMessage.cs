namespace Workhaven.Api.Infrastructure.Email;

internal sealed record EmailMessage(string From, string Recipient, string Subject, string Text, string? Html = null);
