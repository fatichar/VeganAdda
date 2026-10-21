namespace VeganAdda.Web.Observability;

public enum LifecycleEvent
{
    AccountRegistered, AccountSignedIn, AccountDeleted,
    ProfileCreated, ProfileUpdated, ProfileResubmitted,
    ProfileApproved, ProfileChangesRequested, ProfileApprovalRevoked
}

// Record committed transitions only. Never pass profile content or identity credentials.
public sealed class LifecycleEvents(ILogger<LifecycleEvents> logger)
{
    public const string Category = "VeganAdda.Web.Observability.LifecycleEvents";

    public void Record(LifecycleEvent eventName, Guid accountId, Guid? actorId = null) =>
        logger.LogInformation("Lifecycle {EventName} for account {AccountId} by actor {ActorId}",
            eventName.ToString(), accountId, actorId ?? accountId);
}
