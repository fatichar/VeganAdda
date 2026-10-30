namespace VeganAdda.Contracts;

public record ModerationData(List<ProfileReview> Profiles, List<EventReview> Events, int ProfilesTotal = 0, int EventsTotal = 0, int PageNumber = 1, int PageSize = 20);
public record ReportView(Guid Id, string Reporter, Guid SubjectId, string Subject, string Reason, bool Resolved, DateTimeOffset Created, string Details = "", string? Screenshot = null);
public record AdminData(List<EventInput> Events, List<ReportView> Reports, List<AdminPerson> People, List<ProfileReview> Profiles, int EventsTotal = 0, int ReportsTotal = 0, int MembersTotal = 0, int ProfilesTotal = 0, int PageNumber = 1, int PageSize = 20, bool CanSendProfileReminders = false);
public record AdminPerson(Guid Id, string Name, bool Suspended, bool Public, bool Featured, bool VeganVerified = false, bool TestProfile = false, bool Moderator = false, bool? IsVegan = null, bool HasProfile = false, bool Admin = false, bool BasicProfileComplete = false);
public record ProfileReview(Guid Id, string Name, string City, string Bio, string[] Interests, string[] LookingFor, string Photo, string? Instagram, string? LinkedIn, string? Website, bool Public, bool TestProfile, string? ReferredBy, bool ReferrerCanRefer, string? ReviewNote, string Country = "India", string State = "", bool? IsVegan = null, bool VeganVerified = false, string Profession = "", VerificationDetails? Verification = null)
{
    public bool VeganVerificationPending => Verification?.Status == VerificationState.UnderReview;
}
public record VeganVerificationInput(Guid Id, bool Confirm, string? Note = null, Guid? RequestId = null);
public record ModerateInput(Guid Id, string Action);
public record BulkMemberActionInput(List<Guid> MemberIds, string Action);
public record BulkMemberActionResult(string Action, int Affected, int Skipped, int Failed);
