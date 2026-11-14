using VeganAdda.Contracts;
using VeganAdda.UI;

namespace VeganAdda.Tests;

public class BulkActionSummaryTests
{
    [Fact]
    public void Sent_reminders_are_confirmed()
    {
        var summary = BulkActionSummary.From(new("profile-reminder", 1, 0, 0));
        Assert.True(summary.Ok);
        Assert.Equal("Profile reminder sent to 1 member.", summary.Title);
        Assert.Equal("", summary.Detail);
    }

    [Fact]
    public void Failed_and_skipped_reminders_are_reported_as_a_problem()
    {
        var summary = BulkActionSummary.From(new("profile-reminder", 2, 1, 3));
        Assert.False(summary.Ok);
        Assert.Equal("Profile reminder sent to 2 members, but some failed.", summary.Title);
        Assert.Contains("1 member skipped", summary.Detail);
        Assert.Contains("3 members couldn’t be emailed", summary.Detail);
    }

    [Theory]
    [InlineData(0, 2, "Profile reminders couldn’t be sent.")]
    [InlineData(2, 0, "No profile reminders were sent.")]
    public void Nothing_sent_is_not_shown_as_success(int skipped, int failed, string title)
    {
        var summary = BulkActionSummary.From(new("profile-reminder", 0, skipped, failed));
        Assert.False(summary.Ok);
        Assert.Equal(title, summary.Title);
    }

    [Fact]
    public void Verification_result_is_summarized()
    {
        var summary = BulkActionSummary.From(new("verify-vegan", 3, 1, 0));
        Assert.True(summary.Ok);
        Assert.Equal("Verified vegan status for 3 members.", summary.Title);
        Assert.StartsWith("1 member skipped", summary.Detail);
    }
}
