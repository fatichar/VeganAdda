namespace VeganAdda.Contracts;

public record ConnectionItem(string Key, Person Person, string State, bool Incoming, string Introduction, DateTimeOffset Updated);
public record ConnectionInput(Guid PersonId, string Introduction = "");
public record ReportInput(Guid PersonId, string Reason);
