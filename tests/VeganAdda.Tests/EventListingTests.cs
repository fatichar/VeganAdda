using Microsoft.EntityFrameworkCore;
using VeganAdda.Web.Features.Events;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class EventListingTests(DatabaseFixture f)
{
    [Fact]
    public async Task A_member_event_publishes_immediately_and_preserves_optional_details()
    {
        var member = await f.Member(); var admin = await f.Member(admin: true);
        var title = "Gathering-" + Guid.NewGuid().ToString("N");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var expectedStart = DateTimeOffset.UtcNow.AddMinutes(-30);
        var localStart = TimeZoneInfo.ConvertTime(expectedStart, zone).DateTime;
        var input = new EventInput { Title = title, Country = "India", State = "Maharashtra", City = "Pune", Venue = "Community hall", LocalStart = localStart };
        await f.Call(member, "event-submissions", input, "POST");
        var submission = Assert.Single(Assert.IsType<List<EventInput>>(await f.Call(member, "event-submissions")), e => e.Title == title);
        Assert.Null(submission.End); Assert.Equal("", submission.Organizer); Assert.Equal("", submission.Description);
        Assert.Equal("Maharashtra", submission.State); Assert.Equal("India", submission.Country);
        Assert.Equal("Asia/Kolkata", submission.TimeZone);
        Assert.InRange((submission.Start!.Value - expectedStart).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        Assert.True(submission.Published);
        Assert.Equal("Published", submission.SubmissionStatus);
        var listed = Assert.IsType<Page<EventCard>>(await f.Call(null, "events?country=India&state=Maharashtra&city=Pune"));
        Assert.Contains(listed.Items, e => e.Id == submission.Id);
        Assert.Empty(listed.Items.Single(e => e.Id == submission.Id).Contacts);
        submission.LocalStart = localStart;
        submission.Title += " updated";
        await f.Call(member, "event-submissions", submission, "POST");
        await f.Call(admin, "admin/events", submission, "POST");
        await f.Call(member, $"events/{submission.Id}/attendance", new AttendanceInput(true, true), "POST");
        await f.Call(member, $"events/{submission.Id}/check-in", new CheckInInput(true), "POST");
        var detail = Assert.IsType<EventCard>(await f.Call(member, $"events/{submission.Id}"));
        Assert.Equal(member, detail.Creator?.Id);
        Assert.Empty(detail.Contacts);
        var anonymous = Assert.IsType<EventCard>(await f.Call(null, $"events/{submission.Id}"));
        Assert.Null(anonymous.Creator);
        Assert.Empty(anonymous.Contacts);
        Assert.Equal(submission.Start!.Value.AddHours(4), detail.CheckedInUntil);
        submission.LocalEnd = localStart.AddMinutes(-1);
        var error = await Assert.ThrowsAsync<ApiException>(() => f.Call(admin, "admin/events", submission, "POST"));
        Assert.Equal(400, error.Status);
    }

    [Fact]
    public async Task Migration_preserves_linked_contacts_without_duplicating_the_poster()
    {
        var isolated = new DatabaseFixture();
        await isolated.InitializeAt("VerifiedProfilePhone");
        try
        {
            var poster = await isolated.Member();
            var contact = await isolated.Member();
            var legacy = new Gathering { Title = "Legacy outreach", Published = true, SubmittedBy = poster, ContactId = contact };
            var self = new Gathering { Title = "Self contact", Published = true, SubmittedBy = poster, ContactId = poster };
            await using (var db = isolated.Db())
            {
                db.Events.AddRange(legacy, self);
                await db.SaveChangesAsync();
                await db.Database.MigrateAsync();
            }

            var migrated = Assert.IsType<EventCard>(await isolated.Call(poster, $"events/{legacy.Id}"));
            Assert.Equal(poster, migrated.Creator?.Id);
            Assert.Equal(contact, Assert.Single(migrated.Contacts).Id);
            var selfContact = Assert.IsType<EventCard>(await isolated.Call(poster, $"events/{self.Id}"));
            Assert.Equal(poster, selfContact.Creator?.Id);
            Assert.Empty(selfContact.Contacts);
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task Member_contacts_follow_profile_visibility_and_admin_takedowns_cannot_be_undone_by_the_owner()
    {
        var owner = await f.Member();
        var contact = await f.Member(pub: false);
        var second = await f.Member();
        var attendee = await f.Member();
        var admin = await f.Member(admin: true);
        var contactName = $"Event contact {Guid.NewGuid():N}";
        await using (var db = f.Db())
        {
            (await db.Profiles.FindAsync(contact))!.Name = contactName;
            await db.SaveChangesAsync();
        }
        var candidates = await f.Feature<EventsService, List<EventContact>>(owner, s => s.SearchEventContacts(contactName));
        Assert.Contains(candidates, c => c.Id == contact);

        var input = new EventInput
        {
            Title = "Outreach", Country = "India", State = "Maharashtra", City = "Pune", Venue = "Community hall",
            LocalStart = DateTime.UtcNow.AddDays(4),
            Contacts = [new(Guid.NewGuid(), "Unavailable member")]
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(owner, "event-submissions", input, "POST"))).Status);
        input.Contacts = [new(contact, "Client-supplied name"), new(second, ""), new(owner, ""), new(second, "")];
        await f.Call(owner, "event-submissions", input, "POST");
        var posted = Assert.Single(Assert.IsType<List<EventInput>>(await f.Call(owner, "event-submissions")));
        Assert.Equal(owner, posted.Creator?.Id);
        Assert.Equal(2, posted.Contacts!.Length);
        Assert.Contains(new EventContact(contact, contactName), posted.Contacts);
        Assert.Empty(Assert.IsType<EventCard>(await f.Call(null, $"events/{posted.Id}")).Contacts);
        Assert.Equal(posted.Contacts, Assert.IsType<EventCard>(await f.Call(attendee, $"events/{posted.Id}")).Contacts);

        await f.Call(contact, $"people/{attendee}/block", null, "POST");
        var blocked = Assert.IsType<EventCard>(await f.Call(attendee, $"events/{posted.Id}"));
        Assert.Equal(second, Assert.Single(blocked.Contacts).Id);

        await f.Call(contact, $"people/{owner}/block", null, "POST");
        var editable = Assert.Single(Assert.IsType<List<EventInput>>(await f.Call(owner, "event-submissions")));
        Assert.Equal(second, Assert.Single(editable.Contacts!).Id);
        input.Id = posted.Id;
        input.Contacts = [];
        await f.Call(owner, "event-submissions", input, "POST");
        var afterEdit = Assert.IsType<EventCard>(await f.Call(admin, $"events/{posted.Id}"));
        Assert.Equal(contact, Assert.Single(afterEdit.Contacts).Id);

        await f.Call(contact, "account", new DeleteInput("DELETE"), "DELETE");
        Assert.Empty(Assert.IsType<EventCard>(await f.Call(admin, $"events/{posted.Id}")).Contacts);

        input.Contacts = [new(second, "")];
        input.Published = true;
        await f.Call(admin, "admin/events", input, "POST");
        var updated = Assert.IsType<EventCard>(await f.Call(attendee, $"events/{posted.Id}"));
        Assert.Equal(owner, updated.Creator?.Id);
        Assert.Equal(second, Assert.Single(updated.Contacts).Id);
        input.Published = false;
        await f.Call(admin, "admin/events", input, "POST");
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => f.Call(owner, "event-submissions", input, "POST"))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => f.Call(null, $"events/{posted.Id}"))).Status);
    }

    [Fact]
    public async Task Profile_events_show_only_upcoming_published_rsvps_with_member_visibility()
    {
        var person = await f.Member();
        var viewer = await f.Member();
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var shared = new Gathering { Title = "Shared", Published = true, Start = start };
        var privateEvent = new Gathering { Title = "Private", Published = true, Start = start.AddHours(1) };
        var past = new Gathering { Title = "Past", Published = true, Start = start.AddDays(-2) };
        var draft = new Gathering { Title = "Draft", Start = start };
        await using (var db = f.Db())
        {
            db.Events.AddRange(shared, privateEvent, past, draft);
            db.Attendance.AddRange(
                new Attendance { UserId = person, EventId = shared.Id, Visible = true },
                new Attendance { UserId = person, EventId = privateEvent.Id, Visible = false },
                new Attendance { UserId = person, EventId = past.Id, Visible = true },
                new Attendance { UserId = person, EventId = draft.Id, Visible = true });
            await db.SaveChangesAsync();
        }

        Task<Page<ProfileEvent>> Read(Guid? id) => f.Feature<EventsService, Page<ProfileEvent>>(id, s => s.GetProfileEvents(person, 1));
        Assert.Equal(shared.Id, Assert.Single((await Read(viewer)).Items).Id);
        var ownEvents = (await Read(person)).Items;
        Assert.Equal(new[] { shared.Id, privateEvent.Id }, ownEvents.Select(e => e.Id));
        Assert.False(ownEvents[1].Visible);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => Read(null))).Status);

        await f.Call(person, $"people/{viewer}/block", null, "POST");
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => Read(viewer))).Status);
    }

    [Fact]
    public async Task Schedule_pages_chronologically_and_projects_the_viewers_attendance()
    {
        var member = await f.Member();
        var city = "Schedule-" + Guid.NewGuid().ToString("N");
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var events = Enumerable.Range(0, 53).Select(i => new Gathering
        {
            Title = $"Outreach {i}", City = city, Published = true, Featured = i == 52, Start = start.AddDays(i)
        }).ToList();
        await using (var db = f.Db())
        {
            db.Events.AddRange(events);
            db.Events.Add(new Gathering { Title = "Draft", City = city, Start = start });
            db.Events.Add(new Gathering { Title = "Past", City = city, Published = true, Start = start.AddDays(-2) });
            await db.SaveChangesAsync();
        }
        await f.Call(member, $"events/{events[1].Id}/attendance", new AttendanceInput(true, false), "POST");
        var first = await f.Feature<EventsService, Page<ScheduledEvent>>(member, s => s.GetSchedule("", "", city, 1));
        var second = await f.Feature<EventsService, Page<ScheduledEvent>>(member, s => s.GetSchedule("", "", city, 2));
        Assert.Equal(53, first.Total);
        Assert.Equal(50, first.PageSize);
        Assert.Equal(events.Select(e => e.Id), first.Items.Concat(second.Items).Select(e => e.Id));
        Assert.Equal(events[1].Id, Assert.Single(first.Items, e => e.Attending).Id);
    }

    [Fact]
    public async Task Admin_location_filters_distinguish_identically_named_cities_and_page_after_filtering()
    {
        var admin = await f.Member(admin: true); var city = "City-" + Guid.NewGuid().ToString("N");
        await using (var db = f.Db())
        {
            for (var i = 0; i < 23; i++) db.Events.Add(new Gathering { Title = $"Local {i}", Country = "India", State = "Maharashtra", City = city });
            db.Events.Add(new Gathering { Title = "Other country", Country = "Canada", State = "Ontario", City = city });
            await db.SaveChangesAsync();
        }
        var first = Assert.IsType<AdminData>(await f.Call(admin, $"admin?section=Events&country=India&state=Maharashtra&city={city}"));
        var second = Assert.IsType<AdminData>(await f.Call(admin, $"admin?section=Events&country=India&state=Maharashtra&city={city}&page=2"));
        Assert.Equal(23, first.EventsTotal); Assert.Equal(20, first.Events.Count); Assert.Equal(3, second.Events.Count);
        Assert.All(first.Events.Concat(second.Events), e => Assert.Equal("India", e.Country));
        Assert.Equal(23, first.Events.Concat(second.Events).Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Country_and_state_scopes_use_the_picker_catalog_and_city_aliases()
    {
        var india = EventLocationCatalog.Cities("India", "");
        Assert.Contains("nagpur", india); Assert.Contains("bengaluru", india);
        var maharashtra = EventLocationCatalog.Cities("india", "maharashtra");
        Assert.Contains("nagpur", maharashtra); Assert.Contains("mumbai", maharashtra); Assert.DoesNotContain("bengaluru", maharashtra);
        Assert.Contains("delhi ncr", EventLocationCatalog.Cities("India", "Delhi"));
        Assert.DoesNotContain("nagpur", EventLocationCatalog.Cities("Canada", ""));
        Assert.Empty(EventLocationCatalog.Cities("Unknown country", ""));
    }

    [Fact]
    public void Venue_time_zones_are_inferred_from_the_selected_city()
    {
        Assert.Equal("Asia/Kolkata", EventTimeZoneCatalog.Resolve("India", "Maharashtra", "Pune"));
        Assert.Equal("America/Los_Angeles", EventTimeZoneCatalog.Resolve("United States", "California", "Los Angeles"));
        Assert.Equal(400, Assert.Throws<ApiException>(() => EventTimeZoneCatalog.Resolve("India", "Maharashtra", "Unknown town")).Status);
    }

    [Fact]
    public async Task Country_or_state_can_filter_events_without_selecting_a_city()
    {
        Guid id;
        await using (var db = f.Db())
        {
            var e = new Gathering { Title = "Nagpur test", City = "Nagpur", Published = true, Featured = true, Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow.AddDays(1) };
            db.Events.Add(e); await db.SaveChangesAsync(); id = e.Id;
        }
        Assert.Contains(Assert.IsType<Page<EventCard>>(await f.Call(null, "events?country=India")).Items, e => e.Id == id);
        Assert.Contains(Assert.IsType<Page<EventCard>>(await f.Call(null, "events?country=India&state=Maharashtra")).Items, e => e.Id == id);
        Assert.DoesNotContain(Assert.IsType<Page<EventCard>>(await f.Call(null, "events?country=India&state=Karnataka")).Items, e => e.Id == id);
        Assert.DoesNotContain(Assert.IsType<Page<EventCard>>(await f.Call(null, "events?country=Canada")).Items, e => e.Id == id);
        Assert.DoesNotContain(Assert.IsType<Page<EventCard>>(await f.Call(null, "events?country=India&state=Maharashtra&city=Pune")).Items, e => e.Id == id);
    }

    [Fact]
    public async Task Event_filters_include_past_published_events_with_correct_pagination_and_city_matching()
    {
        var city = "City-" + Guid.NewGuid().ToString("N");
        Guid futureId, draftId;
        await using (var db = f.Db())
        {
            for (var i = 1; i <= 14; i++) db.Events.Add(new Gathering { Title = $"Past {i}", City = city, Published = true, Start = DateTimeOffset.UtcNow.AddDays(-i).AddHours(-1), End = DateTimeOffset.UtcNow.AddDays(-i) });
            var future = new Gathering { Title = "Future", City = city, Published = true, Start = DateTimeOffset.UtcNow.AddDays(1), End = DateTimeOffset.UtcNow.AddDays(2) };
            var draft = new Gathering { Title = "Draft", City = city, Published = false, Start = DateTimeOffset.UtcNow.AddDays(1), End = DateTimeOffset.UtcNow.AddDays(2) };
            db.Events.AddRange(future, draft); await db.SaveChangesAsync(); futureId = future.Id; draftId = draft.Id;
        }
        var upcoming = Assert.IsType<Page<EventCard>>(await f.Call(null, $"events?city={city.ToLowerInvariant()}"));
        Assert.Equal(futureId, Assert.Single(upcoming.Items).Id);
        var first = Assert.IsType<Page<EventCard>>(await f.Call(null, $"events?city={city}&includePast=true"));
        var second = Assert.IsType<Page<EventCard>>(await f.Call(null, $"events?city={city}&includePast=true&page=2"));
        Assert.Equal(15, first.Total); Assert.Equal(15, second.Total);
        Assert.Equal(12, first.Items.Count); Assert.Equal(3, second.Items.Count);
        Assert.Equal(15, first.Items.Concat(second.Items).Select(e => e.Id).Distinct().Count());
        Assert.DoesNotContain(first.Items.Concat(second.Items), e => e.Id == draftId);
        Assert.All(first.Items.Concat(second.Items), e => Assert.Equal(city, e.City));
    }
}
