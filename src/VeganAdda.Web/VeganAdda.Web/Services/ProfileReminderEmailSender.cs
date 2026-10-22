using System.Net;
using System.Net.Mail;

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

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, fromName),
            Subject = "Complete your VeganAdda profile",
            Body = $"Hello {name},\r\n\r\nThanks for joining VeganAdda. Please complete your basic profile to start discovering and connecting with the community.\r\n\r\nComplete your profile: {appUrl}/settings/profile\r\n\r\nIf you have already completed your profile, you can ignore this message.\r\n\r\nVeganAdda community team",
            IsBodyHtml = false
        };
        message.To.Add(to);
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new NetworkCredential(username, password);
        await client.SendMailAsync(message, cancellationToken);
    }
}
