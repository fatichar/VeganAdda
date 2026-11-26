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
        var contacts = await ReadEventContacts([id]);
        return card with
        {
            CanEdit = gathering.SubmittedBy == Viewer,
            Creator = contacts.SingleOrDefault(c => c.IsCreator)?.Person,
            Contacts = contacts.Where(c => !c.IsCreator).Select(c => c.Person).ToArray()
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

    private sealed record VisibleContact(Guid EventId, EventContact Person, bool IsCreator);

    private Task<List<VisibleContact>> ReadEventContacts(Guid[] ids)
    {
        var eventMembers = db.EventContacts.Where(c => ids.Contains(c.EventId))
            .Select(c => new { c.EventId, c.MemberId, IsCreator = false })
            .Concat(db.Events.Where(e => ids.Contains(e.Id) && e.SubmittedBy.HasValue)
                .Select(e => new { EventId = e.Id, MemberId = e.SubmittedBy!.Value, IsCreator = true }));
        return (from member in eventMembers
                join profile in VisibleEventMembers() on member.MemberId equals profile.Id
                orderby profile.Name, profile.Id
                select new VisibleContact(member.EventId, new EventContact(profile.Id, profile.Name), member.IsCreator))
            .ToListAsync();
    }

    internal async Task PopulateEventContacts(List<EventInput> events)
    {
        if (events.Count == 0) return;
        var contacts = (await ReadEventContacts(events.Select(e => e.Id).ToArray())).ToLookup(c => c.EventId);
        foreach (var ev in events)
        {
            ev.Creator = contacts[ev.Id].SingleOrDefault(c => c.IsCreator)?.Person;
            ev.Contacts = contacts[ev.Id].Where(c => !c.IsCreator).Select(c => c.Person).ToArray();
        }
    }

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
        var me = await User(allowIncomplete: true);
        if (!me.Admin && !await EventPosters().Where(BasicProfile.Complete).AnyAsync(p => p.Id == me.Id)) throw Denied();
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
        var events = await db.Events.Where(e => e.SubmittedBy == me.Id).OrderByDescending(e => e.SubmittedAt).Select(e => new EventInput
        {
            Id = e.Id, Title = e.Title, Cover = e.Cover, Description = e.Description, Start = e.Start, End = e.End, TimeZone = e.TimeZone,
            Country = e.Country, State = e.State, City = e.City, Venue = e.Venue, Organizer = e.Organizer, Registration = e.Registration, Published = e.Published,
            SubmissionStatus = e.PendingReview ? "Pending review" : e.Published ? "Published" : e.ReviewNote != null ? "Changes requested" : e.ReviewedAt != null ? "Unpublished" : "Draft",
            ReviewNote = e.ReviewNote
        }).ToListAsync();
        await PopulateEventContacts(events);
        return events;
    }
    public async Task<Ok> SaveEvent(EventInput input)
    {
        var me = await User(admin: true);
        var title = Text(input.Title, 150, true); var description = Text(input.Description, 4000); var city = Text(input.City, 80, true); var country = Text(input.Country, 80, true); var state = Text(input.State, 80, true);
        var venue = Text(input.Venue, 200, true); var registration = Url(input.Registration);
        var cover = Photo(input.Cover);
        var times = ResolveTimes(input, country, state, city);
        var contacts = ReadContactIds(input);
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockContactMembers(me.Id, contacts);
        if (input.Id != Guid.Empty) await EventLock(input.Id);
        var e = input.Id == Guid.Empty ? null : await db.Events.FindAsync(input.Id);
        if (input.Id != Guid.Empty && e == null) throw Missing();
        var created = e == null; var wasPublished = e?.Published == true;
        e ??= new() { SubmittedBy = me.Id, SubmittedAt = DateTimeOffset.UtcNow };
        if (db.Entry(e).State == EntityState.Detached) db.Events.Add(e);
        e.Title = title; e.Description = description; e.Country = country; e.State = state; e.City = city; e.Venue = venue; e.Registration = registration;
        e.Cover = cover; e.Start = times.Start; e.End = times.End; e.TimeZone = times.TimeZone; e.Featured = input.Featured; e.Published = input.Published;
        await UpdateEventContacts(e, contacts);
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
        var details = ValidatePublicationDetails(input.Title, input.Description, input.Registration, input.Cover);
        var occurrence = ValidateOccurrence(new(input.Country, input.State, input.City), input.Venue, input.LocalStart, input.LocalEnd);
        var contacts = ReadContactIds(input);
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockContactMembers(me.Id, contacts);
        await RequireEventPoster(me.Id);
        if (input.Id != Guid.Empty) await EventLock(input.Id);
        var e = input.Id == Guid.Empty ? null : await db.Events.SingleOrDefaultAsync(x => x.Id == input.Id && x.SubmittedBy == me.Id);
        if (input.Id != Guid.Empty && e == null) throw Missing();
        if (e is { Published: false, ReviewedAt: not null }) throw new ApiException(409, "Contact the community team to republish this event.");
        var created = e == null;
        var wasPublished = e?.Published == true;
        e ??= new Gathering { SubmittedBy = me.Id, SubmittedAt = DateTimeOffset.UtcNow };
        if (db.Entry(e).State == EntityState.Detached) db.Events.Add(e);
        ApplyPublication(e, details, occurrence);
        await UpdateEventContacts(e, contacts);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        lifecycle.RecordSubject(created ? LifecycleEvent.EventCreated : LifecycleEvent.EventUpdated, "event", e.Id, me.Id);
        if (!wasPublished) lifecycle.RecordSubject(LifecycleEvent.EventPublished, "event", e.Id, me.Id);
        return new Ok();
    }

    public async Task<EventBatchResult> PublishBatch(EventBatchInput input)
    {
        var me = await User();
        if (input.RequestId == Guid.Empty) throw new ApiException(400, "A publication request ID is required.");
        if (input.Dates is not { Length: > 0 and <= EventBatchInput.MaxDates })
            throw new ApiException(400, $"Enter between 1 and {EventBatchInput.MaxDates} event dates.");
        if (input.ContactIds?.Contains(Guid.Empty) == true) throw new ApiException(400, "Choose contacts from VeganAdda members.");
        var contacts = (input.ContactIds ?? []).Where(id => id != me.Id).Distinct().ToArray();

        await using var tx = await db.Database.BeginTransactionAsync();
        await LockContactMembers(me.Id, contacts);
        await RequireEventPoster(me.Id);
        var receipt = await db.EventPublicationBatches.FindAsync(me.Id, input.RequestId);
        if (receipt != null) return new(receipt.EventIds);

        var details = ValidatePublicationDetails(input.Title, input.Description, input.Registration, input.Cover);
        var defaultPlace = ValidatePlace(input.Place);
        if (contacts.Length > 0 && await VisibleEventMembers().CountAsync(p => contacts.Contains(p.Id)) != contacts.Length)
            throw new ApiException(400, "Choose available VeganAdda members as contacts.");

        var events = new List<Gathering>(input.Dates.Length);
        var occurrences = new HashSet<(EventPlace, string, DateTimeOffset)>();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < input.Dates.Length; i++)
        {
            try
            {
                var date = input.Dates[i] ?? throw new ApiException(400, "Enter the date, start time, and venue.");
                var occurrence = ValidateOccurrence(date.Place ?? defaultPlace, date.Venue,
                    date.LocalStart == default ? null : date.LocalStart, end: null);
                if (!occurrences.Add((occurrence.Place, occurrence.Venue, occurrence.Times.Start)))
                    throw new ApiException(400, "An event with this venue and start time is already in this batch.");
                var gathering = new Gathering { SubmittedBy = me.Id, SubmittedAt = now };
                ApplyPublication(gathering, details, occurrence);
                events.Add(gathering);
            }
            catch (ApiException ex) when (ex.Status == 400)
            {
                throw new ApiException(400, $"Event {i + 1} · {ex.Message}");
            }
        }

        db.Events.AddRange(events);
        db.EventContacts.AddRange(events.SelectMany(e => contacts.Select(id => new GatheringContact { EventId = e.Id, MemberId = id })));
        var eventIds = events.Select(e => e.Id).ToArray();
        db.EventPublicationBatches.Add(new() { SubmittedBy = me.Id, RequestId = input.RequestId, EventIds = eventIds });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        foreach (var id in eventIds)
        {
            lifecycle.RecordSubject(LifecycleEvent.EventCreated, "event", id, me.Id);
            lifecycle.RecordSubject(LifecycleEvent.EventPublished, "event", id, me.Id);
        }
        return new(eventIds);
    }

    private async Task RequireEventPoster(Guid id)
    {
        if (!await EventPosters().AnyAsync(p => p.Id == id))
            throw new ApiException(403, "Only a verified vegan member can post an event.");
    }

    private sealed record PublicationDetails(string Title, string Description, string? Registration, string Cover);
    private sealed record PublicationOccurrence(EventPlace Place, string Venue, EventTimes Times);

    private static PublicationDetails ValidatePublicationDetails(string title, string description, string? registration, string cover)
        => new(Text(title, 150, true), Text(description, 4000), Url(registration), Photo(cover));

    private static EventPlace ValidatePlace(EventPlace? place)
        => new(Text(place?.Country, 80, true), Text(place?.State, 80, true), Text(place?.City, 80, true));

    private static PublicationOccurrence ValidateOccurrence(EventPlace place, string venue, DateTime? start, DateTime? end)
    {
        var validPlace = ValidatePlace(place);
        return new(validPlace, Text(venue, 200, true), ResolveTimes(start, end, validPlace.Country, validPlace.State, validPlace.City));
    }

    private static void ApplyPublication(Gathering gathering, PublicationDetails details, PublicationOccurrence occurrence)
    {
        gathering.Title = details.Title;
        gathering.Description = details.Description;
        gathering.Registration = details.Registration;
        gathering.Cover = details.Cover;
        gathering.Country = occurrence.Place.Country;
        gathering.State = occurrence.Place.State;
        gathering.City = occurrence.Place.City;
        gathering.Venue = occurrence.Venue;
        gathering.Start = occurrence.Times.Start;
        gathering.End = occurrence.Times.End;
        gathering.TimeZone = occurrence.Times.TimeZone;
        gathering.Published = true;
        gathering.PendingReview = false;
        gathering.ReviewNote = null;
    }

    private Task EventLock(Guid id) => PairLock("event:" + id);

    private static Guid[]? ReadContactIds(EventInput input)
    {
        var ids = input.Contacts?.Select(c => c?.Id ?? Guid.Empty).Distinct().ToArray();
        if (ids?.Contains(Guid.Empty) == true) throw new ApiException(400, "Choose contacts from VeganAdda members.");
        return ids;
    }

    private async Task LockContactMembers(Guid editor, Guid[]? contacts)
    {
        foreach (var member in (contacts ?? []).Append(editor).Distinct().Order())
            await UserLock(member);
    }

    private async Task UpdateEventContacts(Gathering gathering, Guid[]? contacts)
    {
        if (contacts == null) return;
        var desired = contacts.Where(id => id != gathering.SubmittedBy).ToHashSet();
        var existing = await db.EventContacts.Where(c => c.EventId == gathering.Id).ToListAsync();
        var ids = desired.Concat(existing.Select(c => c.MemberId)).Distinct().ToArray();
        if (ids.Length == 0) return;
        var visible = await VisibleEventMembers().Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToHashSetAsync();
        var present = existing.Select(c => c.MemberId).ToHashSet();
        if (desired.Any(id => !visible.Contains(id) && !present.Contains(id)))
            throw new ApiException(400, "Choose available VeganAdda members as contacts.");

        // An unrelated edit must not remove a contact hidden from the editor.
        db.EventContacts.RemoveRange(existing.Where(c => visible.Contains(c.MemberId) && !desired.Contains(c.MemberId)));
        db.EventContacts.AddRange(desired.Where(id => !present.Contains(id))
            .Select(id => new GatheringContact { EventId = gathering.Id, MemberId = id }));
    }

    private sealed record EventTimes(string TimeZone, DateTimeOffset Start, DateTimeOffset? End);

    private static EventTimes ResolveTimes(EventInput input, string country, string state, string city)
        => ResolveTimes(input.LocalStart, input.LocalEnd, country, state, city);

    private static EventTimes ResolveTimes(DateTime? localStart, DateTime? localEnd, string country, string state, string city)
    {
        if (localStart == null) throw new ApiException(400, "Enter the start date and time.");
        if (localEnd.HasValue && localEnd <= localStart) throw new ApiException(400, "End date and time must be after the start.");
        var zoneId = EventTimeZoneCatalog.Resolve(country, state, city);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        try
        {
            var start = ToUtc(localStart.Value, zone);
            DateTimeOffset? end = localEnd.HasValue ? ToUtc(localEnd.Value, zone) : null;
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
