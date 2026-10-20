namespace VeganAdda.Web.Features.Messaging;

internal readonly record struct MessagingParty(bool Active, bool TestProfile, bool Admin);

internal static class MessagingPolicy
{
    // The same eligibility rule is used for writes/history metadata and batched inbox cards.
    internal static bool CanMessage(string state, bool blocked, MessagingParty first, MessagingParty second)
        => state == "accepted" && !blocked && first.Active && second.Active
            && !(first.TestProfile && !second.TestProfile && !second.Admin)
            && !(second.TestProfile && !first.TestProfile && !first.Admin);
}
