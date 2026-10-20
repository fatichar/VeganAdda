using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class EventListingTests(DatabaseFixture f)
{
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
