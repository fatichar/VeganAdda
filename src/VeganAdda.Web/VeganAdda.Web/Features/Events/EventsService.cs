using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Referrals;
using VeganAdda.Web.Features.Events;
using static VeganAdda.Web.Features.Profiles.BirthDetails;

namespace VeganAdda.Web.Features.Events;

public sealed class EventsService(FeatureContext context, EventProjection projection) : FeatureService(context)
{
    public async Task<EventCard> GetEvent(Guid id)
    {
        await ActiveViewer();
        return await projection.One(await db.Events.SingleOrDefaultAsync(e => e.Id == id && e.Published) ?? throw Missing());
    }
    public async Task<Page<EventCard>> ListEvents(string city, int page, bool includePast = false, string country = "", string state = "")
    {
        var events = db.Events.Where(e => e.Published);
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
        foreach (var e in await events.OrderByDescending(e => e.Featured).ThenBy(e => e.Start).ThenBy(e => e.Id).Skip((page - 1) * 12).Take(12).ToListAsync()) result.Add(await projection.One(e));
        return new Page<EventCard>(result, total, page);
    }
    public async Task<Ok> EventAction(Guid id, string action, JsonElement? body)
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
    public async Task<List<EventInput>> MyEventSubmissions()
    {
        var me = await User();
        return await db.Events.Where(e => e.SubmittedBy == me.Id).OrderByDescending(e => e.SubmittedAt).Select(e => new EventInput
        {
            Id = e.Id, Title = e.Title, Cover = e.Cover, Description = e.Description, Start = e.Start, End = e.End, TimeZone = e.TimeZone,
            City = e.City, Venue = e.Venue, Organizer = e.Organizer, Registration = e.Registration, Published = e.Published,
            SubmissionStatus = e.PendingReview ? "Pending review" : e.Published ? "Approved" : e.ReviewNote != null ? "Changes requested" : "Draft", ReviewNote = e.ReviewNote
        }).ToListAsync();
    }
    public async Task<Ok> SaveEvent(EventInput input)
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
    public async Task<Ok> SaveEventSubmission(EventInput input)
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
    public async Task<Ok> ReviewEvent(EventReviewInput input, bool moderator = false)
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
    public async Task<List<EventReview>> EventQueue(bool includeTestEvents, int page = 1, int pageSize = 20)
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
}
