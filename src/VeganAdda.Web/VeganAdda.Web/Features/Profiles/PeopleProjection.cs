using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Profiles.BirthDetails;

namespace VeganAdda.Web.Features.Profiles;

public sealed class PeopleProjection(FeatureContext context) : FeatureService(context)
{
    public async Task<Person> One(Profile profile) => (await Many([profile])).Single();

    internal static PhotoFocus? GetPhotoFocus(Profile profile) =>
        profile.PhotoFocusX is int x && profile.PhotoFocusY is int y ? new(x, y) : null;

    internal static string FirstName(string name) => name.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    // Anonymous viewers may only load the blurred preview served by Media; the original requires sign-in.
    public const string PreviewPrefix = "/media/preview/";
    internal static string PreviewUrl(string photo) => photo.StartsWith("/media/", StringComparison.Ordinal) ? PreviewPrefix + photo[7..] : "";

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
            // Signed-out visitors get a teaser: first name, a blurred preview photo, and no bio or profession.
            var member = Viewer.HasValue;
            return new Person(p.Id, member ? p.Name : FirstName(p.Name), p.City, member ? p.Bio : "", member ? p.Photo : PreviewUrl(p.Photo), p.Interests,
                p.LookingFor.Where(x => x != "Dating" || dating).ToArray(), Viewer.HasValue ? p.Instagram : null, Viewer.HasValue ? p.LinkedIn : null, Viewer.HasValue ? p.Website : null,
                p.Featured && p.Public && p.FeatureConsent, saved.Contains(p.Id), p.Id == Viewer ? "self" : state,
                demos[p.Id], p.Country, p.State, p.IsVegan, p.IsVegan == true && p.VeganVerified, Age(p.BirthDate), BirthdayToday(p.BirthDate, p.HasExactBirthDate), member ? p.Profession : "", GetPhotoFocus(p));
        }).ToList();
    }
}
