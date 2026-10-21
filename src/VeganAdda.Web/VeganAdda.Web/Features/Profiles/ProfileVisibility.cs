using Microsoft.EntityFrameworkCore;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Profiles;

public sealed class ProfileVisibility(FeatureContext context) : FeatureService(context)
{
    public IQueryable<Profile> Query(bool includeRelatedProfiles = false)
    {
        var q = db.Profiles.Where(BasicProfile.Complete).Where(p => db.Accounts.Any(a => a.Id == p.Id && !a.Suspended));
        if (Viewer is not { } v) return q.Where(p => p.Public && !p.TestProfile);
        return q.Where(p => (!p.TestProfile || p.Id == v || db.Accounts.Any(a => a.Id == v && a.Admin))
            && (db.Accounts.Any(a => a.Id == v && a.Admin) || p.Id == v || !db.Profiles.Any(x => x.Id == v && x.TestProfile) || db.Accounts.Any(a => a.Id == p.Id && a.Admin))
            && !db.Blocks.Any(b => (b.Owner == v && b.Subject == p.Id) || (b.Subject == v && b.Owner == p.Id)));
    }
    public Task<bool> CanReadPhoto(string url, Guid? id, bool isAdmin, bool isModerator)
        => db.Profiles.AnyAsync(p => p.Photo == url && db.Accounts.Any(a => a.Id == p.Id && !a.Suspended)
            && ((id.HasValue && p.Id == id) || isAdmin || (isModerator && p.IsVegan == true && !p.VeganVerified && !p.TestProfile)
                || (db.Profiles.Where(BasicProfile.Complete).Any(x => x.Id == p.Id) && !p.TestProfile && (id.HasValue || p.Public)
                    && (!id.HasValue || (db.Profiles.Where(BasicProfile.Complete).Any(x => x.Id == id)
                        && (!db.Profiles.Any(x => x.Id == id && x.TestProfile) || db.Accounts.Any(a => a.Id == p.Id && a.Admin))
                        && !db.Blocks.Any(b => (b.Owner == id && b.Subject == p.Id) || (b.Subject == id && b.Owner == p.Id)))))));
}
