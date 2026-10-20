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

public sealed class EventProjection(FeatureContext context) : FeatureService(context)
{
    public async Task<EventCard> One(Gathering e)
    {
        var a = Viewer.HasValue ? await db.Attendance.FindAsync(Viewer.Value, e.Id) : null;
        var now = DateTimeOffset.UtcNow;
        var checkedInUntil = a?.Visible == true && a.CheckedInUntil > now && e.Start <= now && e.End > now
            ? (a.CheckedInUntil < e.End ? a.CheckedInUntil : e.End) : null;
        return new(e.Id, e.Title, e.Cover, e.Description, e.Start, e.End, e.TimeZone, e.City, e.Venue, e.Organizer, e.Registration,
            e.Featured, Viewer.HasValue && await db.Saved.AnyAsync(s => s.UserId == Viewer && s.TargetId == e.Id && s.Kind == "event"), a != null, a?.Visible ?? false, e.Demo, checkedInUntil);
    }
}
