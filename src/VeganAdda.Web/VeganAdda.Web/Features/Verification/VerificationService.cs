using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Verification;

public sealed class VerificationService(FeatureContext context) : FeatureService(context)
{
    private static ReferralState ReferralStatus(ReferralState response, bool eligible) =>
        !eligible && response is ReferralState.AwaitingConfirmation or ReferralState.Confirmed ? ReferralState.Unavailable : response;

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
        var row = await db.VerificationRequests.Include(r => r.Referrers).Where(r => r.ApplicantId == applicant)
            .OrderByDescending(r => r.Submitted).ThenByDescending(r => r.Id).FirstOrDefaultAsync();
        if (row == null) return null;
        var ids = row.Referrers.Where(r => r.ReferrerId.HasValue).Select(r => r.ReferrerId!.Value).ToArray();
        var names = await db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name);
        var eligible = await EligibleReferrers(applicant).Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync();
        var referrers = row.Referrers.OrderBy(r => r.Id).Select(r => new VerificationReferrer(r.ReferrerId,
            r.ReferrerId.HasValue ? names.GetValueOrDefault(r.ReferrerId.Value) : null,
            ReferralStatus(r.Status, r.ReferrerId.HasValue && eligible.Contains(r.ReferrerId.Value)))).ToArray();
        var first = referrers.FirstOrDefault();
        return new(row.Id, row.Status, row.Explanation, row.EvidenceUrls, first?.Id, first?.Name,
            first?.Status ?? ReferralState.NotRequested, row.Feedback, row.Submitted, referrers);
    }

    public async Task<VerificationStatus> GetVerification()
    {
        var me = await User(allowIncomplete: true);
        var profile = await db.Profiles.FindAsync(me.Id);
        var details = await Details(me.Id);
        return new(profile?.VeganVerified == true ? VerificationState.Verified
            : details?.Status is VerificationState.UnderReview or VerificationState.ChangesRequested ? details.Status : VerificationState.NotRequested, details);
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
        var referrers = (input.ReferrerIds ?? []).Concat(input.ReferrerId.HasValue ? [input.ReferrerId.Value] : []).Distinct().Order().ToArray();
        if (referrers.Length > 10 || referrers.Contains(Guid.Empty)) throw new ApiException(400, "Choose up to ten verified vegans.");
        var urls = input.EvidenceUrls.Select(u => Url(u)).Where(u => u != null).Cast<string>().Distinct().ToArray();
        if (explanation.Length == 0 && urls.Length == 0 && referrers.Length == 0)
            throw new ApiException(400, "Add verification details or choose a verified vegan who can refer you.");
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var key in referrers.Select(id => Pair(me.Id, id)).Order()) await PairLock(key);
        await UserLock(me.Id);
        if (await db.VerificationRequests.AnyAsync(r => r.ApplicantId == me.Id && r.ClientId == input.ClientId)) return new Ok();
        var profile = await db.Profiles.SingleAsync(p => p.Id == me.Id);
        if (profile.IsVegan != true || profile.VeganVerified || profile.TestProfile)
            throw new ApiException(409, "Only unverified vegan members with real profiles can request verification.");
        if (await db.VerificationRequests.AnyAsync(r => r.ApplicantId == me.Id && r.Status == VerificationState.UnderReview))
            throw new ApiException(409, "Your verification request is already under review.");
        foreach (var referrer in referrers)
        {
            if (!await Eligible(me.Id, referrer)) throw new ApiException(409, "Choose an active verified vegan member.");
        }
        db.VerificationRequests.Add(new() { ApplicantId = me.Id, ClientId = input.ClientId, Explanation = explanation,
            EvidenceUrls = urls, Referrers = referrers.Select(id => new VerificationReferral { ReferrerId = id }).ToList() });
        var recipients = await Reviewers();
        foreach (var recipient in recipients)
            Notice(recipient, "verification-request", "A member requested vegan verification.", "/moderation");
        foreach (var target in referrers)
        {
            var key = Pair(me.Id, target);
            var relationship = await db.Relationships.FindAsync(key);
            if (relationship == null)
            {
                relationship = new Relationship { Key = key,
                    A = string.CompareOrdinal(me.Id.ToString(), target.ToString()) < 0 ? me.Id : target,
                    B = string.CompareOrdinal(me.Id.ToString(), target.ToString()) < 0 ? target : me.Id,
                    State = "referral", RequestedBy = me.Id };
                db.Relationships.Add(relationship);
            }
            relationship.Updated = DateTimeOffset.UtcNow;
            Notice(target, "referral-request", "A member asked you to confirm they are vegan.", "/messages/" + key);
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
        if (row.Status != VerificationState.UnderReview) throw new ApiException(409, "This request is no longer under review.");
        row.Status = VerificationState.Withdrawn; row.Resolved = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        var recipients = await Reviewers();
        recipients.AddRange(await db.VerificationReferrals.Where(r => r.RequestId == row.Id && r.ReferrerId != null).Select(r => r.ReferrerId!.Value).ToListAsync());
        await changes.Changed(ChangeArea.Profiles | ChangeArea.Messaging, recipients.Append(me.Id).Distinct().ToArray());
        return new Ok();
    }

    public async Task<List<ReferralCard>> GetReferralCards(Guid personId)
    {
        var me = await User();
        if (await Blocked(me.Id, personId) || await db.Profiles.AnyAsync(p => (p.Id == me.Id || p.Id == personId) && p.TestProfile)
            || !await db.Accounts.AnyAsync(a => a.Id == personId && !a.Suspended)) return [];
        var rows = await db.VerificationRequests.Include(r => r.Referrers).Where(r => (r.ApplicantId == me.Id && r.Referrers.Any(x => x.ReferrerId == personId))
            || (r.ApplicantId == personId && r.Referrers.Any(x => x.ReferrerId == me.Id))).OrderByDescending(r => r.Submitted).ThenByDescending(r => r.Id).Take(1).ToListAsync();
        var cards = new List<ReferralCard>();
        foreach (var row in rows)
        {
            var referral = row.Referrers.Single(r => r.ReferrerId == (row.ApplicantId == me.Id ? personId : me.Id));
            var eligible = referral.ReferrerId.HasValue && await Eligible(row.ApplicantId, referral.ReferrerId.Value);
            var response = ReferralStatus(referral.Status, eligible);
            var name = await db.Accounts.Where(a => a.Id == row.ApplicantId).Select(a => a.Name).SingleAsync();
            List<ReferralActivity> activities = [new(row.Id, ReferralActivityKind.Requested, row.Submitted, row.ApplicantId == me.Id)];
            if (referral.Responded is { } responded && referral.Status is ReferralState.Confirmed or ReferralState.Declined)
                activities.Add(new(row.Id, referral.Status == ReferralState.Confirmed ? ReferralActivityKind.Confirmed : ReferralActivityKind.Declined,
                    responded, referral.ReferrerId == me.Id));
            if (row.Resolved is { } resolved)
            {
                ReferralActivityKind? kind = row.Status switch
                {
                    VerificationState.Verified => ReferralActivityKind.Verified,
                    VerificationState.Withdrawn => ReferralActivityKind.Withdrawn,
                    VerificationState.ChangesRequested => ReferralActivityKind.ChangesRequested,
                    _ => null
                };
                if (kind.HasValue) activities.Add(new(row.Id, kind.Value, resolved,
                    row.Status == VerificationState.Withdrawn ? row.ApplicantId == me.Id : row.ReviewedBy == me.Id));
            }
            cards.Add(new(row.Id, row.ApplicantId, name, row.Status, response,
                referral.ReferrerId == me.Id && eligible && row.Status == VerificationState.UnderReview && referral.Status == ReferralState.AwaitingConfirmation,
                activities.ToArray()));
        }
        return cards;
    }

    public async Task<Ok> RespondToReferral(Guid id, VerificationResponse input)
    {
        var me = await User();
        var applicant = await db.VerificationRequests.Where(r => r.Id == id && r.Referrers.Any(x => x.ReferrerId == me.Id)).Select(r => (Guid?)r.ApplicantId).SingleOrDefaultAsync() ?? throw Missing();
        await using var tx = await db.Database.BeginTransactionAsync(); await PairLock(Pair(applicant, me.Id)); await UserLock(applicant);
        var row = await db.VerificationRequests.SingleAsync(r => r.Id == id);
        var referral = await db.VerificationReferrals.SingleAsync(r => r.RequestId == id && r.ReferrerId == me.Id);
        if (row.Status != VerificationState.UnderReview || !await Eligible(applicant, me.Id)
            || !await db.Profiles.Where(BasicProfile.Complete).AnyAsync(p => p.Id == applicant && p.IsVegan == true && !p.TestProfile)
            || !await db.Accounts.AnyAsync(a => a.Id == applicant && !a.Suspended))
            throw new ApiException(409, "This confirmation request is no longer available.");
        var response = input.Confirm ? ReferralState.Confirmed : ReferralState.Declined;
        if (referral.Status == response) return new Ok();
        if (referral.Status != ReferralState.AwaitingConfirmation) throw new ApiException(409, "You have already responded to this request.");
        referral.Status = response; referral.Responded = DateTimeOffset.UtcNow;
        var recipients = await Reviewers();
        foreach (var recipient in recipients)
            Notice(recipient, "referral-response", "A member responded to a vegan verification request.", "/moderation");
        Notice(applicant, "referral-response", input.Confirm ? "Your referrer confirmed you are vegan. A moderator will review your request." : "Your referrer declined to confirm your vegan status. Your request remains under review.", "/settings/profile#verification");
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await changes.Changed(ChangeArea.Profiles | ChangeArea.Notifications | ChangeArea.Messaging, recipients.Append(applicant).Append(me.Id).Distinct().ToArray());
        return new Ok();
    }
}
