namespace VeganAdda.Contracts;

public record Conversation(string Key, string Name, string Photo, string Latest, int Unread, DateTimeOffset? Updated, bool CanMessage, Guid? PersonId = null, bool BlockedByMe = false, bool CanReconnect = false);
public record Message(long Id, Guid SenderId, Guid ClientId, string Text, DateTimeOffset SentAt);
public record MessagePage(List<Message> Items, bool HasOlder, bool CanMessage, bool HasNewer = false);
public record MessageInput(Guid ClientId, string Text);
public record ReadInput(long ThroughId);
