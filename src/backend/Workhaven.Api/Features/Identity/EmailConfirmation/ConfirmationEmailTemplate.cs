using System.Globalization;
using System.Text.Encodings.Web;
using Workhaven.Api.Infrastructure.Email;

namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal static class ConfirmationEmailTemplate
{
    public static EmailMessage Create(string from, string recipient, string link, DateTimeOffset expiresAt)
    {
        var expiry = expiresAt.UtcDateTime.ToString("d MMMM yyyy 'at' HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var encodedLink = HtmlEncoder.Default.Encode(link);
        var text = $"""
            Workhaven

            Confirm your email address

            Welcome to Workhaven. Confirm your email address to continue setting up your account.

            {link}

            This link expires on {expiry}.

            If you didn't create a Workhaven account, you can safely ignore this email.

            The Workhaven team
            """;
        // Inline styles and presentation tables keep the message usable in email clients without modern CSS.
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Confirm your Workhaven email</title>
            </head>
            <body style="margin:0;padding:0;background-color:#F7F8FA;color:#111827;font-family:Arial,Helvetica,sans-serif;">
              <div style="display:none;max-height:0;overflow:hidden;mso-hide:all;">Welcome to Workhaven. Confirm your email address to get started.</div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background-color:#F7F8FA;">
                <tr>
                  <td align="center" style="padding:40px 16px;">
                    <!--[if mso]><table role="presentation" width="560" cellspacing="0" cellpadding="0" border="0"><tr><td><![endif]-->
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="max-width:560px;">
                      <tr>
                        <td style="padding:0 0 24px;font-size:24px;line-height:32px;font-weight:700;letter-spacing:-0.5px;color:#3156D3;">Workhaven</td>
                      </tr>
                      <tr>
                        <td style="background-color:#FFFFFF;border:1px solid #DCE1E8;border-top:4px solid #3156D3;border-radius:8px;padding:32px 24px;">
                          <h1 style="margin:0 0 20px;font-size:26px;line-height:34px;font-weight:700;color:#111827;">Confirm your email address</h1>
                          <p style="margin:0 0 28px;font-size:16px;line-height:26px;color:#475569;">Welcome to Workhaven. Confirm your email address to continue setting up your account.</p>
                          <table role="presentation" cellspacing="0" cellpadding="0" border="0">
                            <tr>
                              <td align="center" bgcolor="#3156D3" style="border-radius:6px;mso-padding-alt:14px 24px;">
                                <a href="{{encodedLink}}" style="display:inline-block;border:1px solid #3156D3;border-radius:6px;padding:14px 24px;font-size:16px;line-height:22px;font-weight:700;color:#FFFFFF;text-decoration:none;">Confirm email address</a>
                              </td>
                            </tr>
                          </table>
                          <p style="margin:20px 0 0;font-size:13px;line-height:21px;color:#5B6878;">This link expires on {{expiry}}.</p>
                          <p style="margin:24px 0 0;font-size:14px;line-height:23px;color:#475569;">If the button doesn&rsquo;t work, <a href="{{encodedLink}}" style="color:#3156D3;text-decoration:underline;">open the confirmation page</a>.</p>
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="margin-top:28px;">
                            <tr>
                              <td style="border-top:1px solid #DCE1E8;padding-top:24px;">
                                <p style="margin:0;font-size:14px;line-height:23px;color:#5B6878;">If you didn&rsquo;t create a Workhaven account, you can safely ignore this email.</p>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:24px 0 0;font-size:13px;line-height:21px;color:#5B6878;">The Workhaven team</td>
                      </tr>
                    </table>
                    <!--[if mso]></td></tr></table><![endif]-->
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return new(from, recipient, "Confirm your Workhaven email", text, html);
    }
}
