using System.ComponentModel.DataAnnotations;

namespace Workhaven.Api.Infrastructure.Email;

internal sealed class MailpitOptions
{
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:8025";
}
