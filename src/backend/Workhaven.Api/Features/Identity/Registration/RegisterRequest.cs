using System.ComponentModel.DataAnnotations;

namespace Workhaven.Api.Features.Identity.Registration;

internal sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; set; }

    [Required]
    [MaxLength(128)]
    public string? Password { get; init; }
}
