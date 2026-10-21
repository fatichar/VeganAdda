using VeganAdda.Web.Features.Events;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class EventListingTests(DatabaseFixture f)
{
    [Fact]
    public async Task Optional_contact_description_and_end_survive_submission_review_and_editing()
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
        await f.Call(admin, "moderation/event-review", new EventReviewInput(submission.Id, true), "POST");
        var listed = Assert.IsType<Page<EventCard>>(await f.Call(null, "events?country=India&state=Maharashtra&city=Pune"));
        Assert.Contains(listed.Items, e => e.Id == submission.Id);
        submission.Published = true;
        submission.LocalStart = localStart;
        await f.Call(admin, "admin/events", submission, "POST");
        await f.Call(member, $"events/{submission.Id}/attendance", new AttendanceInput(true, true), "POST");
        await f.Call(member, $"events/{submission.Id}/check-in", new CheckInInput(true), "POST");
        var detail = Assert.IsType<EventCard>(await f.Call(member, $"events/{submission.Id}"));
        Assert.Equal(submission.Start!.Value.AddHours(4), detail.CheckedInUntil);
        submission.LocalEnd = localStart.AddMinutes(-1);
        var error = await Assert.ThrowsAsync<ApiException>(() => f.Call(admin, "admin/events", submission, "POST"));
        Assert.Equal(400, error.Status);
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
