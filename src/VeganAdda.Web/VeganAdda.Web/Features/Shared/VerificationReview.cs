using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Features.Shared;

// A vegan verification request reaches reviewers through either of two parallel paths:
// a confirmation from a verified vegan, or a social profile (Instagram or LinkedIn) reviewers can check.
// A confirmation keeps counting once given; reviewers see each referrer's current status on the request.
public static class VerificationReview
{
    const string Instagram = "instagram.com", LinkedIn = "linkedin.com";

    public static IQueryable<Profile> Awaiting(CommunityDb db) => db.Profiles.Where(BasicProfile.Complete)
        .Where(p => p.IsVegan == true && !p.VeganVerified && db.Accounts.Any(a => a.Id == p.Id && !a.Suspended)
            && db.VerificationRequests.Any(r => r.ApplicantId == p.Id && r.Status == VerificationState.UnderReview
                && (r.Referrers.Any(x => x.Status == ReferralState.Confirmed)
                    || p.Instagram != null || p.LinkedIn != null || r.EvidenceUrls.Length > 0
                    || r.Explanation.ToLower().Contains(Instagram) || r.Explanation.ToLower().Contains(LinkedIn))));

    public static bool HasSocialProfile(Profile profile, string explanation, string[] evidenceUrls) =>
        profile.Instagram != null || profile.LinkedIn != null || evidenceUrls.Length > 0
        || explanation.Contains(Instagram, StringComparison.OrdinalIgnoreCase)
        || explanation.Contains(LinkedIn, StringComparison.OrdinalIgnoreCase);

    public static Task<bool> IsAwaiting(CommunityDb db, Guid applicant) => Awaiting(db).AnyAsync(p => p.Id == applicant);

    public static Task<List<Guid>> Reviewers(CommunityDb db, bool testProfile = false) => db.Accounts
        .Where(a => !a.Suspended && (a.Admin || (!testProfile && a.Moderator && db.Profiles.Where(BasicProfile.Complete)
            .Any(p => p.Id == a.Id && p.IsVegan == true && p.VeganVerified && !p.TestProfile))))
        .Select(a => a.Id).ToListAsync();

    // Adds (unsaved) reviewer notifications and returns the reviewers to invalidate.
    public static async Task<List<Guid>> NotifyReviewers(CommunityDb db, string text)
    {
        var reviewers = await Reviewers(db);
        foreach (var reviewer in reviewers)
            db.Notifications.Add(new() { UserId = reviewer, Kind = "verification-request", Text = text, Url = "/moderation" });
        return reviewers;
    }
}
