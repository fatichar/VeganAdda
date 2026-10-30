using Microsoft.EntityFrameworkCore;
using VeganAdda.Web.Features.Verification;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Referrals;
using VeganAdda.Web.Features.Events;
using VeganAdda.Web.Observability;
using VeganAdda.Web.Services;

namespace VeganAdda.Web.Features.Moderation;

public sealed class ModerationService(FeatureContext context, ReferralTrust trust, EventsService eventService, LifecycleEvents lifecycle, VerificationService verification, IFirebaseUserEmailLookup emailLookup, IProfileReminderEmailSender emailSender, ILogger<ModerationService> logger) : FeatureService(context)
{
    private IQueryable<Profile> ReviewableProfiles() => db.Profiles.Where(BasicProfile.Complete)
        .Where(p => p.IsVegan == true && !p.VeganVerified && db.VerificationRequests.Any(r => r.ApplicantId == p.Id && r.Status == VerificationState.UnderReview) && db.Accounts.Any(a => a.Id == p.Id && !a.Suspended));

    public async Task<AdminData> Admin(string verification = "", string section = "Members", int page = 1, string country = "", string state = "", string city = "")
    {
        await User(admin: true);
        const int pageSize = 20;
        if (section is not ("Events" or "Members" or "Reports")) throw new ApiException(400, "Choose a supported administration section.");
        var eventsQuery = EventLocationCatalog.Filter(db.Events, Text(country, 80), Text(state, 80), Text(city, 80));
        var eventsTotal = await eventsQuery.CountAsync();
        var eventsPage = section == "Events" ? Math.Min(page, Math.Max(1, (eventsTotal + pageSize - 1) / pageSize)) : 1;
        var events = section == "Events" ? await eventsQuery.OrderBy(e => e.Start).ThenBy(e => e.Id).Skip((eventsPage - 1) * pageSize).Take(pageSize).Select(e => new EventInput { Id = e.Id, Title = e.Title, Cover = e.Cover, Description = e.Description, Start = e.Start, End = e.End, TimeZone = e.TimeZone, Country = e.Country, State = e.State, City = e.City, Venue = e.Venue, Organizer = e.Organizer, Registration = e.Registration, Published = e.Published, Featured = e.Featured, SubmissionStatus = e.PendingReview ? "Pending review" : e.Published ? "Approved" : e.ReviewNote != null ? "Changes requested" : "Draft", ReviewNote = e.ReviewNote }).ToListAsync() : [];
        var reportsQuery = db.Reports.AsQueryable();
        var reportsTotal = await reportsQuery.CountAsync();
        var reportsPage = section == "Reports" ? Math.Min(page, Math.Max(1, (reportsTotal + pageSize - 1) / pageSize)) : 1;
        var reports = section == "Reports" ? await reportsQuery.OrderByDescending(r => r.Created).ThenByDescending(r => r.Id).Skip((reportsPage - 1) * pageSize).Take(pageSize).Select(r => new ReportView(r.Id, r.ReporterId == null ? "Deleted account" : "Member", r.SubjectId ?? Guid.Empty, r.SubjectId == null ? "Deleted account" : db.Accounts.Where(a => a.Id == r.SubjectId).Select(a => a.Name).FirstOrDefault() ?? "Unavailable", r.Reason, r.Resolved, r.Created, r.Details, r.Screenshot)).ToListAsync() : [];
        var members = db.Accounts.AsQueryable();
        var reviewableProfiles = ReviewableProfiles();
        members = verification switch
        {
            "" or "all" => members,
            "verified" => members.Where(a => db.Profiles.Any(p => p.Id == a.Id && p.VeganVerified)),
            "pending" => members.Where(a => reviewableProfiles.Any(p => p.Id == a.Id)),
            "unverified" => members.Where(a => db.Profiles.Any(p => p.Id == a.Id && !p.VeganVerified)),
            "no-profile" => members.Where(a => !db.Profiles.Any(p => p.Id == a.Id)),
            _ => throw new ApiException(400, "Choose a supported vegan verification filter.")
        };
        var membersTotal = await members.CountAsync();
        var membersPage = section == "Members" ? Math.Min(page, Math.Max(1, (membersTotal + pageSize - 1) / pageSize)) : 1;
        var accounts = section == "Members" ? await members.OrderBy(a => a.Name).ThenBy(a => a.Id).Skip((membersPage - 1) * pageSize).Take(pageSize).ToListAsync() : [];
        var people = new List<AdminPerson>();
        foreach (var a in accounts)
        {
            var profile = await db.Profiles.FindAsync(a.Id);
            people.Add(new(a.Id, a.Name, a.Suspended, profile?.Public ?? false, profile?.Featured ?? false, profile?.VeganVerified ?? false, profile?.TestProfile ?? false, a.Moderator, profile?.IsVegan, profile != null, a.Admin, BasicProfile.IsComplete(profile)));
        }
        var profilesTotal = await ReviewableProfiles().CountAsync();
        var profiles = await ReviewableProfiles().OrderBy(p => p.Name).ThenBy(p => p.Id).Take(pageSize).ToListAsync();
        var pendingProfiles = new List<ProfileReview>();
        foreach (var profile in profiles) pendingProfiles.Add(await ProfileReviewDto(profile));
        var activePage = section == "Events" ? eventsPage : section == "Reports" ? reportsPage : membersPage;
        return new AdminData(events, reports, people, pendingProfiles, eventsTotal, reportsTotal, membersTotal, profilesTotal, activePage, pageSize, emailSender.IsConfigured);
    }
    public async Task<BulkMemberActionResult> BulkMemberAction(BulkMemberActionInput input, CancellationToken cancellationToken = default)
    {
        await User(admin: true);
        if (input.MemberIds is null || input.MemberIds.Count is < 1 or > 100)
            throw new ApiException(400, "Select between 1 and 100 members.");
        var ids = input.MemberIds.Distinct().ToArray();
        var duplicateCount = input.MemberIds.Count - ids.Length;
        if (input.Action == "profile-reminder")
        {
            if (!emailSender.IsConfigured) throw new ApiException(503, "Profile reminder email delivery is not configured.");
            var result = await SendProfileReminders(ids, cancellationToken);
            return new(input.Action, result.Sent, result.Skipped + duplicateCount, result.Failed);
        }
        if (input.Action != "verify-vegan") throw new ApiException(400, "Choose an available bulk member action.");

        var verified = 0;
        var skipped = duplicateCount;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var account = await db.Accounts.FindAsync([id], cancellationToken);
            var profile = await db.Profiles.FindAsync([id], cancellationToken);
            if (account == null || account.Suspended || profile == null || !BasicProfile.IsComplete(profile) || profile.IsVegan != true || profile.VeganVerified)
            {
                skipped++;
                continue;
            }
            try
            {
                await ReviewProfile(new VeganVerificationInput(id, true));
                verified++;
            }
            catch (ApiException ex) when (ex.Status is 404 or 409)
            {
                skipped++;
            }
        }
        return new(input.Action, verified, skipped, 0);
    }

    private async Task<(int Sent, int Skipped, int Failed)> SendProfileReminders(IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken)
    {
        if (!emailSender.IsConfigured) throw new ApiException(503, "Profile reminder email delivery is not configured.");
        var ids = memberIds.Distinct().ToArray();
        var accounts = await db.Accounts
            .Where(a => ids.Contains(a.Id) && !a.Suspended && !db.Profiles.Where(BasicProfile.Complete).Any(p => p.Id == a.Id))
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken);
        var skipped = ids.Length - accounts.Count;
        var sent = 0;
        var failed = 0;
        var subjectPrefix = $"firebase:{config["Authentication:Firebase:ProjectId"]}:";
        foreach (var account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!account.Subject.StartsWith(subjectPrefix, StringComparison.Ordinal) || account.Subject.Length == subjectPrefix.Length)
            {
                skipped++;
                continue;
            }
            try
            {
                var email = await emailLookup.GetVerifiedEmail(account.Subject[subjectPrefix.Length..]);
                if (string.IsNullOrWhiteSpace(email))
                {
                    skipped++;
                    continue;
                }
                await emailSender.Send(email, account.Name, cancellationToken);
                sent++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning("Profile reminder email failed for account {AccountId}; error type {ErrorType}.", account.Id, ex.GetType().Name);
            }
        }
        return (sent, skipped, failed);
    }
    public async Task<ProfileReview> AdminMemberProfile(Guid id)
    {
        await User(admin: true);
        var profile = await db.Profiles.FindAsync(id) ?? throw Missing();
        return await ProfileReviewDto(profile);
    }
    public async Task<ModerationData> Moderation(string section, int page)
    {
        await User(moderator: true);
        const int pageSize = 20;
        if (section is not ("Profiles" or "Events")) throw new ApiException(400, "Choose a supported review section.");
        var profilesQuery = ReviewableProfiles().Where(p => !p.TestProfile);
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
        var referrerCanRefer = referral != null && await trust.CanCreateReferral(referral.ReferrerId);
        return new(p.Id, p.Name, p.City, p.Bio, p.Interests, p.LookingFor, p.Photo, p.Instagram, p.LinkedIn, p.Website, p.Public, p.TestProfile,
            referral == null ? null : referrer?.Name ?? "Deleted member", referrerCanRefer, p.ReviewNote, p.Country, p.State, p.IsVegan, p.VeganVerified, p.Profession, await verification.Details(p.Id));
    }
    private async Task<List<ProfileReview>> ProfileQueue(bool includeTestProfiles, int page = 1, int pageSize = 20)
    {
        if (includeTestProfiles) await User(admin: true); else await User(moderator: true);
        var reviewRows = await ReviewableProfiles().Where(p => includeTestProfiles || !p.TestProfile).OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var profileReviews = new List<ProfileReview>();
        foreach (var p in reviewRows) profileReviews.Add(await ProfileReviewDto(p));
        return profileReviews;
    }
    public async Task<Ok> Moderate(ModerateInput input)
    {
        var me = await User(admin: true);
        if (input.Action == "confirm-vegan") return await ReviewProfile(new(input.Id, true));
        if (input.Action == "remove-vegan-verification") return await ReviewProfile(new(input.Id, false, "Vegan verification removed by an administrator."));
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
                if (input.Action == "grant-moderator" && !await db.Profiles.Where(BasicProfile.Complete).AnyAsync(p => p.Id == u.Id && p.IsVegan == true && p.VeganVerified && !p.TestProfile && !u.Suspended)) throw new ApiException(409, "Only a verified vegan member can become a moderator.");
                u.Moderator = input.Action == "grant-moderator";
            }
            else if (input.Action is "feature" or "unfeature")
            {
                var p = await db.Profiles.FindAsync(u.Id) ?? throw Missing();
                if (input.Action == "feature" && (!p.Public || p.TestProfile || u.Suspended)) throw new ApiException(400, "Only active public non-test profiles can be featured.");
                p.Featured = input.Action == "feature";
            }
            else if (input.Action is "test-profile" or "normal-profile")
            {
                var p = await db.Profiles.FindAsync(u.Id) ?? throw Missing();
                p.TestProfile = input.Action == "test-profile";
                if (p.TestProfile) { p.Featured = false; p.VeganVerified = false; u.Moderator = false; }
            }
            else throw Missing();
        }
        await db.SaveChangesAsync(); affected.UnionWith(await db.Accounts.Where(a => a.Admin && !a.Suspended).Select(a => a.Id).ToListAsync()); await changes.Changed(affected.ToArray()); return new Ok();
    }
    public async Task<Ok> ReviewProfile(VeganVerificationInput input, bool moderator = false)
    {
        var reviewer = await User(admin: !moderator, moderator: moderator);
        await using var tx = await db.Database.BeginTransactionAsync();
        await UserLock(input.Id);
        var profile = await db.Profiles.FindAsync(input.Id) ?? throw Missing();
        if (!reviewer.Admin && profile.TestProfile) throw Denied();
        var account = await db.Accounts.FindAsync(profile.Id) ?? throw Missing();
        if (input.Confirm && (profile.IsVegan != true || account.Suspended || !BasicProfile.IsComplete(profile)))
            throw new ApiException(409, "Only an active member with a complete profile and vegan status can be confirmed.");
        var request = await db.VerificationRequests.SingleOrDefaultAsync(r => r.ApplicantId == profile.Id && r.Status == VerificationState.UnderReview);
        if ((!reviewer.Admin || input.RequestId.HasValue) && (request == null || request.Id != input.RequestId))
            throw new ApiException(409, "This request has changed. Refresh the review queue.");
        if (!input.Confirm && !profile.VeganVerified && request == null)
            throw new ApiException(409, "There is no active verification request to review.");
        if (request != null)
        {
            request.Status = input.Confirm ? VerificationState.Verified : VerificationState.ChangesRequested;
            request.Feedback = input.Confirm ? null : Text(input.Note, 500, true);
            request.Resolved = DateTimeOffset.UtcNow; request.ReviewedBy = reviewer.Id;
        }
        var wasVerified = profile.VeganVerified;
        profile.VeganVerified = input.Confirm;
        profile.ReviewNote = input.Confirm ? null : Text(input.Note, 500, true);
        profile.ReviewedBy = reviewer.Id; profile.ReviewedAt = DateTimeOffset.UtcNow;
        if (!input.Confirm) account.Moderator = false;
        Notice(profile.Id, input.Confirm ? "vegan-confirmed" : "vegan-verification-removed",
            input.Confirm ? "Your vegan status has been verified." : "Your vegan status is unverified: " + profile.ReviewNote, "/settings/profile");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        lifecycle.Record(input.Confirm ? LifecycleEvent.VeganConfirmed : wasVerified ? LifecycleEvent.VeganVerificationRemoved : LifecycleEvent.VeganVerificationFeedback, profile.Id, reviewer.Id);
        var recipients = await verification.Reviewers(profile.TestProfile);
        if (request != null) recipients.AddRange(await db.VerificationReferrals.Where(r => r.RequestId == request.Id && r.ReferrerId != null).Select(r => r.ReferrerId!.Value).ToListAsync());
        await changes.Changed(ChangeArea.Profiles | ChangeArea.Notifications | ChangeArea.Messaging, recipients.Append(input.Id).Distinct().ToArray());
        return new Ok();
    }
}
