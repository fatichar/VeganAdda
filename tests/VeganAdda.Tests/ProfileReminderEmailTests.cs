using System.Net.Mail;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

public class ProfileReminderEmailTests
{
    [Fact]
    public void Reminder_has_plain_text_and_html_alternatives_with_paragraphs()
    {
        using var message = ProfileReminderEmailSender.Compose(
            new MailAddress("hello@veganadda.test", "VeganAdda"),
            new MailAddress("member@example.com"),
            "Alex",
            "https://veganadda.test");

        Assert.Equal(ProfileReminderEmailSender.Subject, message.Subject);
        Assert.Equal(["text/plain", "text/html"], message.AlternateViews.Select(v => v.ContentType.MediaType));

        var text = ProfileReminderEmailSender.PlainText("Alex", "https://veganadda.test/settings/profile");
        Assert.Contains("Hello Alex,\r\n\r\nThanks", text);
        Assert.Contains("Complete your profile: https://veganadda.test/settings/profile", text);

        var html = ProfileReminderEmailSender.Html("Alex", "https://veganadda.test/settings/profile");
        Assert.Contains("<p style=\"margin:0 0 16px;\">Hello Alex,</p>", html);
        Assert.Contains("href=\"https://veganadda.test/settings/profile\"", html);
    }

    [Fact]
    public void Reminder_html_encodes_member_name()
    {
        var html = ProfileReminderEmailSender.Html("<script>x</script>", "https://veganadda.test/settings/profile");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
