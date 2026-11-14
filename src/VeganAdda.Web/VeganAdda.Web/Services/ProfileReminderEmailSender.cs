using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace VeganAdda.Web.Services;

public interface IProfileReminderEmailSender
{
    bool IsConfigured { get; }
    Task Send(string recipient, string name, CancellationToken cancellationToken = default);
}

public sealed class ProfileReminderEmailSender(IConfiguration configuration) : IProfileReminderEmailSender
{
    private readonly string host = configuration["Email:Smtp:Host"] ?? "";
    private readonly int port = int.TryParse(configuration["Email:Smtp:Port"], out var configuredPort) ? configuredPort : 587;
    private readonly string username = configuration["Email:Smtp:Username"] ?? "";
    private readonly string password = configuration["Email:Smtp:Password"] ?? "";
    private readonly string fromAddress = configuration["Email:FromAddress"] ?? "";
    private readonly string fromName = configuration["Email:FromName"] ?? "VeganAdda";
    private readonly string appUrl = (configuration["Email:AppUrl"] ?? "").TrimEnd('/');

    public bool IsConfigured => !string.IsNullOrWhiteSpace(host)
        && port is > 0 and <= 65535
        && !string.IsNullOrWhiteSpace(fromAddress)
        && MailAddress.TryCreate(fromAddress, out _)
        && Uri.TryCreate(appUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (string.IsNullOrWhiteSpace(username) == string.IsNullOrWhiteSpace(password));

    public async Task Send(string recipient, string name, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Profile reminder email delivery is not configured.");
        if (!MailAddress.TryCreate(recipient, out var to)) throw new InvalidOperationException("Firebase did not provide a valid email address.");

        using var message = Compose(new MailAddress(fromAddress, fromName), to, name, appUrl);
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new NetworkCredential(username, password);
        await client.SendMailAsync(message, cancellationToken);
    }

    public const string Subject = "Complete your VeganAdda profile";

    // multipart/alternative: clients that render HTML get paragraphs and a button;
    // plain-text clients get the same copy with blank-line paragraph breaks.
    public static MailMessage Compose(MailAddress from, MailAddress to, string name, string appUrl)
    {
        var profileUrl = $"{appUrl}/settings/profile";
        var message = new MailMessage
        {
            From = from,
            Subject = Subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };
        message.To.Add(to);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(PlainText(name, profileUrl), Encoding.UTF8, MediaTypeNames.Text.Plain));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(Html(name, profileUrl), Encoding.UTF8, MediaTypeNames.Text.Html));
        return message;
    }

    public static string PlainText(string name, string profileUrl) =>
        $"Hello {name},\r\n\r\n" +
        "Thanks for joining VeganAdda. Please complete your basic profile to start discovering and connecting with the community.\r\n\r\n" +
        $"Complete your profile: {profileUrl}\r\n\r\n" +
        "If you have already completed your profile, you can ignore this message.\r\n\r\n" +
        "VeganAdda community team\r\n";

    public static string Html(string name, string profileUrl)
    {
        var safeName = WebUtility.HtmlEncode(name);
        var safeUrl = WebUtility.HtmlEncode(profileUrl);
        return $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{Subject}</title></head>
            <body style="margin:0;padding:0;background:#f4f7f2;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f7f2;">
            <tr><td align="center" style="padding:24px 12px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border-radius:12px;font-family:Arial,Helvetica,sans-serif;color:#1f2a1f;">
            <tr><td style="padding:28px 28px 8px;font-size:20px;font-weight:bold;color:#2e7d32;">VeganAdda</td></tr>
            <tr><td style="padding:8px 28px;font-size:16px;line-height:1.5;">
            <p style="margin:0 0 16px;">Hello {safeName},</p>
            <p style="margin:0 0 16px;">Thanks for joining VeganAdda. Please complete your basic profile to start discovering and connecting with the community.</p>
            <p style="margin:24px 0;"><a href="{safeUrl}" style="display:inline-block;background:#2e7d32;color:#ffffff;text-decoration:none;font-weight:bold;padding:12px 22px;border-radius:8px;">Complete your profile</a></p>
            <p style="margin:0 0 16px;font-size:14px;color:#5b665b;">Or open this link: <a href="{safeUrl}" style="color:#2e7d32;">{safeUrl}</a></p>
            <p style="margin:0 0 16px;font-size:14px;color:#5b665b;">If you have already completed your profile, you can ignore this message.</p>
            </td></tr>
            <tr><td style="padding:8px 28px 28px;font-size:15px;">VeganAdda community team</td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }
}
