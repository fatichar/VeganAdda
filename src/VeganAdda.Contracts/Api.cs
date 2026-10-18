using System.Text.Json;

namespace VeganAdda.Contracts;

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Intents = ["Friendship", "Business & networking", "Dating", "Activism"];
    public static readonly string[] Cities = ["All India", "Pune", "Bengaluru", "Mumbai", "Delhi NCR", "Hyderabad", "Chennai"];
}
public interface ICommunityApi
{
    Task<T> Get<T>(string path);
    Task<T> Send<T>(string path, object? body = null, string method = "POST");
}
public interface IPlatform
{
    void SignIn(string returnTo);
    Task SignInWithGoogle(string returnTo);
    Task ClearFirebaseSession();
    Task StartRealtime();
    Task<string> Upload(Stream stream, string filename, string contentType);
    event Action? Changed;
}
public class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public record Ok(bool Success = true);
public record Session(Guid? Id, string Name, bool HasProfile, bool IsAdmin, bool Development, bool AuthConfigured);
public record FirebaseClientConfig(string ApiKey, string AuthDomain, string ProjectId, string AppId, bool Configured);
public record FirebaseLoginInput(string IdToken);
public record Page<T>(List<T> Items, int Total, int PageNumber, int PageSize = 12);
// This is the only profile projection returned to other people. Never add email or OAuth identity.
public record Person(Guid Id, string Name, string City, string Bio, string Photo, string[] Interests,
    string[] LookingFor, string? Instagram, string? LinkedIn, string? Website, bool Featured,
    bool Saved, string Relationship, bool Demo = false);
public class ProfileInput
{
    public string Name { get; set; } = "";
    public string City { get; set; } = "Pune";
    public string Bio { get; set; } = "";
    public string Photo { get; set; } = "";
    public string[] Interests { get; set; } = [];
    public string[] LookingFor { get; set; } = ["Friendship"];
    public string? Instagram { get; set; }
    public string? LinkedIn { get; set; }
    public string? Website { get; set; }
    public bool Public { get; set; }
    public bool FeatureConsent { get; set; }
    public bool DefaultAttendeeVisible { get; set; }
    public bool AdultConfirmed { get; set; }
}
public record ConnectionItem(string Key, Person Person, string State, bool Incoming, string Introduction, DateTimeOffset Updated);
public record ConnectionInput(Guid PersonId, string Introduction = "");
public record ReportInput(Guid PersonId, string Reason);
public record Conversation(string Key, string Name, string Photo, string Latest, int Unread, DateTimeOffset? Updated, bool CanMessage);
public record Message(long Id, Guid SenderId, Guid ClientId, string Text, DateTimeOffset SentAt);
public record MessagePage(List<Message> Items, bool HasOlder, bool CanMessage, bool HasNewer = false);
public record MessageInput(Guid ClientId, string Text);
public record ReadInput(long ThroughId);
public class EventInput
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Cover { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string City { get; set; } = "Pune";
    public string Venue { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string? Registration { get; set; }
    public bool Published { get; set; }
    public bool Featured { get; set; }
}
public record EventCard(Guid Id, string Title, string Cover, string Description, DateTimeOffset? Start,
    DateTimeOffset? End, string TimeZone, string City, string Venue, string Organizer, string? Registration,
    bool Featured, bool Saved, bool Attending, bool AttendeeVisible, bool Demo);
public record AttendanceInput(bool Attending, bool Visible);
public record SavedItems(List<Person> People, List<EventCard> Events);
public record Notice(Guid Id, string Kind, string Text, string Url, bool Read, DateTimeOffset Created);
public record NotificationList(List<Notice> Items, int Unread);
public record ReportView(Guid Id, string Reporter, Guid SubjectId, string Subject, string Reason, bool Resolved, DateTimeOffset Created);
public record AdminData(List<EventInput> Events, List<ReportView> Reports, List<AdminPerson> People);
public record AdminPerson(Guid Id, string Name, bool Suspended, bool Public, bool Consent, bool Featured);
public record ModerateInput(Guid Id, string Action);
public record DeleteInput(string Confirm);
