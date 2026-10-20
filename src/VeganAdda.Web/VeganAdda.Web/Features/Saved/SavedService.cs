using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Events;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Saved;

public sealed class SavedService(FeatureContext context, ProfileVisibility visibility,
    PeopleProjection people, EventProjection events) : FeatureService(context)
{
    public async Task<SavedItems> GetSaved(int peoplePage = 1, int eventsPage = 1)
    {
        var me = await User(true);
        const int pageSize = 12;
        var personIds = db.Saved.Where(s => s.UserId == me.Id && s.Kind == "person").Select(s => s.TargetId);
        var eventIds = db.Saved.Where(s => s.UserId == me.Id && s.Kind == "event").Select(s => s.TargetId);
        var personQuery = visibility.Query().Where(p => personIds.Contains(p.Id));
        var eventQuery = db.Events.Where(e => e.Published && eventIds.Contains(e.Id));
        var peopleTotal = await personQuery.CountAsync();
        var eventsTotal = await eventQuery.CountAsync();
        peoplePage = Math.Clamp(peoplePage, 1, Math.Max(1, (peopleTotal + pageSize - 1) / pageSize));
        eventsPage = Math.Clamp(eventsPage, 1, Math.Max(1, (eventsTotal + pageSize - 1) / pageSize));
        var peopleRows = await personQuery.OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((peoplePage - 1) * pageSize).Take(pageSize).ToListAsync();
        var eventRows = await eventQuery.OrderBy(e => e.Start).ThenBy(e => e.Id).Skip((eventsPage - 1) * pageSize).Take(pageSize).ToListAsync();
        return new(await people.Many(peopleRows), await events.Many(eventRows), peopleTotal, eventsTotal, peoplePage, eventsPage);
    }
}
