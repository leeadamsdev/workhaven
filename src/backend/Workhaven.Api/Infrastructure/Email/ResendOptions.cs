using System.ComponentModel.DataAnnotations;

namespace Workhaven.Api.Infrastructure.Email;

internal sealed class ResendOptions
{
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string From { get; set; } = string.Empty;
}
