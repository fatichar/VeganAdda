using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Profiles.BirthDetails;

namespace VeganAdda.Web.Features.Profiles;

public sealed class PeopleProjection(FeatureContext context) : FeatureService(context)
{
    public async Task<Person> One(Profile profile) => (await Many([profile])).Single();

    // A fixed number of queries per page, never one query per card.
    public async Task<List<Person>> Many(List<Profile> profiles)
    {
        if (profiles.Count == 0) return [];
        var ids = profiles.Select(p => p.Id).ToArray();
        var dating = Viewer is { } viewer && await db.Profiles.AnyAsync(p => p.Id == viewer && p.LookingFor.Contains("Dating"));
        var relationships = Viewer is { } member
            ? await db.Relationships.Where(r => (r.A == member && ids.Contains(r.B)) || (r.B == member && ids.Contains(r.A))).ToDictionaryAsync(r => r.A == member ? r.B : r.A)
            : [];
        var saved = Viewer is { } saver
            ? await db.Saved.Where(s => s.UserId == saver && s.Kind == "person" && ids.Contains(s.TargetId)).Select(s => s.TargetId).ToHashSetAsync()
            : [];
        var demos = await db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Demo);
        return profiles.Select(p =>
        {
            relationships.TryGetValue(p.Id, out var relation);
            var state = relation?.State == "accepted" ? "connected" : relation?.State == "pending" ? (relation.RequestedBy == Viewer ? "sent" : "received") : "none";
            return new Person(p.Id, p.Name, p.City, p.Bio, p.Photo, p.Interests,
                p.LookingFor.Where(x => x != "Dating" || dating).ToArray(), Viewer.HasValue ? p.Instagram : null, Viewer.HasValue ? p.LinkedIn : null, Viewer.HasValue ? p.Website : null,
                p.Featured && p.Public && p.FeatureConsent, saved.Contains(p.Id), p.Id == Viewer ? "self" : state,
                demos[p.Id], p.Country, p.State, p.IsVegan, p.IsVegan == true && p.VeganVerified, Age(p.BirthDate), BirthdayToday(p.BirthDate, p.HasExactBirthDate), p.Profession);
        }).ToList();
    }
}
