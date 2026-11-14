namespace VeganAdda.Web.Observability;

public enum LifecycleEvent
{
    AccountRegistered, AccountSignedIn, AccountDeleted,
    ProfileCreated, ProfileUpdated,
    VeganConfirmed, VeganVerificationFeedback, VeganVerificationRemoved,
    // Member verification requests and verified-vegan referral responses.
    VerificationRequested, VerificationWithdrawn, VerificationReferralConfirmed, VerificationReferralDeclined,
    // Administrator actions on an account (actor is the administrator).
    MemberSuspended, MemberRestored, AdminGranted, AdminRevoked, ModeratorGranted, ModeratorRevoked,
    ProfileFeatured, ProfileUnfeatured, TestProfileMarked, TestProfileCleared, ProfileChangesRequested,
    // Safety. Blocks record only the member who acted, never whom they blocked.
    MemberReported, MemberBlocked, MemberUnblocked,
    // Connections record only the member who acted, never the other member.
    ConnectionRequested, ConnectionAccepted, ConnectionDeclined, ConnectionCancelled, ConnectionRemoved,
    // Subject-scoped transitions (see RecordSubject).
    ReportResolved, EventSubmitted, EventApproved, EventRejected, EventCreated, EventUpdated, EventPublished, EventUnpublished
}

// Record committed transitions only. Never pass profile content, message content, report details,
// coordinates or identity credentials: identifiers, event names and counts only.
public sealed class LifecycleEvents(ILogger<LifecycleEvents> logger)
{
    public const string Category = "VeganAdda.Web.Observability.LifecycleEvents";

    public void Record(LifecycleEvent eventName, Guid accountId, Guid? actorId = null) =>
        logger.LogInformation("Lifecycle {EventName} for account {AccountId} by actor {ActorId}",
            eventName.ToString(), accountId, actorId ?? accountId);

    /// <summary>Records a transition on a non-account record such as a community event or a report.</summary>
    public void RecordSubject(LifecycleEvent eventName, string subjectKind, Guid subjectId, Guid actorId) =>
        logger.LogInformation("Lifecycle {EventName} for {SubjectKind} {SubjectId} by actor {ActorId}",
            eventName.ToString(), subjectKind, subjectId, actorId);

    // One summary per bulk run: counts only, never recipients or addresses. Sent means the SMTP
    // server accepted the message, not that it reached the inbox.
    public void RecordProfileReminders(Guid actorId, int sent, int skipped, int failed) =>
        logger.LogInformation("Lifecycle {EventName} by actor {ActorId}: {Sent} sent, {Skipped} skipped, {Failed} failed",
            "ProfileRemindersSent", actorId, sent, skipped, failed);
}
