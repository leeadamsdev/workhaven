using System.Net;
using System.Text.RegularExpressions;
using Workhaven.Api.Features.Identity.EmailConfirmation;
using Xunit;

namespace Workhaven.Api.IntegrationTests;

public sealed partial class ConfirmationEmailTemplateTests
{
    [Fact]
    public void BothAlternativesPreserveTheConfirmationLinkAndExpiry()
    {
        const string link = "https://workhaven.example/confirm-email#userId=test&token=a%2Bb%2F%3D";
        var message = ConfirmationEmailTemplate.Create("sender@example.test", "recipient@example.test", link,
            new DateTimeOffset(2026, 9, 14, 17, 30, 0, TimeSpan.FromHours(1)));

        Assert.NotNull(message.Html);
        Assert.Equal(link, message.Text.Split('\n').Single(line => line.StartsWith("https:", StringComparison.Ordinal)));
        var links = LinkPattern().Matches(message.Html);
        Assert.Equal(2, links.Count);
        Assert.All(links, match => Assert.Equal(link, WebUtility.HtmlDecode(match.Groups[1].Value)));
        Assert.Contains("14 September 2026 at 16:30 UTC", message.Text, StringComparison.Ordinal);
        Assert.Contains("14 September 2026 at 16:30 UTC", message.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodesTheLinkWithoutIntroducingHtmlAttributesOrElements()
    {
        const string link = "https://workhaven.example/confirm-email#token=\"><script>alert('test')</script>&userId=test";
        var message = ConfirmationEmailTemplate.Create("sender@example.test", "recipient@example.test", link, DateTimeOffset.UtcNow);

        Assert.NotNull(message.Html);
        Assert.DoesNotContain("<script>", message.Html, StringComparison.Ordinal);
        Assert.All(LinkPattern().Matches(message.Html), match => Assert.Equal(link, WebUtility.HtmlDecode(match.Groups[1].Value)));
        Assert.Contains(link, message.Text, StringComparison.Ordinal);
    }

    [GeneratedRegex("href=\"([^\"]*)\"")]
    private static partial Regex LinkPattern();
}
