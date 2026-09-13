namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal sealed class ConfirmEmailRequest
{
    public string? UserId { get; init; }

    public string? Token { get; init; }
}
