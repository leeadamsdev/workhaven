using System.ComponentModel.DataAnnotations;

namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal sealed class ConfirmationEmailOptions
{
    [Required]
    [Url]
    public string PageUrl { get; set; } = "http://localhost:5173/confirm-email";
}
