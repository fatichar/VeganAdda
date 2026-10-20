using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Services;

public partial class CommunityService
{
    private async Task<EventCard> EventDto(Gathering e)
    {
        var a = Viewer.HasValue ? await db.Attendance.FindAsync(Viewer.Value, e.Id) : null;
        var now = DateTimeOffset.UtcNow;
        var checkedInUntil = a?.Visible == true && a.CheckedInUntil > now && e.Start <= now && e.End > now
            ? (a.CheckedInUntil < e.End ? a.CheckedInUntil : e.End) : null;
        return new(e.Id, e.Title, e.Cover, e.Description, e.Start, e.End, e.TimeZone, e.City, e.Venue, e.Organizer, e.Registration,
            e.Featured, Viewer.HasValue && await db.Saved.AnyAsync(s => s.UserId == Viewer && s.TargetId == e.Id && s.Kind == "event"), a != null, a?.Visible ?? false, e.Demo, checkedInUntil);
    }
    private async Task<object> EventQuery(Guid? id, string city, int page, bool includePast = false, string country = "", string state = "")
    {
        var events = db.Events.Where(e => e.Published);
        if (id.HasValue) return await EventDto(await events.SingleOrDefaultAsync(e => e.Id == id) ?? throw Missing());
        if (!includePast) events = events.Where(e => e.End >= DateTimeOffset.UtcNow);
        country = Text(country, 80); state = Text(state, 80);
        if (country != "" || state != "")
        {
            var cities = EventLocationCatalog.Cities(country, state);
            events = events.Where(e => cities.Contains(e.City.ToLower()));
        }
        city = Text(city, 80).ToLowerInvariant();
        if (city.Length > 0 && city != "all india") events = events.Where(e => e.City.ToLower() == city);
        var total = await events.CountAsync(); var result = new List<EventCard>();
        foreach (var e in await events.OrderByDescending(e => e.Featured).ThenBy(e => e.Start).ThenBy(e => e.Id).Skip((page - 1) * 12).Take(12).ToListAsync()) result.Add(await EventDto(e));
        return new Page<EventCard>(result, total, page);
    }
    private async Task<object> EventAction(Guid id, string action, JsonElement? body)
    {
        var me = await User(true);
        var gathering = await db.Events.SingleOrDefaultAsync(e => e.Id == id && e.Published) ?? throw Missing();
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(me.Id);
        if (action == "save")
        {
            var s = await db.Saved.FindAsync(me.Id, id, "event");
            if (s != null) db.Saved.Remove(s); else db.Saved.Add(new() { UserId = me.Id, TargetId = id, Kind = "event" });
        }
        else if (action == "attendance")
        {
            var input = Read<AttendanceInput>(body); var a = await db.Attendance.FindAsync(me.Id, id);
            if (!input.Attending && a != null) db.Attendance.Remove(a);
            if (input.Attending)
            {
                if (a == null) { a = new() { UserId = me.Id, EventId = id }; db.Attendance.Add(a); }
                a.Visible = input.Visible;
                if (!a.Visible) a.CheckedInUntil = null;
            }
        }
        else if (action == "check-in")
        {
            var input = Read<CheckInInput>(body);
            var a = await db.Attendance.FindAsync(me.Id, id);
            if (input.CheckedIn)
            {
                var now = DateTimeOffset.UtcNow;
                if (gathering.Start is not { } start || gathering.End is not { } end || now < start || now >= end)
                    throw new ApiException(400, "Check-in is available while the event is running.");
                if (a == null || !a.Visible) throw new ApiException(400, "Mark yourself as attending and choose to appear in the attendee list before checking in.");
                var expires = now.AddHours(4);
                a.CheckedInUntil = expires < end ? expires : end;
            }
            else if (a != null) a.CheckedInUntil = null;
        }
        else throw Missing();
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new Ok();
    }
    private async Task<object> Admin(string approval = "", string section = "Members", int page = 1)
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
        var verified = await VerifiedAccounts();
        var pendingProfiles = new List<ProfileReview>();
        foreach (var profile in profiles) pendingProfiles.Add(await ProfileReviewDto(profile, verified));
        var activePage = section == "Events" ? eventsPage : section == "Reports" ? reportsPage : membersPage;
        return new AdminData(events, reports, people, pendingProfiles, eventsTotal, reportsTotal, membersTotal, profilesTotal, activePage, pageSize);
    }
    private async Task<ProfileReview> AdminMemberProfile(Guid id)
    {
        await User(admin: true);
        var profile = await db.Profiles.FindAsync(id) ?? throw Missing();
        return await ProfileReviewDto(profile, await VerifiedAccounts());
    }
    private async Task<ModerationData> Moderation(string section, int page)
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
        var events = section == "Events" ? await EventQueue(includeTestEvents: false, page, pageSize) : [];
        return new ModerationData(profiles, events, profilesTotal, eventsTotal, page, pageSize);
    }
    private async Task<ProfileReview> ProfileReviewDto(Profile p, HashSet<Guid> verifiedAccounts)
    {
        var referral = await db.Referrals.FindAsync(p.Id);
        var referrer = referral == null ? null : await db.Accounts.FindAsync(referral.ReferrerId);
        var referrerCanRefer = referral != null && verifiedAccounts.Contains(referral.ReferrerId)
            && await db.Profiles.AnyAsync(p => p.Id == referral.ReferrerId && p.Approved && !p.TestProfile);
        return new(p.Id, p.Name, p.City, p.Bio, p.Interests, p.LookingFor, p.Photo, p.Instagram, p.LinkedIn, p.Website, p.Public, p.TestProfile,
            referral == null ? null : referrer?.Name ?? "Deleted member", referrerCanRefer, p.ReviewNote, p.Country, p.State, p.IsVegan, p.VeganVerificationPending);
    }
    private async Task<List<ProfileReview>> ProfileQueue(bool includeTestProfiles, int page = 1, int pageSize = 20)
    {
        if (includeTestProfiles) await User(admin: true); else await User(moderator: true);
        var verifiedAccounts = await VerifiedAccounts();
        var reviewRows = await db.Profiles.Where(p => !p.Approved && (includeTestProfiles || !p.TestProfile)).OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var profileReviews = new List<ProfileReview>();
        foreach (var p in reviewRows) profileReviews.Add(await ProfileReviewDto(p, verifiedAccounts));
        return profileReviews;
    }
    private async Task<List<EventReview>> EventQueue(bool includeTestEvents, int page = 1, int pageSize = 20)
    {
        await User(moderator: !includeTestEvents, admin: includeTestEvents);
        var rows = await db.Events.Where(e => e.PendingReview && (includeTestEvents || !e.Demo)).OrderBy(e => e.Start).ThenBy(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var result = new List<EventReview>();
        foreach (var e in rows)
        {
            var details = new EventInput { Id = e.Id, Title = e.Title, Cover = e.Cover, Description = e.Description, Start = e.Start, End = e.End, TimeZone = e.TimeZone, City = e.City, Venue = e.Venue, Organizer = e.Organizer, Registration = e.Registration, Published = false, SubmissionStatus = "Pending review", ReviewNote = e.ReviewNote };
            var submitter = e.SubmittedBy.HasValue ? await db.Accounts.Where(a => a.Id == e.SubmittedBy).Select(a => a.Name).SingleOrDefaultAsync() : null;
            result.Add(new EventReview(details, submitter));
        }
        return result;
    }
    private async Task<List<EventInput>> MyEventSubmissions()
    {
        var me = await User();
        return await db.Events.Where(e => e.SubmittedBy == me.Id).OrderByDescending(e => e.SubmittedAt).Select(e => new EventInput
        {
            Id = e.Id, Title = e.Title, Cover = e.Cover, Description = e.Description, Start = e.Start, End = e.End, TimeZone = e.TimeZone,
            City = e.City, Venue = e.Venue, Organizer = e.Organizer, Registration = e.Registration, Published = e.Published,
            SubmissionStatus = e.PendingReview ? "Pending review" : e.Published ? "Approved" : e.ReviewNote != null ? "Changes requested" : "Draft", ReviewNote = e.ReviewNote
        }).ToListAsync();
    }
    private async Task<object> SaveEvent(EventInput input)
    {
        var me = await User(admin: true);
        var title = Text(input.Title, 150, true); var description = Text(input.Description, 4000); var city = Text(input.City, 80);
        var venue = Text(input.Venue, 200); var organizer = Text(input.Organizer, 150); var registration = Url(input.Registration);
        var cover = Photo(input.Cover);
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone); } catch { throw new ApiException(400, "Choose a valid IANA time zone, such as Asia/Kolkata."); }
        if (input.Published && (input.Start == null || input.End == null || input.End <= input.Start || city == "" || venue == "" || organizer == "" || description == "")) throw new ApiException(400, "Published events need verified start/end times, city, venue, organizer and description.");
        var e = input.Id == Guid.Empty ? null : await db.Events.FindAsync(input.Id);
        if (input.Id != Guid.Empty && e == null) throw Missing();
        e ??= new(); if (db.Entry(e).State == EntityState.Detached) db.Events.Add(e);
        e.Title = title; e.Description = description; e.City = city; e.Venue = venue; e.Organizer = organizer; e.Registration = registration;
        e.Cover = cover; e.Start = input.Start?.ToUniversalTime(); e.End = input.End?.ToUniversalTime(); e.TimeZone = input.TimeZone; e.Featured = input.Featured; e.Published = input.Published;
        if (input.Published) { e.PendingReview = false; e.ReviewNote = null; e.ReviewedBy = me.Id; e.ReviewedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(); return new Ok();
    }
    private async Task<object> SaveEventSubmission(EventInput input)
    {
        var me = await User(profile: true);
        if (!await db.Profiles.AnyAsync(p => p.Id == me.Id && p.IsVegan == true && !p.VeganVerificationPending && p.Approved && !p.TestProfile)) throw new ApiException(403, "Only an approved vegan member can submit an event.");
        var title = Text(input.Title, 150, true); var description = Text(input.Description, 4000); var city = Text(input.City, 80, true);
        var venue = Text(input.Venue, 200, true); var organizer = Text(input.Organizer, 150, true); var registration = Url(input.Registration); var cover = Photo(input.Cover);
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone); } catch { throw new ApiException(400, "Choose a valid IANA time zone, such as Asia/Kolkata."); }
        if (input.Start == null || input.End == null || input.End <= input.Start) throw new ApiException(400, "Enter verified start and end times.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await UserLock(me.Id);
        var e = input.Id == Guid.Empty ? null : await db.Events.SingleOrDefaultAsync(x => x.Id == input.Id && x.SubmittedBy == me.Id);
        if (input.Id != Guid.Empty && e == null) throw Missing();
        if (e?.PendingReview == true) throw new ApiException(409, "This event is already waiting for review.");
        e ??= new Gathering { SubmittedBy = me.Id, SubmittedAt = DateTimeOffset.UtcNow };
        if (db.Entry(e).State == EntityState.Detached) db.Events.Add(e);
        e.Title = title; e.Description = description; e.City = city; e.Venue = venue; e.Organizer = organizer; e.Registration = registration;
        e.Cover = cover; e.Start = input.Start.Value.ToUniversalTime(); e.End = input.End.Value.ToUniversalTime(); e.TimeZone = input.TimeZone;
        e.Published = false; e.Featured = false; e.PendingReview = true; e.ReviewNote = null; e.ReviewedBy = null; e.ReviewedAt = null;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        var reviewers = await db.Accounts.Where(a => !a.Suspended && (a.Admin || a.Moderator)).Select(a => a.Id).ToArrayAsync();
        foreach (var reviewer in reviewers) Notice(reviewer, "event-submission", $"{me.Name} submitted an event for review.", "/moderation");
        if (reviewers.Length > 0) await db.SaveChangesAsync();
        await changes.Changed(reviewers);
        return new Ok();
    }
    private async Task<object> ReviewEvent(EventReviewInput input, bool moderator = false)
    {
        var reviewer = await User(admin: !moderator, moderator: moderator);
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(input.Id);
        var e = await db.Events.FindAsync(input.Id) ?? throw Missing();
        if (!e.PendingReview || (!reviewer.Admin && e.Demo)) throw new ApiException(409, "This event is no longer waiting for review.");
        e.PendingReview = false; e.Published = input.Approve;
        e.ReviewNote = input.Approve ? null : Text(input.Note, 500, true);
        e.ReviewedBy = reviewer.Id; e.ReviewedAt = DateTimeOffset.UtcNow;
        if (e.SubmittedBy.HasValue) Notice(e.SubmittedBy.Value, input.Approve ? "event-approved" : "event-review", input.Approve ? "Your event was approved and is now published." : "Your event needs an update before it can be published: " + e.ReviewNote, "/events/suggest");
        await db.SaveChangesAsync(); await tx.CommitAsync();
        if (e.SubmittedBy.HasValue) await changes.Changed(e.SubmittedBy.Value);
        return new Ok();
    }
    private async Task<object> Moderate(ModerateInput input)
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
                    var children = await db.Referrals.Select(r => new { r.ReferrerId, r.ReferredId }).ToListAsync();
                    var byReferrer = children.GroupBy(r => r.ReferrerId).ToDictionary(g => g.Key, g => g.Select(r => r.ReferredId).ToArray());
                    var queue = new Queue<Guid>(); queue.Enqueue(input.Id);
                    while (queue.TryDequeue(out var parentId))
                        if (byReferrer.TryGetValue(parentId, out var descendants)) foreach (var child in descendants) if (affected.Add(child)) queue.Enqueue(child);
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
    private async Task<object> ReviewProfile(ProfileReviewInput input, bool moderator = false)
    {
        var reviewer = await User(admin: !moderator, moderator: moderator);
        await using var tx = await db.Database.BeginTransactionAsync();
        await UserLock(input.Id);
        var profile = await db.Profiles.FindAsync(input.Id) ?? throw Missing();
        if (!reviewer.Admin && profile.TestProfile) throw Denied();
        profile.Approved = input.Approve;
        profile.ReviewNote = input.Approve ? null : CommunityService.Text(input.Note, 500, true);
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
        await changes.Changed(input.Id);
        return new Ok();
    }
    private async Task<object> DeleteAccount()
    {
        var me = await User();
        if (me.Admin) throw new ApiException(409, "Transfer administrator access before deleting this account.");
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(me.Id);
        var peers = await db.Relationships.Where(r => r.A == me.Id || r.B == me.Id).Select(r => r.A == me.Id ? r.B : r.A).ToArrayAsync();
        await db.Saved.Where(s => s.TargetId == me.Id && s.Kind == "person").ExecuteDeleteAsync();
        // Cascade removes all conversations and both participants' messages in those conversations.
        db.Accounts.Remove(me); await db.SaveChangesAsync(); await tx.CommitAsync();
        var media = config["Storage:MediaPath"] ?? "uploads";
        if (Directory.Exists(media)) foreach (var marker in Directory.EnumerateFiles(media, "*.owner-" + me.Id.ToString("N")))
        { File.Delete(marker[..marker.LastIndexOf(".owner-", StringComparison.Ordinal)]); File.Delete(marker); }
        await changes.Changed(peers); return new Ok();
    }
}
