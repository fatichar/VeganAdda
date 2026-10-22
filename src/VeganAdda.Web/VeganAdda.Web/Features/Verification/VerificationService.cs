using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Verification;

public sealed class VerificationService(FeatureContext context) : FeatureService(context)
{
    private IQueryable<Profile> EligibleReferrers(Guid applicant) => db.Profiles.Where(BasicProfile.Complete)
        .Where(p => p.Id != applicant && p.IsVegan == true && p.VeganVerified && !p.TestProfile
            && db.Accounts.Any(a => a.Id == p.Id && !a.Suspended)
            && !db.Blocks.Any(b => (b.Owner == applicant && b.Subject == p.Id) || (b.Owner == p.Id && b.Subject == applicant)));

    internal Task<bool> Eligible(Guid applicant, Guid referrer) => EligibleReferrers(applicant).AnyAsync(p => p.Id == referrer);

    internal Task<List<Guid>> Reviewers(bool testProfile = false) => db.Accounts
        .Where(a => !a.Suspended && (a.Admin || (!testProfile && a.Moderator && db.Profiles.Where(BasicProfile.Complete)
            .Any(p => p.Id == a.Id && p.IsVegan == true && p.VeganVerified && !p.TestProfile))))
        .Select(a => a.Id).ToListAsync();

    internal async Task<VerificationDetails?> Details(Guid applicant)
    {
        var row = await db.VerificationRequests.Where(r => r.ApplicantId == applicant)
            .OrderByDescending(r => r.Submitted).ThenByDescending(r => r.Id).FirstOrDefaultAsync();
        if (row == null) return null;
        var name = row.ReferrerId.HasValue ? await db.Accounts.Where(a => a.Id == row.ReferrerId).Select(a => a.Name).FirstOrDefaultAsync() : null;
        var response = row.ReferralStatus;
        if (response is "Awaiting confirmation" or "Confirmed" && (!row.ReferrerId.HasValue || !await Eligible(applicant, row.ReferrerId.Value)))
            response = "Unavailable";
        return new(row.Id, row.Status, row.Explanation, row.EvidenceUrls, row.ReferrerId, name, response, row.Feedback, row.Submitted);
    }

    public async Task<VerificationStatus> GetVerification()
    {
        var me = await User(allowIncomplete: true);
        var profile = await db.Profiles.FindAsync(me.Id);
        var details = await Details(me.Id);
        return new(profile?.VeganVerified == true ? VerificationStates.Verified
            : details?.Status is VerificationStates.UnderReview or VerificationStates.ChangesRequested ? details.Status : VerificationStates.NotRequested, details);
    }

    public async Task<List<VerificationCandidate>> SearchReferrers(string search)
    {
        var me = await User();
        search = Text(search, 80);
        if (search.Length < 2 || await db.Profiles.AnyAsync(p => p.Id == me.Id && p.TestProfile)) return [];
        var term = search.ToLowerInvariant();
        return await EligibleReferrers(me.Id).Where(p => p.Name.ToLower().Contains(term))
            .OrderBy(p => p.Name).ThenBy(p => p.Id).Take(10).Select(p => new VerificationCandidate(p.Id, p.Name)).ToListAsync();
    }

