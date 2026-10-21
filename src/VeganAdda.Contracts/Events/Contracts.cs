namespace VeganAdda.Contracts;

public class EventInput
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Cover { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public DateTime? LocalStart { get; set; }
    public DateTime? LocalEnd { get; set; }
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string City { get; set; } = "Pune";
    public string Country { get; set; } = "";
    public string State { get; set; } = "";
    public string Venue { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string? Registration { get; set; }
    public bool Published { get; set; }
    public bool Featured { get; set; }
    public string SubmissionStatus { get; set; } = "Draft";
    public string? ReviewNote { get; set; }
}
public record EventReview(EventInput Details, string? SubmittedBy);
public record EventReviewInput(Guid Id, bool Approve, string? Note = null);
public record EventCard(Guid Id, string Title, string Cover, string Description, DateTimeOffset? Start,
    DateTimeOffset? End, string TimeZone, string City, string Venue, string Organizer, string? Registration,
    bool Featured, bool Saved, bool Attending, bool AttendeeVisible, bool Demo, DateTimeOffset? CheckedInUntil = null);
public record AttendanceInput(bool Attending, bool Visible);
public record CheckInInput(bool CheckedIn);
