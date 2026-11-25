using VeganAdda.Web.Services;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Shared;

using VeganAdda.Web.Observability;

namespace VeganAdda.Web.Features.Events;

public sealed class EventsService(FeatureContext context, EventProjection projection, ProfileVisibility visibility, LifecycleEvents lifecycle, ActivityRecorder activity) : FeatureService(context)
{
    public async Task<EventCard> GetEvent(Guid id)
    {
        await ActiveViewer();
        var gathering = await db.Events.SingleOrDefaultAsync(e => e.Id == id && e.Published) ?? throw Missing();
        var card = await projection.One(gathering);
        if (!Viewer.HasValue) return card;
        var people = gathering.ContactId.HasValue || gathering.SubmittedBy.HasValue
            ? await VisibleEventMembers().Where(p => p.Id == gathering.ContactId || p.Id == gathering.SubmittedBy)
                .Select(p => new EventContact(p.Id, p.Name)).ToDictionaryAsync(p => p.Id)
            : [];
        var contact = gathering.ContactId is { } contactId ? people.GetValueOrDefault(contactId) : null;
        var creator = gathering.SubmittedBy is { } creatorId ? people.GetValueOrDefault(creatorId) : null;
        return card with
        {
            Contact = contact,
            ContactPhone = gathering.ContactId == null || contact != null ? gathering.ContactPhone : null,
            Creator = creator
        };
    }
    public async Task<Page<EventCard>> ListEvents(string city, int page, bool includePast = false, string country = "", string state = "")
    {
        await ActiveViewer();
        var events = db.Events.Where(e => e.Published);
        if (!includePast) events = Upcoming(events);
        events = EventLocationCatalog.Filter(events, Text(country, 80), Text(state, 80), Text(city, 80));
        var total = await events.CountAsync();
        var rows = await events.OrderByDescending(e => e.Featured).ThenBy(e => e.Start).ThenBy(e => e.Id).Skip((page - 1) * 12).Take(12).ToListAsync();
        return new Page<EventCard>(await projection.Many(rows), total, page);
    }

    private static IQueryable<Gathering> Upcoming(IQueryable<Gathering> events)
    {
        var now = DateTimeOffset.UtcNow;
        return events.Where(e => (e.End ?? e.Start!.Value.AddHours(4)) >= now);
    }

