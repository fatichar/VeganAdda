namespace VeganAdda.Contracts;

public record Conversation(string Key, string Name, string Photo, string Latest, int Unread, DateTimeOffset? Updated, bool CanMessage, Guid? PersonId = null, bool BlockedByMe = false, bool CanReconnect = false, bool VeganVerified = false);
public record Message(long Id, Guid SenderId, Guid ClientId, string Text, DateTimeOffset SentAt);
public record MessagePage(List<Message> Items, bool HasOlder, bool CanMessage, bool HasNewer = false);
public record MessageInput(Guid ClientId, string Text);
public record ReadInput(long ThroughId);

public record Inbox(Page<Conversation> Chats, Page<ConnectionItem> Requests, Conversation? Selected, ConnectionItem? SelectedRequest);
public interface IMessagingApi
{
    Task<Inbox> GetInbox(int chatPage = 1, int requestPage = 1, string? key = null);
}
