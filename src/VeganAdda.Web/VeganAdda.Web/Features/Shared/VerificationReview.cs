using VeganAdda.Contracts;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Features.Shared;

// A vegan verification request reaches reviewers through either of two parallel paths:
// a confirmation from a verified vegan, or a social profile (Instagram or LinkedIn) reviewers can check.
public static class VerificationReview
{
    public static IQueryable<Profile> Awaiting(CommunityDb db) => db.Profiles.Where(BasicProfile.Complete)
        .Where(p => p.IsVegan == true && !p.VeganVerified && db.Accounts.Any(a => a.Id == p.Id && !a.Suspended)
            && db.VerificationRequests.Any(r => r.ApplicantId == p.Id && r.Status == VerificationState.UnderReview
                && (r.Referrers.Any(x => x.Status == ReferralState.Confirmed)
                    || p.Instagram != null || p.LinkedIn != null || r.EvidenceUrls.Length > 0
                    || r.Explanation.ToLower().Contains("instagram.com") || r.Explanation.ToLower().Contains("linkedin.com"))));

    public static bool HasSocialProfile(Profile profile, string explanation, string[] evidenceUrls) =>
        profile.Instagram != null || profile.LinkedIn != null || evidenceUrls.Length > 0
        || explanation.Contains("instagram.com", StringComparison.OrdinalIgnoreCase)
        || explanation.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase);
}
