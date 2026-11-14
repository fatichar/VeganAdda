using VeganAdda.Contracts;

namespace VeganAdda.UI;

/// <summary>Admin-facing outcome of a bulk member action, shown where the action was confirmed.</summary>
public sealed record BulkActionSummary(bool Ok, string Title, string Detail)
{
    public static BulkActionSummary From(BulkMemberActionResult result)
    {
        static string Members(int count) => $"{count} member{(count == 1 ? "" : "s")}";
        var skipped = result.Skipped > 0 ? $"{Members(result.Skipped)} skipped" : "";

        if (result.Action == "verify-vegan")
            return new(result.Affected > 0,
                result.Affected > 0 ? $"Verified vegan status for {Members(result.Affected)}." : "No members were verified.",
                Join(skipped, result.Skipped > 0 ? "they were already verified, suspended, or have an incomplete basic profile" : ""));

        var skippedReason = result.Skipped > 0 ? "profile already complete, suspended, or no verified email" : "";
        var failed = result.Failed > 0 ? $"{Members(result.Failed)} couldn’t be emailed; try again later" : "";
        var title = (result.Affected, result.Failed) switch
        {
            ( > 0, 0) => $"Profile reminder sent to {Members(result.Affected)}.",
            ( > 0, _) => $"Profile reminder sent to {Members(result.Affected)}, but some failed.",
            (0, > 0) => "Profile reminders couldn’t be sent.",
            _ => "No profile reminders were sent."
        };
        return new(result.Affected > 0 && result.Failed == 0, title,
            string.Join(" ", new[] { Join(skipped, skippedReason), failed == "" ? "" : failed + "." }.Where(s => s != "")));
    }

    private static string Join(string count, string reason) => count == "" ? "" : $"{count} ({reason}).";
}
