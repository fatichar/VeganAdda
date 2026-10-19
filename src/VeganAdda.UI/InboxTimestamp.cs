using System.Globalization;

namespace VeganAdda.UI;

public static class InboxTimestamp
{
    public static string Format(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value == null) return "";
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var local = TimeZoneInfo.ConvertTime(value.Value, zone);
        var days = (TimeZoneInfo.ConvertTime(now, zone).Date - local.Date).Days;
        return local.ToString(days == 0 ? "h:mm tt" : days is > 0 and < 7 ? "ddd" : local.Month is 6 or 7 ? "d MMMM" : "d MMM", CultureInfo.InvariantCulture);
    }
}