    public async Task<Page<ScheduledEvent>> GetSchedule(string country, string state, string city, int page)
    {
        await ActiveViewer();
        const int pageSize = 50;
        var viewer = Viewer;
        var events = EventLocationCatalog.Filter(Upcoming(db.Events.Where(e => e.Published && e.Start.HasValue)),
            Text(country, 80), Text(state, 80), Text(city, 80));
        var total = await events.CountAsync();
        var rows = await events.OrderBy(e => e.Start).ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new ScheduledEvent(e.Id, e.Title, e.Start!.Value, e.TimeZone, e.City, e.Venue,
                viewer.HasValue && db.Attendance.Any(a => a.EventId == e.Id && a.UserId == viewer), e.Demo))
            .ToListAsync();
        return new Page<ScheduledEvent>(rows, total, page, pageSize);
    }

    private IQueryable<Profile> VisibleEventMembers() => visibility.Query().Where(p => !p.TestProfile);

    public async Task<Page<ProfileEvent>> GetProfileEvents(Guid personId, int page)
    {
        var viewer = await User();
        if (!await visibility.Query(true).AnyAsync(p => p.Id == personId)) throw Missing();
        const int pageSize = 12;
        var upcoming = Upcoming(db.Events.Where(e => e.Published && e.Start.HasValue));
        var events = from attendance in db.Attendance
                     where attendance.UserId == personId && (viewer.Id == personId || attendance.Visible)
                     join gathering in upcoming on attendance.EventId equals gathering.Id
                     orderby gathering.Start, gathering.Id
                     select new ProfileEvent(gathering.Id, gathering.Title, gathering.Start!.Value,
                         gathering.TimeZone, gathering.City, attendance.Visible);
        var total = await events.CountAsync();
        var rows = await events.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new(rows, total, page, pageSize);
    }

    private IQueryable<Profile> EventPosters() => db.Profiles.Where(p => p.IsVegan == true && p.VeganVerified && !p.TestProfile)
        .Where(p => db.Accounts.Any(a => a.Id == p.Id && !a.Suspended));

    public async Task<List<EventContact>> SearchEventContacts(string search)
    {
        var me = await User();
        if (!await EventPosters().AnyAsync(p => p.Id == me.Id)) throw Denied();
        var term = Text(search, 80).ToLowerInvariant();
        if (term.Length < 2) return [];
        return await VisibleEventMembers().Where(p => p.Name.ToLower().Contains(term))
            .OrderBy(p => p.Name).ThenBy(p => p.Id).Take(10)
            .Select(p => new EventContact(p.Id, p.Name)).ToListAsync();
    }
    public async Task<Ok> EventAction(Guid id, string action, JsonElement? body)
    {
        var me = await User();
        var gathering = await db.Events.SingleOrDefaultAsync(e => e.Id == id && e.Published) ?? throw Missing();
        var joined = false;
        await using var tx = await db.Database.BeginTransactionAsync(); await UserLock(me.Id);
        if (action == "save")
        {
            var s = await db.Saved.FindAsync(me.Id, id, "event");
            if (s != null) db.Saved.Remove(s); else db.Saved.Add(new() { UserId = me.Id, TargetId = id, Kind = "event" });
        }
        else if (action == "attendance")
        {
            var input = Read<AttendanceInput>(body); var a = await db.Attendance.FindAsync(me.Id, id);
            joined = input.Attending && a == null;
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
                if (gathering.Start is not { } start || (gathering.End ?? gathering.Start?.AddHours(4)) is not { } end || now < start || now >= end)
                    throw new ApiException(400, "Check-in is available while the event is running.");
                if (a == null || !a.Visible) throw new ApiException(400, "Mark yourself as attending and choose to appear in the attendee list before checking in.");
                var expires = now.AddHours(4);
                a.CheckedInUntil = expires < end ? expires : end;
            }
            else if (a != null) a.CheckedInUntil = null;
        }
        else throw Missing();
        await db.SaveChangesAsync(); await tx.CommitAsync();
        if (joined) await activity.Count(ActivityKind.EventJoined, me.Id);
        return new Ok();
    }
    public async Task<List<EventInput>> MyEventSubmissions()
    {
        var me = await User();
        var contacts = VisibleEventMembers();
        return await db.Events.Where(e => e.SubmittedBy == me.Id).OrderByDescending(e => e.SubmittedAt).Select(e => new EventInput
        {
            Id = e.Id, Title = e.Title, Cover = e.Cover, Description = e.Description, Start = e.Start, End = e.End, TimeZone = e.TimeZone,
            Country = e.Country, State = e.State, City = e.City, Venue = e.Venue, Organizer = e.Organizer, Registration = e.Registration, Published = e.Published,
            Contact = contacts.Where(p => p.Id == e.ContactId).Select(p => new EventContact(p.Id, p.Name)).FirstOrDefault(),
            ContactPhone = e.ContactPhone,
            SubmissionStatus = e.PendingReview ? "Pending review" : e.Published ? "Published" : e.ReviewNote != null ? "Changes requested" : e.ReviewedAt != null ? "Unpublished" : "Draft",
            ReviewNote = e.ReviewNote
        }).ToListAsync();
    }
    public async Task<Ok> SaveEvent(EventInput input)
    {
        var me = await User(admin: true);
        var title = Text(input.Title, 150, true); var description = Text(input.Description, 4000); var city = Text(input.City, 80, true); var country = Text(input.Country, 80, true); var state = Text(input.State, 80, true);
        var venue = Text(input.Venue, 200, true); var organizer = Text(input.Organizer, 150); var registration = Url(input.Registration);
        var cover = Photo(input.Cover);
        var times = ResolveTimes(input, country, state, city);
        await using var tx = await db.Database.BeginTransactionAsync();
        if (input.Id != Guid.Empty) await EventLock(input.Id);
        var e = input.Id == Guid.Empty ? null : await db.Events.FindAsync(input.Id);
        if (input.Id != Guid.Empty && e == null) throw Missing();
        var created = e == null; var wasPublished = e?.Published == true;
        var previousPhone = e?.ContactPhone;
        var phone = EventPhone(input.ContactPhone ?? previousPhone, input.Published && (!wasPublished || previousPhone != null));
        e ??= new() { SubmittedBy = me.Id, SubmittedAt = DateTimeOffset.UtcNow };
        if (db.Entry(e).State == EntityState.Detached) db.Events.Add(e);
        e.Title = title; e.Description = description; e.Country = country; e.State = state; e.City = city; e.Venue = venue; e.Organizer = organizer; e.Registration = registration;
        e.Cover = cover; e.Start = times.Start; e.End = times.End; e.TimeZone = times.TimeZone; e.Featured = input.Featured; e.Published = input.Published;
        e.ContactPhone = phone;
        if (input.Published) { e.PendingReview = false; e.ReviewNote = null; e.ReviewedBy = me.Id; e.ReviewedAt = DateTimeOffset.UtcNow; }
        else if (wasPublished) { e.ReviewedBy = me.Id; e.ReviewedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        lifecycle.RecordSubject(created ? LifecycleEvent.EventCreated : LifecycleEvent.EventUpdated, "event", e.Id, me.Id);
        if (e.Published != wasPublished) lifecycle.RecordSubject(e.Published ? LifecycleEvent.EventPublished : LifecycleEvent.EventUnpublished, "event", e.Id, me.Id);
        return new Ok();
    }
    public async Task<Ok> SaveEventSubmission(EventInput input)
    {
        var me = await User();
        var title = Text(input.Title, 150, true); var description = Text(input.Description, 4000); var city = Text(input.City, 80, true); var country = Text(input.Country, 80, true); var state = Text(input.State, 80, true);
        var venue = Text(input.Venue, 200, true); var organizer = Text(input.Organizer, 150); var registration = Url(input.Registration); var cover = Photo(input.Cover);
        var times = ResolveTimes(input, country, state, city);
        var contactId = input.Contact?.Id;
        var phone = EventPhone(input.ContactPhone, required: true);
        await using var tx = await db.Database.BeginTransactionAsync();
        var members = contactId is { } contactMember && contactMember != me.Id ? new[] { me.Id, contactMember } : [me.Id];
        foreach (var member in members.Order()) await UserLock(member);
        if (!await EventPosters().AnyAsync(p => p.Id == me.Id)) throw new ApiException(403, "Only a verified vegan member can post an event.");
        if (input.Id != Guid.Empty) await EventLock(input.Id);
        var e = input.Id == Guid.Empty ? null : await db.Events.SingleOrDefaultAsync(x => x.Id == input.Id && x.SubmittedBy == me.Id);
        if (input.Id != Guid.Empty && e == null) throw Missing();
        if (e is { Published: false, ReviewedAt: not null }) throw new ApiException(409, "Contact the community team to republish this event.");
        if (contactId.HasValue && !await VisibleEventMembers().AnyAsync(p => p.Id == contactId))
            throw new ApiException(400, "Choose an available VeganAdda member as the contact.");
        var created = e == null;
        var wasPublished = e?.Published == true;
        e ??= new Gathering { SubmittedBy = me.Id, SubmittedAt = DateTimeOffset.UtcNow };
        if (db.Entry(e).State == EntityState.Detached) db.Events.Add(e);
        e.Title = title; e.Description = description; e.Country = country; e.State = state; e.City = city; e.Venue = venue; e.Organizer = organizer; e.Registration = registration;
        e.Cover = cover; e.Start = times.Start; e.End = times.End; e.TimeZone = times.TimeZone;
        e.ContactId = contactId;
        e.ContactPhone = phone;
        e.Published = true; e.PendingReview = false; e.ReviewNote = null;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        lifecycle.RecordSubject(created ? LifecycleEvent.EventCreated : LifecycleEvent.EventUpdated, "event", e.Id, me.Id);
        if (!wasPublished) lifecycle.RecordSubject(LifecycleEvent.EventPublished, "event", e.Id, me.Id);
        return new Ok();
    }
    private Task EventLock(Guid id) => PairLock("event:" + id);

    private static string? EventPhone(string? input, bool required)
    {
        var phone = input?.Trim() ?? "";
        if (phone.Length == 0 && !required) return null;
        var digits = string.Concat(phone.Where(char.IsAsciiDigit));
        var number = phone.StartsWith('+') ? phone[1..] : phone;
        if (phone.Length > 32 || digits.Length is < 7 or > 15
            || number.Any(c => !char.IsAsciiDigit(c) && c is not (' ' or '-' or '(' or ')' or '.')))
            throw new ApiException(400, "Enter a valid event contact phone number with 7 to 15 digits.");
        return phone.StartsWith('+') ? "+" + digits : digits;
    }

    private sealed record EventTimes(string TimeZone, DateTimeOffset Start, DateTimeOffset? End);

    private static EventTimes ResolveTimes(EventInput input, string country, string state, string city)
    {
        if (input.LocalStart == null) throw new ApiException(400, "Enter the start date and time.");
        if (input.LocalEnd.HasValue && input.LocalEnd <= input.LocalStart) throw new ApiException(400, "End date and time must be after the start.");
        var zoneId = EventTimeZoneCatalog.Resolve(country, state, city);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        try
        {
            var start = ToUtc(input.LocalStart.Value, zone);
            DateTimeOffset? end = input.LocalEnd.HasValue ? ToUtc(input.LocalEnd.Value, zone) : null;
            return new EventTimes(zoneId, start, end);
        }
        catch (ArgumentException)
        {
            throw new ApiException(400, "This local time doesn't exist because clocks change at the event venue. Choose another time.");
        }
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var wallTime = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wallTime)) throw new ArgumentException("The local time falls in a daylight-saving gap.");
        // ConvertTimeToUtc selects standard time for the repeated hour during a daylight-saving overlap.
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wallTime, zone));
    }
    public async Task<Ok> ReviewEvent(EventReviewInput input, bool moderator = false)
    {
        var reviewer = await User(admin: !moderator, moderator: moderator);
        await using var tx = await db.Database.BeginTransactionAsync(); await EventLock(input.Id);
        var e = await db.Events.FindAsync(input.Id) ?? throw Missing();
        if (!e.PendingReview || (!reviewer.Admin && e.Demo)) throw new ApiException(409, "This event is no longer waiting for review.");
        e.PendingReview = false; e.Published = input.Approve;
        e.ReviewNote = input.Approve ? null : Text(input.Note, 500, true);
        e.ReviewedBy = reviewer.Id; e.ReviewedAt = DateTimeOffset.UtcNow;
        if (e.SubmittedBy.HasValue) Notice(e.SubmittedBy.Value, input.Approve ? "event-approved" : "event-review", input.Approve ? "Your event was approved and is now published." : "Your event was declined. Contact the community team about republishing it. " + e.ReviewNote, "/events/post");
        await db.SaveChangesAsync(); await tx.CommitAsync();
        lifecycle.RecordSubject(input.Approve ? LifecycleEvent.EventApproved : LifecycleEvent.EventRejected, "event", e.Id, reviewer.Id);
        var reviewers = await db.Accounts.Where(a => a.Admin && !a.Suspended).Select(a => a.Id).ToListAsync();
        if (e.SubmittedBy.HasValue) reviewers.Add(e.SubmittedBy.Value);
        await changes.Changed(reviewers.Distinct().ToArray());
        return new Ok();
    }
    public async Task<List<EventReview>> EventQueue(bool includeTestEvents, int page = 1, int pageSize = 20)
    {
        await User(moderator: !includeTestEvents, admin: includeTestEvents);
        var rows = await db.Events.Where(e => e.PendingReview && (includeTestEvents || !e.Demo)).OrderBy(e => e.Start).ThenBy(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var result = new List<EventReview>();
        foreach (var e in rows)
        {
            var details = new EventInput { Id = e.Id, Title = e.Title, Cover = e.Cover, Description = e.Description, Start = e.Start, End = e.End, TimeZone = e.TimeZone, Country = e.Country, State = e.State, City = e.City, Venue = e.Venue, Organizer = e.Organizer, Registration = e.Registration, Published = false, SubmissionStatus = "Pending review", ReviewNote = e.ReviewNote };
            var submitter = e.SubmittedBy.HasValue ? await db.Accounts.Where(a => a.Id == e.SubmittedBy).Select(a => a.Name).SingleOrDefaultAsync() : null;
            result.Add(new EventReview(details, submitter));
        }
        return result;
    }
}
