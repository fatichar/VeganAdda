using VeganAdda.Contracts;

namespace VeganAdda.Web.Data;

internal static class VerificationStorage
{
    public static VerificationState ParseVerification(string value) => value switch
    {
        "Not requested" => VerificationState.NotRequested,
        "Under review" => VerificationState.UnderReview,
        "Changes requested" => VerificationState.ChangesRequested,
        "Verified vegan" => VerificationState.Verified,
        "Withdrawn" => VerificationState.Withdrawn,
        _ => throw new InvalidOperationException("Unknown stored verification status.")
    };

    public static ReferralState ParseReferral(string value) => value switch
    {
        "Not requested" => ReferralState.NotRequested,
        "Awaiting confirmation" => ReferralState.AwaitingConfirmation,
        "Confirmed" => ReferralState.Confirmed,
        "Declined" => ReferralState.Declined,
        "Unavailable" => ReferralState.Unavailable,
        _ => throw new InvalidOperationException("Unknown stored referral status.")
    };
}
