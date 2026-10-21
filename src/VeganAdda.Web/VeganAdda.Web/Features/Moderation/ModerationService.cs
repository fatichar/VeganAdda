using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Referrals;
using VeganAdda.Web.Features.Events;
using VeganAdda.Web.Observability;

namespace VeganAdda.Web.Features.Moderation;

public sealed class ModerationService(FeatureContext context, ReferralTrust trust, EventsService eventService, LifecycleEvents lifecycle) : FeatureService(context)
{
    public async Task<AdminData> Admin(string approval = "", string section = "Members", int page = 1)
    {
        await User(admin: true);
        const int pageSize = 20;
        if (section is not ("Events" or "Members" or "Reports")) throw new ApiException(400, "Choose a supported administration section.");
        var eventsQuery = db.Events.AsQueryable();
        var eventsTotal = await eventsQuery.CountAsync();
        var eventsPage = section == "Events" ? Math.Min(page, Math.Max(1, (eventsTotal + pageSize - 1) / pageSize)) : 1;
        var events = section == "Events" ? await eventsQuery.OrderBy(e => e.Start).ThenBy(e => e.Id).Skip((eventsPage - 1) * pageSize).Take(pageSize).Select(e => new EventInput { Id = e.Id, Title = e.Title, Cover = e.Cover, Description = e.Description, Start = e.Start, End = e.End, TimeZone = e.TimeZone, City = e.City, Venue = e.Venue, Organizer = e.Organizer, Registration = e.Registration, Published = e.Published, Featured = e.Featured, SubmissionStatus = e.PendingReview ? "Pending review" : e.Published ? "Approved" : e.ReviewNote != null ? "Changes requested" : "Draft", ReviewNote = e.ReviewNote }).ToListAsync() : [];
        var reportsQuery = db.Reports.AsQueryable();
        var reportsTotal = await reportsQuery.CountAsync();
        var reportsPage = section == "Reports" ? Math.Min(page, Math.Max(1, (reportsTotal + pageSize - 1) / pageSize)) : 1;
        var reports = section == "Reports" ? await reportsQuery.OrderByDescending(r => r.Created).ThenByDescending(r => r.Id).Skip((reportsPage - 1) * pageSize).Take(pageSize).Select(r => new ReportView(r.Id, r.ReporterId == null ? "Deleted account" : "Member", r.SubjectId ?? Guid.Empty, r.SubjectId == null ? "Deleted account" : db.Accounts.Where(a => a.Id == r.SubjectId).Select(a => a.Name).FirstOrDefault() ?? "Unavailable", r.Reason, r.Resolved, r.Created)).ToListAsync() : [];
        var members = db.Accounts.AsQueryable();
        members = approval switch
        {
            "" or "all" => members,
            "approved" => members.Where(a => db.Profiles.Any(p => p.Id == a.Id && p.Approved)),
            "awaiting-approval" => members.Where(a => db.Profiles.Any(p => p.Id == a.Id && !p.Approved)),
            "no-profile" => members.Where(a => !db.Profiles.Any(p => p.Id == a.Id)),
            _ => throw new ApiException(400, "Choose a supported approval filter.")
        };
        var membersTotal = await members.CountAsync();
        var membersPage = section == "Members" ? Math.Min(page, Math.Max(1, (membersTotal + pageSize - 1) / pageSize)) : 1;
        var accounts = section == "Members" ? await members.OrderBy(a => a.Name).ThenBy(a => a.Id).Skip((membersPage - 1) * pageSize).Take(pageSize).ToListAsync() : [];
        var people = new List<AdminPerson>();
        foreach (var a in accounts)
        {
            var profile = await db.Profiles.FindAsync(a.Id);
            people.Add(new(a.Id, a.Name, a.Suspended, profile?.Public ?? false, profile?.FeatureConsent ?? false, profile?.Featured ?? false, profile?.Approved ?? false, profile?.TestProfile ?? false, a.Moderator, profile?.IsVegan, profile != null, a.Admin));
        }
        var profilesTotal = await db.Profiles.CountAsync(p => !p.Approved);
        var profiles = await db.Profiles.Where(p => !p.Approved).OrderBy(p => p.Name).ThenBy(p => p.Id).Take(pageSize).ToListAsync();
        var pendingProfiles = new List<ProfileReview>();
        foreach (var profile in profiles) pendingProfiles.Add(await ProfileReviewDto(profile));
        var activePage = section == "Events" ? eventsPage : section == "Reports" ? reportsPage : membersPage;
        return new AdminData(events, reports, people, pendingProfiles, eventsTotal, reportsTotal, membersTotal, profilesTotal, activePage, pageSize);
    }
    public async Task<ProfileReview> AdminMemberProfile(Guid id)
    {
        await User(admin: true);
        var profile = await db.Profiles.FindAsync(id) ?? throw Missing();
        return await ProfileReviewDto(profile);
    }
    public async Task<ModerationData> Moderation(string section, int page)
    {
        const int pageSize = 20;
        if (section is not ("Profiles" or "Events")) throw new ApiException(400, "Choose a supported review section.");
        var profilesQuery = db.Profiles.Where(p => !p.Approved && !p.TestProfile);
        var eventsQuery = db.Events.Where(e => e.PendingReview && !e.Demo);
        var profilesTotal = await profilesQuery.CountAsync();
        var eventsTotal = await eventsQuery.CountAsync();
        var sectionTotal = section == "Profiles" ? profilesTotal : eventsTotal;
        page = Math.Min(page, Math.Max(1, (sectionTotal + pageSize - 1) / pageSize));
        var profiles = section == "Profiles" ? await ProfileQueue(includeTestProfiles: false, page, pageSize) : [];
        var events = section == "Events" ? await eventService.EventQueue(includeTestEvents: false, page, pageSize) : [];
        return new ModerationData(profiles, events, profilesTotal, eventsTotal, page, pageSize);
    }
    private async Task<ProfileReview> ProfileReviewDto(Profile p)
    {
        var referral = await db.Referrals.FindAsync(p.Id);
        var referrer = referral == null ? null : await db.Accounts.FindAsync(referral.ReferrerId);
        var referrerCanRefer = referral != null && await trust.IsVerified(referral.ReferrerId)
            && await db.Profiles.AnyAsync(p => p.Id == referral.ReferrerId && p.Approved && !p.TestProfile);
        return new(p.Id, p.Name, p.City, p.Bio, p.Interests, p.LookingFor, p.Photo, p.Instagram, p.LinkedIn, p.Website, p.Public, p.TestProfile,
            referral == null ? null : referrer?.Name ?? "Deleted member", referrerCanRefer, p.ReviewNote, p.Country, p.State, p.IsVegan, p.VeganVerificationPending);
    }
    private async Task<List<ProfileReview>> ProfileQueue(bool includeTestProfiles, int page = 1, int pageSize = 20)
    {
        if (includeTestProfiles) await User(admin: true); else await User(moderator: true);
        var reviewRows = await db.Profiles.Where(p => !p.Approved && (includeTestProfiles || !p.TestProfile)).OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var profileReviews = new List<ProfileReview>();
        foreach (var p in reviewRows) profileReviews.Add(await ProfileReviewDto(p));
        return profileReviews;
    }
    public async Task<Ok> Moderate(ModerateInput input)
    {
        var me = await User(admin: true);
        if (input.Action == "approve") return await ReviewProfile(new(input.Id, true));
        if (input.Action == "revoke-approval") return await ReviewProfile(new(input.Id, false, "Approval revoked by an administrator."));
        var affected = new HashSet<Guid> { input.Id };
        if (input.Action == "resolve")
        {
            var report = await db.Reports.FindAsync(input.Id) ?? throw Missing(); report.Resolved = true; report.ResolvedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            var u = await db.Accounts.FindAsync(input.Id) ?? throw Missing();
            if (u.Admin && input.Action == "suspend") throw new ApiException(400, "Administrator suspension requires an owner database review.");
            if (input.Action is "grant-admin" or "revoke-admin")
            {
                var appoint = input.Action == "grant-admin";
                if (appoint == u.Admin) throw new ApiException(409, appoint ? "This member is already an administrator." : "This member is not an administrator.");
                if (appoint && u.Suspended) throw new ApiException(409, "Restore this member's access before appointing them as an administrator.");
                if (!appoint && (u.Id == me.Id || await db.Accounts.CountAsync(a => a.Admin) <= 1)) throw new ApiException(409, "Another administrator must revoke this access.");
                u.Admin = appoint;
                u.Moderator = appoint ? false : u.Moderator;
                Notice(u.Id, appoint ? "admin-appointed" : "admin-revoked", appoint ? "You have been appointed as a VeganAdda administrator." : "Your VeganAdda administrator access has been revoked.", appoint ? "/admin" : "/");
            }
            else if (input.Action is "suspend" or "restore") u.Suspended = input.Action == "suspend";
            else if (input.Action is "grant-moderator" or "revoke-moderator")
            {
                if (u.Admin) throw new ApiException(409, "Administrators already have profile review access.");
                if (input.Action == "grant-moderator" && !await db.Profiles.AnyAsync(p => p.Id == u.Id && p.IsVegan == true && !p.VeganVerificationPending && p.Approved && !p.TestProfile)) throw new ApiException(409, "Only an approved vegan member can become a moderator.");
                u.Moderator = input.Action == "grant-moderator";
            }
            else if (input.Action is "verify" or "unverify")
            {
                if (input.Action == "verify" && (u.Suspended || !await db.Profiles.AnyAsync(p => p.Id == u.Id && p.Approved && !p.TestProfile))) throw new ApiException(409, "Only an active, approved, non-test profile can be verified.");
                u.Verified = input.Action == "verify";
                u.TrustRevoked = input.Action == "unverify";
                if (input.Action == "unverify")
                {
                    foreach (var descendant in await trust.Descendants(input.Id)) affected.Add(descendant);
                    var referredAccounts = await db.Accounts.Where(a => affected.Contains(a.Id) && a.Id != input.Id).ToListAsync();
                    foreach (var child in referredAccounts) { child.Verified = false; child.TrustRevoked = true; }
                }
            }
            else if (input.Action is "feature" or "unfeature")
            {
                var p = await db.Profiles.FindAsync(u.Id) ?? throw Missing();
                if (input.Action == "feature" && (!p.Public || !p.FeatureConsent || u.Suspended)) throw new ApiException(400, "Only consenting, active public profiles can be featured.");
                p.Featured = input.Action == "feature";
            }
            else if (input.Action is "test-profile" or "normal-profile")
            {
                var p = await db.Profiles.FindAsync(u.Id) ?? throw Missing();
                p.TestProfile = input.Action == "test-profile";
                if (p.TestProfile) { p.Featured = false; u.Verified = false; u.TrustRevoked = true; }
            }
            else throw Missing();
        }
        await db.SaveChangesAsync(); await changes.Changed(affected.ToArray()); return new Ok();
    }
    public async Task<Ok> ReviewProfile(ProfileReviewInput input, bool moderator = false)
    {
        var reviewer = await User(admin: !moderator, moderator: moderator);
        await using var tx = await db.Database.BeginTransactionAsync();
        await UserLock(input.Id);
        var profile = await db.Profiles.FindAsync(input.Id) ?? throw Missing();
        if (!reviewer.Admin && profile.TestProfile) throw Denied();
        var wasApproved = profile.Approved;
        profile.Approved = input.Approve;
        profile.ReviewNote = input.Approve ? null : Text(input.Note, 500, true);
        profile.ReviewedBy = reviewer.Id; profile.ReviewedAt = DateTimeOffset.UtcNow;
        if (input.Approve) profile.VeganVerificationPending = false;
        if (!input.Approve)
        {
            profile.Featured = false;
            var account = await db.Accounts.FindAsync(profile.Id) ?? throw Missing();
            account.Moderator = false;
        }
        Notice(profile.Id, input.Approve ? "profile-approved" : "profile-review", input.Approve ? "Your profile was approved and is ready to be discovered." : "Your profile needs an update before it can be approved: " + profile.ReviewNote, "/settings/profile");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        lifecycle.Record(input.Approve ? LifecycleEvent.ProfileApproved : wasApproved ? LifecycleEvent.ProfileApprovalRevoked : LifecycleEvent.ProfileChangesRequested, profile.Id, reviewer.Id);
        await changes.Changed(input.Id);
        return new Ok();
    }
}
