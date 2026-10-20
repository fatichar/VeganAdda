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

namespace VeganAdda.Web.Features.Saved;

public sealed class SavedService(FeatureContext context, ProfileVisibility visibility,
    PeopleProjection people, EventProjection events) : FeatureService(context)
{
    public async Task<SavedItems> GetSaved()
    {
        var saver = await User(true);
                    var personIds = db.Saved.Where(s => s.UserId == saver.Id && s.Kind == "person").Select(s => s.TargetId);
                    var eventIds = db.Saved.Where(s => s.UserId == saver.Id && s.Kind == "event").Select(s => s.TargetId);
                    var savedPeople = new List<Person>(); foreach (var p in await visibility.Query().Where(p => personIds.Contains(p.Id)).ToListAsync()) savedPeople.Add(await people.One(p));
                    var savedEvents = new List<EventCard>(); foreach (var e in await db.Events.Where(e => e.Published && eventIds.Contains(e.Id)).ToListAsync()) savedEvents.Add(await events.One(e));
                    return new SavedItems(savedPeople, savedEvents);
    }
}
