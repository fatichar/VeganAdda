using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Events;

public sealed class EventProjection(FeatureContext context) : FeatureService(context)
{
    public async Task<EventCard> One(Gathering gathering) => (await Many([gathering])).Single();

    public async Task<List<EventCard>> Many(List<Gathering> events)
    {
        if (events.Count == 0) return [];
        var ids = events.Select(e => e.Id).ToArray();
        var attendance = Viewer is { } viewer ? await db.Attendance.Where(a => a.UserId == viewer && ids.Contains(a.EventId)).ToDictionaryAsync(a => a.EventId) : [];
        var saved = Viewer is { } saver ? await db.Saved.Where(s => s.UserId == saver && s.Kind == "event" && ids.Contains(s.TargetId)).Select(s => s.TargetId).ToHashSetAsync() : [];
        var now = DateTimeOffset.UtcNow;
        return events.Select(e =>
        {
            attendance.TryGetValue(e.Id, out var a);
            var checkedIn = a?.Visible == true && a.CheckedInUntil > now && e.Start <= now && e.End > now
                ? (a.CheckedInUntil < e.End ? a.CheckedInUntil : e.End) : null;
            return new EventCard(e.Id, e.Title, e.Cover, e.Description, e.Start, e.End, e.TimeZone, e.City, e.Venue, e.Organizer, e.Registration,
                e.Featured, saved.Contains(e.Id), a != null, a?.Visible ?? false, e.Demo, checkedIn);
        }).ToList();
    }
}
