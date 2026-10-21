namespace VeganAdda.Contracts;

public static class VerificationStates
{
    public const string NotRequested = "Not requested";
    public const string UnderReview = "Under review";
    public const string ChangesRequested = "Changes requested";
    public const string Verified = "Verified vegan";
    public const string Withdrawn = "Withdrawn";
}

public record VerificationSubmission(string Explanation, string[] EvidenceUrls, Guid? ReferrerId, Guid ClientId);
public record VerificationCandidate(Guid Id, string Name);
public record VerificationResponse(bool Confirm);
public record VerificationRequestAction(Guid Id);
public record VerificationDetails(Guid Id, string Status, string Explanation, string[] EvidenceUrls,
    Guid? ReferrerId, string? ReferrerName, string ReferralStatus, string? Feedback, DateTimeOffset Submitted);
public record VerificationStatus(string Status, VerificationDetails? Request);
// Participant projection deliberately excludes evidence and moderator feedback.
public record ReferralCard(Guid Id, Guid ApplicantId, string ApplicantName, string Status, string ReferralStatus, bool CanRespond);

public interface IVerificationApi
{
    Task<VerificationStatus> GetVerification();
    Task<Ok> SubmitVerification(VerificationSubmission input);
    Task<Ok> WithdrawVerification(Guid id);
    Task<List<VerificationCandidate>> SearchReferrers(string search);
    Task<List<ReferralCard>> GetReferralCards(Guid personId);
    Task<Ok> RespondToReferral(Guid id, bool confirm);
}
