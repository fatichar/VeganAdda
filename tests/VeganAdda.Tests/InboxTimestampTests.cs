using VeganAdda.UI;

namespace VeganAdda.Tests;

public class InboxTimestampTests
{
    [Theory]
    [InlineData("2026-10-20T03:12:00+05:30", "3:12 AM")]
    [InlineData("2026-10-19T23:59:00+05:30", "Mon")]
    [InlineData("2026-10-14T00:00:00+05:30", "Wed")]
    [InlineData("2026-10-13T23:59:00+05:30", "13 Oct")]
    [InlineData("2026-08-12T12:00:00+05:30", "12 Aug")]
    [InlineData("2026-07-09T12:00:00+05:30", "9 July")]
    [InlineData("2026-10-19T21:42:00Z", "3:12 AM")]
    public void Formats_using_India_calendar_days(string value, string expected)
        => Assert.Equal(expected, InboxTimestamp.Format(DateTimeOffset.Parse(value), DateTimeOffset.Parse("2026-10-20T09:00:00+05:30")));
}
