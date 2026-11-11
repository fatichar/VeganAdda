using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;

namespace VeganAdda.Web.Data;

/// <summary>A member account and its access flags.</summary>
public class Account
{
    /// <summary>Primary account identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identity-provider subject identifier.</summary>
    public string Subject { get; set; } = "";
    /// <summary>Account display name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Whether sign-in is suspended.</summary>
    public bool Suspended { get; set; }
    /// <summary>Whether the member has admin access.</summary>
    public bool Admin { get; set; }
    /// <summary>Whether the member has moderator access.</summary>
    public bool Moderator { get; set; }
    /// <summary>Whether this is a seeded demo account.</summary>
    public bool Demo { get; set; }
    /// <summary>Account creation time.</summary>
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
/// <summary>A member's public profile and private review/location data.</summary>
public class Profile
{
    /// <summary>Birth date; for month-only entries, month end supports conservative age checks.</summary>
    public DateOnly? BirthDate { get; set; }
    /// <summary>Whether BirthDate contains the actual birthday.</summary>
    public bool HasExactBirthDate { get; set; }
    /// <summary>Private latitude; exclude from member-facing and moderation projections.</summary>
    public double? Latitude { get; set; }
    /// <summary>Private longitude; exclude from member-facing and moderation projections.</summary>
    public double? Longitude { get; set; }
    /// <summary>Whether the member opted into precise-location use.</summary>
    public bool HasPreciseLocation { get; set; }
    /// <summary>Owning account identifier and profile key.</summary>
    public Guid Id { get; set; }
    /// <summary>Profile display name.</summary>
    public string Name { get; set; } = "";
    /// <summary>City shown on the profile.</summary>
    public string City { get; set; } = "";
    /// <summary>Country shown on the profile.</summary>
    public string Country { get; set; } = "India";
    /// <summary>State or region shown on the profile.</summary>
    public string State { get; set; } = "";
    /// <summary>Profile biography.</summary>
    public string Bio { get; set; } = "";
    /// <summary>Member's profession.</summary>
    public string Profession { get; set; } = "";
    /// <summary>Member's gender.</summary>
    public string Gender { get; set; } = "";
    /// <summary>Profile photo URL or storage reference.</summary>
    public string Photo { get; set; } = "";
    /// <summary>Horizontal photo focal point.</summary>
    public int? PhotoFocusX { get; set; }
    /// <summary>Vertical photo focal point.</summary>
    public int? PhotoFocusY { get; set; }
    /// <summary>Member's interests.</summary>
    public string[] Interests { get; set; } = [];
    /// <summary>What the member is looking for.</summary>
    public string[] LookingFor { get; set; } = [];
    /// <summary>Instagram profile link.</summary>
    public string? Instagram { get; set; }
    /// <summary>LinkedIn profile link.</summary>
    public string? LinkedIn { get; set; }
    /// <summary>Personal website link.</summary>
    public string? Website { get; set; }
    /// <summary>Whether the profile is visible to others.</summary>
    public bool Public { get; set; }
    /// <summary>Whether vegan status has been verified.</summary>
    public bool VeganVerified { get; set; }
    /// <summary>Member's stated vegan status, when known.</summary>
    public bool? IsVegan { get; set; }
    /// <summary>Whether this is an isolated test profile.</summary>
    public bool TestProfile { get; set; }
    /// <summary>Private administrative review note.</summary>
    public string? ReviewNote { get; set; }
    /// <summary>Time the profile was reviewed.</summary>
    public DateTimeOffset? ReviewedAt { get; set; }
    /// <summary>Account that reviewed the profile.</summary>
    public Guid? ReviewedBy { get; set; }
    /// <summary>Whether the profile is featured.</summary>
    public bool Featured { get; set; }
    /// <summary>Whether the member consents to being featured.</summary>
    public bool FeatureConsent { get; set; } = true;
    /// <summary>Default visibility of this member in attendee lists.</summary>
    public bool DefaultAttendeeVisible { get; set; } = true;
    /// <summary>Legacy time adult eligibility was confirmed during profile save.</summary>
    public DateTimeOffset AdultConfirmedAt { get; set; }
}
/// <summary>A member-to-member connection and its conversation state.</summary>
public class Relationship
{
    /// <summary>Stable conversation and relationship key.</summary>
    public string Key { get; set; } = "";
    /// <summary>First member identifier.</summary>
    public Guid A { get; set; }
    /// <summary>Second member identifier.</summary>
    public Guid B { get; set; }
    /// <summary>Member who initiated the request.</summary>
    public Guid RequestedBy { get; set; }
    /// <summary>Connection state.</summary>
    public string State { get; set; } = "pending";
    /// <summary>Introduction attached to the request.</summary>
    public string Introduction { get; set; } = "";
    /// <summary>Key ID of the encrypted introduction.</summary>
    public string? IntroductionKeyId { get; set; }
    /// <summary>Encrypted copy of the connection introduction.</summary>
    public byte[]? EncryptedIntroduction { get; set; }
    /// <summary>Time the relationship last changed.</summary>
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Last message sequence read by member A.</summary>
    public long ReadA { get; set; }
    /// <summary>Last message sequence read by member B.</summary>
    public long ReadB { get; set; }
}
/// <summary>A member's block of another member.</summary>
public class Block
{
    /// <summary>Member who created the block.</summary>
    public Guid Owner { get; set; }
    /// <summary>Member who is blocked.</summary>
    public Guid Subject { get; set; }
}
/// <summary>A conversation message and its pending push state.</summary>
public class ChatMessage
{
    /// <summary>Whether push delivery is queued.</summary>
    public bool PushPending { get; set; }
    /// <summary>Identifier of the worker's current push lease.</summary>
    public Guid? PushLeaseId { get; set; }
    /// <summary>Expiry time of the current push lease.</summary>
    public DateTimeOffset? PushLeaseUntil { get; set; }
    /// <summary>Number of push delivery attempts.</summary>
    public int PushAttempts { get; set; }
    /// <summary>Message sequence and primary key.</summary>
    public long Id { get; set; }
    /// <summary>Conversation key.</summary>
    public string Conversation { get; set; } = "";
    /// <summary>Sending member identifier.</summary>
    public Guid SenderId { get; set; }
    /// <summary>Client-generated idempotency identifier.</summary>
    public Guid ClientId { get; set; }
    /// <summary>Message body.</summary>
    public string Text { get; set; } = "";
    /// <summary>Key ID of the encrypted message body.</summary>
    public string? BodyKeyId { get; set; }
    /// <summary>Encrypted copy of the message body.</summary>
    public byte[]? EncryptedBody { get; set; }
    /// <summary>Message send time.</summary>
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
/// <summary>A community gathering or event.</summary>
public class Gathering
{
    /// <summary>Event country.</summary>
    public string Country { get; set; } = "";
    /// <summary>Event state or region.</summary>
    public string State { get; set; } = "";
    /// <summary>Event identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Event title.</summary>
    public string Title { get; set; } = "";
    /// <summary>Cover image URL or storage reference.</summary>
    public string Cover { get; set; } = "";
    /// <summary>Event description.</summary>
    public string Description { get; set; } = "";
    /// <summary>Event start instant.</summary>
    public DateTimeOffset? Start { get; set; }
    /// <summary>Event end instant.</summary>
    public DateTimeOffset? End { get; set; }
    /// <summary>Time zone used to present event times.</summary>
    public string TimeZone { get; set; } = "Asia/Kolkata";
    /// <summary>Event city.</summary>
    public string City { get; set; } = "";
    /// <summary>Event venue.</summary>
    public string Venue { get; set; } = "";
    /// <summary>Organizer name or group.</summary>
    public string Organizer { get; set; } = "";
    /// <summary>Optional registration link.</summary>
    public string? Registration { get; set; }
    /// <summary>Whether the event is published.</summary>
    public bool Published { get; set; }
    /// <summary>Whether the event is featured.</summary>
    public bool Featured { get; set; }
    /// <summary>Whether this is a seeded demo event.</summary>
    public bool Demo { get; set; }
    /// <summary>Account that submitted the event.</summary>
    public Guid? SubmittedBy { get; set; }
    /// <summary>Event submission time.</summary>
    public DateTimeOffset? SubmittedAt { get; set; }
    /// <summary>Whether the event is awaiting moderation.</summary>
    public bool PendingReview { get; set; }
    /// <summary>Private moderation note.</summary>
    public string? ReviewNote { get; set; }
    /// <summary>Time the event was reviewed.</summary>
    public DateTimeOffset? ReviewedAt { get; set; }
    /// <summary>Account that reviewed the event.</summary>
    public Guid? ReviewedBy { get; set; }
}
/// <summary>A member's attendance and check-in state for an event.</summary>
public class Attendance
{
    /// <summary>Attending member identifier.</summary>
    public Guid UserId { get; set; }
    /// <summary>Event identifier.</summary>
    public Guid EventId { get; set; }
    /// <summary>Whether this attendance appears in attendee lists.</summary>
    public bool Visible { get; set; }
    /// <summary>Expiry time for the member's check-in.</summary>
    public DateTimeOffset? CheckedInUntil { get; set; }
}
/// <summary>A member's saved profile or event.</summary>
public class Saved
{
    /// <summary>Member who saved the item.</summary>
    public Guid UserId { get; set; }
    /// <summary>Identifier of the saved item.</summary>
    public Guid TargetId { get; set; }
    /// <summary>Type of saved item.</summary>
    public string Kind { get; set; } = "";
}
/// <summary>An in-app notification for a member.</summary>
public class Notification
{
    /// <summary>Notification identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Recipient member identifier.</summary>
    public Guid UserId { get; set; }
    /// <summary>Notification category.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Notification text.</summary>
    public string Text { get; set; } = "";
    /// <summary>Destination link within the app.</summary>
    public string Url { get; set; } = "";
    /// <summary>Whether the member has read it.</summary>
    public bool Read { get; set; }
    /// <summary>Notification creation time.</summary>
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
/// <summary>A safety or conduct report.</summary>
public class Report
{
    /// <summary>Report identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Reporting member, if signed in.</summary>
    public Guid? ReporterId { get; set; }
    /// <summary>Reported member, when the report concerns a member.</summary>
    public Guid? SubjectId { get; set; }
    /// <summary>Selected report reason.</summary>
    public string Reason { get; set; } = "";
    /// <summary>Additional report details.</summary>
    public string Details { get; set; } = "";
    /// <summary>Optional screenshot reference.</summary>
    public string? Screenshot { get; set; }
    /// <summary>Whether moderation resolved the report.</summary>
    public bool Resolved { get; set; }
    /// <summary>Report submission time.</summary>
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Time the report was resolved.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }
}
/// <summary>Attribution of a new member to their referrer.</summary>
public class Referral
{
    /// <summary>Newly referred member identifier.</summary>
    public Guid ReferredId { get; set; }
    /// <summary>Referring member identifier.</summary>
    public Guid ReferrerId { get; set; }
    /// <summary>Referral creation time.</summary>
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
/// <summary>A revocable, expiring referral invitation.</summary>
public class ReferralInvite
{
    /// <summary>Hashed invitation token and primary key.</summary>
    public string TokenHash { get; set; } = "";
    /// <summary>Member who created the invitation.</summary>
    public Guid ReferrerId { get; set; }
    /// <summary>Invitation creation time.</summary>
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Invitation expiry time.</summary>
    public DateTimeOffset Expires { get; set; }
    /// <summary>Member who redeemed the invitation.</summary>
    public Guid? ReferredId { get; set; }
    /// <summary>Whether the invitation has been revoked.</summary>
    public bool Revoked { get; set; }
}
/// <summary>A member's vegan verification application.</summary>
public class VerificationRequest
{
    /// <summary>Verification request identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Applicant account identifier.</summary>
    public Guid ApplicantId { get; set; }
    /// <summary>Client-generated idempotency identifier.</summary>
    public Guid ClientId { get; set; }
    /// <summary>Current review status.</summary>
    public VerificationState Status { get; set; } = VerificationState.UnderReview;
    /// <summary>Applicant's explanation.</summary>
    public string Explanation { get; set; } = "";
    /// <summary>Evidence links supplied with the application.</summary>
    public string[] EvidenceUrls { get; set; } = [];
    /// <summary>Referrer confirmations associated with this request.</summary>
    public List<VerificationReferral> Referrers { get; set; } = [];
    /// <summary>Application submission time.</summary>
    public DateTimeOffset Submitted { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Time the application was resolved.</summary>
    public DateTimeOffset? Resolved { get; set; }
    /// <summary>Account that reviewed the application.</summary>
    public Guid? ReviewedBy { get; set; }
    /// <summary>Feedback provided to the applicant.</summary>
    public string? Feedback { get; set; }
}
/// <summary>A referrer's response to a verification application.</summary>
public class VerificationReferral
{
    /// <summary>Referral response identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Verification request identifier.</summary>
    public Guid RequestId { get; set; }
    /// <summary>Referring member identifier, if still available.</summary>
    public Guid? ReferrerId { get; set; }
    /// <summary>Current confirmation status.</summary>
    public ReferralState Status { get; set; } = ReferralState.AwaitingConfirmation;
    /// <summary>Time the referrer responded.</summary>
    public DateTimeOffset? Responded { get; set; }
}
public class CommunityDb(DbContextOptions<CommunityDb> options) : DbContext(options)
{
    public DbSet<VerificationReferral> VerificationReferrals => Set<VerificationReferral>();
    public DbSet<VerificationRequest> VerificationRequests => Set<VerificationRequest>();
    public DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();
    public DbSet<ChatKeyRepair> ChatKeyRepairs => Set<ChatKeyRepair>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Relationship> Relationships => Set<Relationship>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<Gathering> Events => Set<Gathering>();
    public DbSet<Attendance> Attendance => Set<Attendance>();
    public DbSet<Saved> Saved => Set<Saved>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<ReferralInvite> ReferralInvites => Set<ReferralInvite>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        // Preserve the existing text columns and values, including the active-request index.
        b.Entity<VerificationRequest>().Property(r => r.Status).HasMaxLength(600)
            .HasConversion(value => value.Label(), value => VerificationStorage.ParseVerification(value));
        b.Entity<VerificationReferral>().Property(r => r.Status).HasMaxLength(600)
            .HasConversion(value => value.Label(), value => VerificationStorage.ParseReferral(value));
        b.Entity<VerificationRequest>().HasOne<Account>().WithMany().HasForeignKey(r => r.ApplicantId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<VerificationRequest>().HasMany(r => r.Referrers).WithOne().HasForeignKey(r => r.RequestId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<VerificationReferral>().HasOne<Account>().WithMany().HasForeignKey(r => r.ReferrerId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<VerificationReferral>().HasIndex(r => new { r.RequestId, r.ReferrerId }).IsUnique();
        b.Entity<VerificationRequest>().HasIndex(r => new { r.ApplicantId, r.ClientId }).IsUnique();
        b.Entity<VerificationRequest>().HasIndex(r => r.ApplicantId).IsUnique().HasFilter("\"Status\" = 'Under review'");
        b.Entity<VerificationRequest>().HasIndex(r => new { r.Status, r.Submitted });
        b.Entity<Profile>().Property(x => x.FeatureConsent).HasDefaultValue(true);
        b.Entity<Account>().HasIndex(x => x.Subject).IsUnique();
        b.Entity<Profile>().HasOne<Account>().WithOne().HasForeignKey<Profile>(x => x.Id).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Relationship>().HasKey(x => x.Key);
        b.Entity<Relationship>().HasIndex(x => new { x.A, x.B }).IsUnique();
        b.Entity<Relationship>().HasOne<Account>().WithMany().HasForeignKey(x => x.A).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Relationship>().HasOne<Account>().WithMany().HasForeignKey(x => x.B).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Block>().HasKey(x => new { x.Owner, x.Subject });
        b.Entity<Block>().HasOne<Account>().WithMany().HasForeignKey(x => x.Owner).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Block>().HasOne<Account>().WithMany().HasForeignKey(x => x.Subject).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChatMessage>().HasIndex(x => new { x.SenderId, x.ClientId }).IsUnique();
        b.Entity<ChatMessage>().HasIndex(x => new { x.Conversation, x.Id });
        b.Entity<ChatMessage>().HasIndex(x => x.Id).HasDatabaseName("IX_Messages_PendingPush").HasFilter("\"PushPending\" = TRUE");
        b.Entity<PushDelivery>().HasKey(x => new { x.MessageId, x.DeviceId });
        b.Entity<PushDelivery>().HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PushDelivery>().HasOne<PushDevice>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChatMessage>().HasOne<Relationship>().WithMany().HasForeignKey(x => x.Conversation).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Attendance>().HasKey(x => new { x.UserId, x.EventId });
        b.Entity<Attendance>().HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Attendance>().HasOne<Gathering>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Saved>().HasKey(x => new { x.UserId, x.TargetId, x.Kind });
        b.Entity<Saved>().HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Notification>().HasIndex(x => new { x.UserId, x.Created });
        b.Entity<Notification>().HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Report>().HasOne<Account>().WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Report>().HasOne<Account>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Gathering>().HasOne<Account>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Gathering>().HasIndex(x => new { x.PendingReview, x.Demo });
        b.Entity<Referral>().HasKey(x => x.ReferredId);
        b.Entity<Referral>().HasOne<Account>().WithMany().HasForeignKey(x => x.ReferredId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Referral>().HasIndex(x => x.ReferrerId);
        b.Entity<ReferralInvite>().HasKey(x => x.TokenHash);
        b.Entity<ReferralInvite>().HasIndex(x => x.ReferrerId);
        b.Entity<ReferralInvite>().HasIndex(x => x.ReferredId).IsUnique();
        foreach (var e in b.Model.GetEntityTypes()) foreach (var p in e.GetProperties())
            if (p.ClrType == typeof(string)) p.SetMaxLength(p.Name is "Bio" or "Description" or "Text" ? 4000 : 600);
        b.Entity<VerificationRequest>().Property(r => r.Explanation).HasMaxLength(4000);
        b.Entity<Relationship>().Property(r => r.IntroductionKeyId).HasMaxLength(32);
        b.Entity<ChatMessage>().Property(m => m.BodyKeyId).HasMaxLength(32);
        b.Entity<ChatKeyRepair>().HasKey(x => x.KeyId);
        b.Entity<ChatKeyRepair>().Property(x => x.KeyId).HasMaxLength(32);
        b.Entity<Report>().Property(r => r.Details).HasMaxLength(1000).HasDefaultValue("");
        b.Entity<Profile>().Property(x => x.Gender).HasMaxLength(40).HasDefaultValue("");
        b.Entity<Profile>().Property(x => x.Profession).HasMaxLength(80).HasDefaultValue("");
        b.Entity<PushDevice>().HasIndex(x => x.Token).IsUnique();
        b.Entity<PushDevice>().Property(x => x.Token).HasMaxLength(4096);
        b.Entity<PushDevice>().HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
/// <summary>A member's registered device for push notifications.</summary>
public class PushDevice
{
    /// <summary>Push-device identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Owning member identifier.</summary>
    public Guid UserId { get; set; }
    /// <summary>Push provider device token.</summary>
    public string Token { get; set; } = "";
    /// <summary>Time the token was last updated.</summary>
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A push attempt linking a message to a target device.</summary>
/// <summary>A chat key whose cohort verification has started but not finished.</summary>
public class ChatKeyRepair
{
    /// <summary>Key ID whose messages and introductions must all be verified.</summary>
    public string KeyId { get; set; } = "";
}

public class PushDelivery
{
    /// <summary>Message being delivered.</summary>
    public long MessageId { get; set; }
    /// <summary>Target device.</summary>
    public Guid DeviceId { get; set; }
}
