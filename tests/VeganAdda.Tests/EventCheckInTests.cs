using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public class EventCheckInTests(DatabaseFixture f)
{
    private async Task<Guid> Event(int startHours = -1, int endHours = 8)
    {
        await using var db = f.Db();
        var e = new Gathering { Title = "Check-in test", Published = true, Start = DateTimeOffset.UtcNow.AddHours(startHours), End = DateTimeOffset.UtcNow.AddHours(endHours) };
        db.Events.Add(e); await db.SaveChangesAsync(); return e.Id;
    }
    private Task<object> Attend(Guid member, Guid ev, bool visible = true, bool attending = true)
        => f.Call(member, $"events/{ev}/attendance", new AttendanceInput(attending, visible), "POST");
    private Task<object> CheckIn(Guid? member, Guid ev, bool value = true)
        => f.Call(member, $"events/{ev}/check-in", new CheckInInput(value), "POST");
    private async Task<Page<Person>> People(Guid? viewer, Guid ev, bool checkedIn = true)
        => Assert.IsType<Page<Person>>(await f.Call(viewer, $"people?event={ev}&checkedIn={checkedIn.ToString().ToLowerInvariant()}"));

    [Fact]
    public async Task Check_in_requires_authentication_attendance_and_visibility()
    {
        var ev = await Event(); var member = await f.Member();
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => CheckIn(null, ev))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => CheckIn(member, ev))).Status);
        await Attend(member, ev, visible: false);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => CheckIn(member, ev))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => People(null, ev))).Status);
        await Attend(member, ev);
        await CheckIn(member, ev);
        var dto = Assert.IsType<EventCard>(await f.Call(member, $"events/{ev}"));
        Assert.InRange(dto.CheckedInUntil!.Value, DateTimeOffset.UtcNow.AddHours(3.99), DateTimeOffset.UtcNow.AddHours(4));
        Assert.Null(Assert.IsType<EventCard>(await f.Call(null, $"events/{ev}")).CheckedInUntil);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(-5, -1)]
    public async Task Check_in_is_only_allowed_during_the_event(int start, int end)
    {
        var ev = await Event(start, end); var member = await f.Member(); await Attend(member, ev);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => CheckIn(member, ev))).Status);
        await CheckIn(member, ev, false); // Checkout remains harmless after the event.
    }

    [Fact]
    public async Task Check_out_visibility_and_attendance_changes_remove_presence()
    {
        var ev = await Event(); var member = await f.Member(); var viewer = await f.Member();
        await Attend(member, ev); await CheckIn(member, ev); await CheckIn(member, ev); // Repeated check-in is safe.
        Assert.Equal(member, Assert.Single((await People(viewer, ev)).Items).Id);
        await CheckIn(member, ev, false);
        Assert.Empty((await People(viewer, ev)).Items);
        Assert.Equal(member, Assert.Single((await People(viewer, ev, false)).Items).Id);
        await CheckIn(member, ev); await Attend(member, ev, visible: false);
        Assert.Empty((await People(viewer, ev)).Items);
        await Attend(member, ev); // Re-enabling visibility must not restore an old check-in.
        Assert.Empty((await People(viewer, ev)).Items);
        await CheckIn(member, ev); await Attend(member, ev, attending: false);
        Assert.Empty((await People(viewer, ev)).Items);
        await using var db = f.Db();
        Assert.Null(await db.Attendance.FindAsync(member, ev));
    }

    [Fact]
    public async Task Expired_check_ins_and_ended_events_are_excluded_without_cleanup_jobs()
    {
        var ev = await Event(endHours: 1); var member = await f.Member(); var viewer = await f.Member();
        await Attend(member, ev); await CheckIn(member, ev);
        await using (var db = f.Db())
        {
            var e = (await db.Events.FindAsync(ev))!;
            Assert.Equal(e.End, (await db.Attendance.FindAsync(member, ev))!.CheckedInUntil);
            (await db.Attendance.FindAsync(member, ev))!.CheckedInUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        Assert.Empty((await People(viewer, ev)).Items);
        Assert.Null(Assert.IsType<EventCard>(await f.Call(member, $"events/{ev}")).CheckedInUntil);
        await CheckIn(member, ev);
        await using (var db = f.Db()) { (await db.Events.FindAsync(ev))!.End = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); }
        Assert.Empty((await People(viewer, ev)).Items);
        Assert.Null(Assert.IsType<EventCard>(await f.Call(member, $"events/{ev}")).CheckedInUntil);
    }

    [Fact]
    public async Task Event_discovery_preserves_privacy_filters_pagination_and_post_search()
    {
        var ev = await Event(); var other = await Event(); var viewer = await f.Member();
        var members = new List<Guid>();
        for (var i = 0; i < 14; i++) { var id = await f.Member(pub: false, dating: true); members.Add(id); await Attend(id, ev); await CheckIn(id, ev); }
        var elsewhere = await f.Member(); await Attend(elsewhere, other); await CheckIn(elsewhere, other);
        await using (var db = f.Db())
        {
            db.Blocks.Add(new() { Owner = members[0], Subject = viewer });
            (await db.Profiles.FindAsync(members[1]))!.VeganVerified = false;
            await db.SaveChangesAsync();
        }
        var result = await People(viewer, ev);
        Assert.Equal(13, result.Total); Assert.Equal(12, result.Items.Count);
        Assert.DoesNotContain(result.Items, p => p.Id == elsewhere || p.Id == members[0]);
        Assert.DoesNotContain(result.Items.SelectMany(p => p.LookingFor), i => i == "Dating");
        Assert.Single(Assert.IsType<Page<Person>>(await f.Call(viewer, $"people?event={ev}&checkedIn=true&page=2")).Items);
        var post = Assert.IsType<Page<Person>>(await f.Call(viewer, "people/search", new DiscoveryInput { Event = ev.ToString(), CheckedIn = true }, "POST"));
        Assert.Equal(result.Items.Select(p => p.Id), post.Items.Select(p => p.Id));
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(viewer, "people?event=invalid&checkedIn=true"))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => f.Call(viewer, "people?checkedIn=true"))).Status);
        await using (var db = f.Db()) { (await db.Events.FindAsync(ev))!.Published = false; await db.SaveChangesAsync(); }
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => People(viewer, ev))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => CheckIn(viewer, ev))).Status);
    }
}
