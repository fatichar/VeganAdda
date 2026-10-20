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

namespace VeganAdda.Web.Features.Profiles;

public sealed class PeopleProjection(FeatureContext context) : FeatureService(context)
{
    public async Task<Person> One(Profile p)
    {
        bool dating = Viewer is { } v && await db.Profiles.AnyAsync(x => x.Id == v && x.LookingFor.Contains("Dating"));
        var relation = Viewer is { } id ? await db.Relationships.FindAsync(Pair(id, p.Id)) : null;
        var state = relation?.State == "accepted" ? "connected" : relation?.State == "pending" ? (relation.RequestedBy == Viewer ? "sent" : "received") : "none";
        return new(p.Id, p.Name, p.City, p.Bio, p.Photo, p.Interests,
            p.LookingFor.Where(x => x != "Dating" || dating).ToArray(), p.Instagram, p.LinkedIn, p.Website,
            p.Featured && p.Public && p.FeatureConsent, Viewer.HasValue && await db.Saved.AnyAsync(x => x.UserId == Viewer && x.TargetId == p.Id && x.Kind == "person"),
            p.Id == Viewer ? "self" : state, await db.Accounts.Where(a => a.Id == p.Id).Select(a => a.Demo).SingleAsync(), p.Country, p.State, p.IsVegan, p.VeganVerificationPending, Age(p.BirthDate), BirthdayToday(p.BirthDate, p.HasExactBirthDate));
    }
}
