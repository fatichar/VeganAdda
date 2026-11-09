namespace VeganAdda.Contracts;

public sealed record Announcement(string Id, string Title, string Message, string? ActionLabel = null, string? ActionPath = null);