    public async Task<Ok> SubmitVerification(VerificationSubmission input)
    {
        var me = await User();
        var explanation = Text(input.Explanation, 4000);
        if (input.ClientId == Guid.Empty || input.EvidenceUrls == null || input.EvidenceUrls.Length > 5)
            throw new ApiException(400, "Add up to five evidence links.");
        var urls = input.EvidenceUrls.Select(u => Url(u)).Where(u => u != null).Cast<string>().Distinct().ToArray();
        if (explanation.Length == 0 && urls.Length == 0 && !input.ReferrerId.HasValue)
            throw new ApiException(400, "Add verification details or choose a verified vegan who can refer you.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await UserLock(me.Id);
        if (await db.VerificationRequests.AnyAsync(r => r.ApplicantId == me.Id && r.ClientId == input.ClientId)) return new Ok();
        var profile = await db.Profiles.SingleAsync(p => p.Id == me.Id);
        if (profile.IsVegan != true || profile.VeganVerified || profile.TestProfile)
            throw new ApiException(409, "Only unverified vegan members with real profiles can request verification.");
        if (await db.VerificationRequests.AnyAsync(r => r.ApplicantId == me.Id && r.Status == VerificationStates.UnderReview))
            throw new ApiException(409, "Your verification request is already under review.");
        if (input.ReferrerId is { } referrer)
        {
            await PairLock(Pair(me.Id, referrer));
            if (!await Eligible(me.Id, referrer)) throw new ApiException(409, "Choose an active verified vegan member.");
        }
        db.VerificationRequests.Add(new() { ApplicantId = me.Id, ClientId = input.ClientId, Explanation = explanation,
            EvidenceUrls = urls, ReferrerId = input.ReferrerId, ReferralStatus = input.ReferrerId.HasValue ? "Awaiting confirmation" : "Not requested" });
        var recipients = await Reviewers();
        foreach (var recipient in recipients)
            Notice(recipient, "verification-request", "A member requested vegan verification.", "/moderation");
        if (input.ReferrerId is { } target)
        {
            Notice(target, "referral-request", "A member asked you to confirm they are vegan.", $"/people/{me.Id}#vegan-confirmation");
            recipients.Add(target);
        }
        profile.ReviewNote = null;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await changes.Changed(ChangeArea.Profiles | ChangeArea.Notifications | ChangeArea.Messaging, recipients.Append(me.Id).Distinct().ToArray());
        return new Ok();
    }

    public async Task<Ok> WithdrawVerification(Guid id)
    {
        var me = await User();
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(me.Id);
        var row = await db.VerificationRequests.SingleOrDefaultAsync(r => r.Id == id && r.ApplicantId == me.Id) ?? throw Missing();
        if (row.Status != VerificationStates.UnderReview) throw new ApiException(409, "This request is no longer under review.");
        row.Status = VerificationStates.Withdrawn; row.Resolved = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        var recipients = await Reviewers();
        if (row.ReferrerId.HasValue) recipients.Add(row.ReferrerId.Value);
        await changes.Changed(ChangeArea.Profiles | ChangeArea.Messaging, recipients.Append(me.Id).Distinct().ToArray());
        return new Ok();
    }

    public async Task<List<ReferralCard>> GetReferralCards(Guid personId)
    {
        var me = await User();
        if (await Blocked(me.Id, personId) || await db.Profiles.AnyAsync(p => (p.Id == me.Id || p.Id == personId) && p.TestProfile)
            || !await db.Accounts.AnyAsync(a => a.Id == personId && !a.Suspended)) return [];
        var rows = await db.VerificationRequests.Where(r => (r.ApplicantId == me.Id && r.ReferrerId == personId)
            || (r.ApplicantId == personId && r.ReferrerId == me.Id)).OrderByDescending(r => r.Submitted).ThenByDescending(r => r.Id).Take(1).ToListAsync();
        var cards = new List<ReferralCard>();
        foreach (var row in rows)
        {
            var eligible = row.ReferrerId.HasValue && await Eligible(row.ApplicantId, row.ReferrerId.Value);
            var response = !eligible && row.ReferralStatus is "Awaiting confirmation" or "Confirmed" ? "Unavailable" : row.ReferralStatus;
            var name = await db.Accounts.Where(a => a.Id == row.ApplicantId).Select(a => a.Name).SingleAsync();
            cards.Add(new(row.Id, row.ApplicantId, name, row.Status, response,
                row.ReferrerId == me.Id && eligible && row.Status == VerificationStates.UnderReview && row.ReferralStatus == "Awaiting confirmation"));
        }
        return cards;
    }

    public async Task<Ok> RespondToReferral(Guid id, VerificationResponse input)
    {
        var me = await User();
        var applicant = await db.VerificationRequests.Where(r => r.Id == id && r.ReferrerId == me.Id).Select(r => (Guid?)r.ApplicantId).SingleOrDefaultAsync() ?? throw Missing();
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(applicant); await PairLock(Pair(applicant, me.Id));
        var row = await db.VerificationRequests.SingleAsync(r => r.Id == id);
        if (row.Status != VerificationStates.UnderReview || !await Eligible(applicant, me.Id)
            || !await db.Profiles.Where(BasicProfile.Complete).AnyAsync(p => p.Id == applicant && p.IsVegan == true && !p.TestProfile)
            || !await db.Accounts.AnyAsync(a => a.Id == applicant && !a.Suspended))
            throw new ApiException(409, "This confirmation request is no longer available.");
        var response = input.Confirm ? "Confirmed" : "Declined";
        if (row.ReferralStatus == response) return new Ok();
        if (row.ReferralStatus != "Awaiting confirmation") throw new ApiException(409, "You have already responded to this request.");
        row.ReferralStatus = response; row.Responded = DateTimeOffset.UtcNow;
        var recipients = await Reviewers();
        foreach (var recipient in recipients)
            Notice(recipient, "referral-response", "A member responded to a vegan verification request.", "/moderation");
        Notice(applicant, "referral-response", input.Confirm ? "Your referrer confirmed you are vegan. A moderator will review your request." : "Your referrer declined to confirm your vegan status. Your request remains under review.", "/settings/profile#verification");
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await changes.Changed(ChangeArea.Profiles | ChangeArea.Notifications | ChangeArea.Messaging, recipients.Append(applicant).Append(me.Id).Distinct().ToArray());
        return new Ok();
    }
}
