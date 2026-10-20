using Microsoft.EntityFrameworkCore;

namespace VeganAdda.Web.Data;

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Subject { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Suspended { get; set; }
    public bool Admin { get; set; }
    public bool Moderator { get; set; }
    public bool Verified { get; set; }
    public bool TrustRevoked { get; set; }
    public bool Demo { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
public class Profile
{
    // Month-only birth details use month end for conservative age calculations.
    // The stored day is an actual birthday only when HasExactBirthDate is true.
    public DateOnly? BirthDate { get; set; }
    public bool HasExactBirthDate { get; set; }
    // Private coordinates: never include in member-facing or moderation projections.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "India";
    public string State { get; set; } = "";
    public string Bio { get; set; } = "";
    public string Photo { get; set; } = "";
    public string[] Interests { get; set; } = [];
    public string[] LookingFor { get; set; } = [];
    public string? Instagram { get; set; }
    public string? LinkedIn { get; set; }
    public string? Website { get; set; }
    public bool Public { get; set; }
    public bool Approved { get; set; }
    public bool? IsVegan { get; set; }
    public bool VeganVerificationPending { get; set; }
    public bool TestProfile { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public bool Featured { get; set; }
    public bool FeatureConsent { get; set; }
    public bool DefaultAttendeeVisible { get; set; }
    public DateTimeOffset AdultConfirmedAt { get; set; }
}
public class Relationship
{
    public string Key { get; set; } = "";
    public Guid A { get; set; }
    public Guid B { get; set; }
    public Guid RequestedBy { get; set; }
    public string State { get; set; } = "pending";
    public string Introduction { get; set; } = "";
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public long ReadA { get; set; }
    public long ReadB { get; set; }
}
public class Block
{
    public Guid Owner { get; set; }
    public Guid Subject { get; set; }
}
public class ChatMessage
{
    public bool PushPending { get; set; }
    public int PushAttempts { get; set; }
    public long Id { get; set; }
    public string Conversation { get; set; } = "";
    public Guid SenderId { get; set; }
    public Guid ClientId { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
public class Gathering
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Cover { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string City { get; set; } = "";
    public string Venue { get; set; } = "";
    public string Organizer { get; set; } = "";
    public string? Registration { get; set; }
    public bool Published { get; set; }
    public bool Featured { get; set; }
    public bool Demo { get; set; }
    public Guid? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public bool PendingReview { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
}
public class Attendance
{
    public Guid UserId { get; set; }
    public Guid EventId { get; set; }
    public bool Visible { get; set; }
}
public class Saved
{
    public Guid UserId { get; set; }
    public Guid TargetId { get; set; }
    public string Kind { get; set; } = "";
}
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Read { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ReporterId { get; set; }
    public Guid? SubjectId { get; set; }
    public string Reason { get; set; } = "";
    public bool Resolved { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
public class Referral
{
    public Guid ReferredId { get; set; }
    public Guid ReferrerId { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
public class ReferralInvite
{
    public string TokenHash { get; set; } = "";
    public Guid ReferrerId { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Expires { get; set; }
    public Guid? ReferredId { get; set; }
    public bool Revoked { get; set; }
}
public class CommunityDb(DbContextOptions<CommunityDb> options) : DbContext(options)
{
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
        b.Entity<PushDevice>().HasIndex(x => x.Token).IsUnique();
        b.Entity<PushDevice>().Property(x => x.Token).HasMaxLength(4096);
        b.Entity<PushDevice>().HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
public class PushDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Token { get; set; } = "";
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
}
