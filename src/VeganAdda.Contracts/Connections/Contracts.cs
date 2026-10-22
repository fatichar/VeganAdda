namespace VeganAdda.Contracts;

public record ConnectionItem(string Key, Person Person, string State, bool Incoming, string Introduction, DateTimeOffset Updated);
public record ConnectionInput(Guid PersonId, string Introduction = "");
public static class ReportReasons
{
    public static readonly string[] All = ["Harassment", "Spam", "Not a vegan", "Fake profile or impersonation", "Inappropriate content", "Other"];
}
public record ReportInput(Guid PersonId, string Reason, string? Details = null, string? Screenshot = null);
