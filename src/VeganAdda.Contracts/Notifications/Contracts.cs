namespace VeganAdda.Contracts;

public record Notice(Guid Id, string Kind, string Text, string Url, bool Read, DateTimeOffset Created);
public record NotificationList(List<Notice> Items, int Unread, int Total = 0, int PageNumber = 1, int PageSize = 20);
public record NotificationSelection(Guid[] Ids, bool All = false);
public record UnreadCounts(int Messages, int Notifications);
