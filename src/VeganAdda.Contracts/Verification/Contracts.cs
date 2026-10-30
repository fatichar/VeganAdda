using System.Text.Json.Serialization;

namespace VeganAdda.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<VerificationState>))]
public enum VerificationState
{
    [JsonStringEnumMemberName("Not requested")] NotRequested,
    [JsonStringEnumMemberName("Under review")] UnderReview,
    [JsonStringEnumMemberName("Changes requested")] ChangesRequested,
    [JsonStringEnumMemberName("Verified vegan")] Verified,
    Withdrawn
}

[JsonConverter(typeof(JsonStringEnumConverter<ReferralState>))]
public enum ReferralState
{
    [JsonStringEnumMemberName("Not requested")] NotRequested,
    [JsonStringEnumMemberName("Awaiting confirmation")] AwaitingConfirmation,
    Confirmed,
    Declined,
    Unavailable
}

public static class VerificationLabels
{
    public static string Label(this VerificationState state) => state switch
    {
        VerificationState.NotRequested => "Not requested",
        VerificationState.UnderReview => "Under review",
        VerificationState.ChangesRequested => "Changes requested",
        VerificationState.Verified => "Verified vegan",
        VerificationState.Withdrawn => "Withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    public static string Label(this ReferralState state) => state switch
    {
        ReferralState.NotRequested => "Not requested",
        ReferralState.AwaitingConfirmation => "Awaiting confirmation",
        ReferralState.Confirmed => "Confirmed",
        ReferralState.Declined => "Declined",
        ReferralState.Unavailable => "Unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}

public record VerificationSubmission(string Explanation, string[] EvidenceUrls, Guid? ReferrerId, Guid ClientId, Guid[]? ReferrerIds = null);
public record VerificationCandidate(Guid Id, string Name);
public record VerificationResponse(bool Confirm);
public record VerificationRequestAction(Guid Id);
public record VerificationDetails(Guid Id, VerificationState Status, string Explanation, string[] EvidenceUrls,
    Guid? ReferrerId, string? ReferrerName, ReferralState ReferralStatus, string? Feedback, DateTimeOffset Submitted, VerificationReferrer[]? Referrers = null);
public record VerificationReferrer(Guid? Id, string? Name, ReferralState Status);
public record VerificationStatus(VerificationState Status, VerificationDetails? Request);
// Participant projection deliberately excludes evidence and moderator feedback.
public record ReferralCard(Guid Id, Guid ApplicantId, string ApplicantName, VerificationState Status, ReferralState ReferralStatus, bool CanRespond,
    ReferralActivity[] Activities);

public enum ReferralActivityKind { Requested, Confirmed, Declined, Verified, Withdrawn, ChangesRequested }
public record ReferralActivity(Guid RequestId, ReferralActivityKind Kind, DateTimeOffset OccurredAt, bool ByViewer);

public interface IVerificationApi
{
    Task<VerificationStatus> GetVerification();
    Task<Ok> SubmitVerification(VerificationSubmission input);
    Task<Ok> WithdrawVerification(Guid id);
    Task<List<VerificationCandidate>> SearchReferrers(string search);
    Task<List<ReferralCard>> GetReferralCards(Guid personId);
    Task<Ok> RespondToReferral(Guid id, bool confirm);
}
