using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Features.Profiles;

public sealed class ProfileVisibility(FeatureContext context) : FeatureService(context)
{
    public IQueryable<Profile> Query(bool includeRelatedProfiles = false)
    {
        var q = db.Profiles.Where(p => db.Accounts.Any(a => a.Id == p.Id && !a.Suspended));
        if (Viewer is not { } v) return q.Where(p => p.Public && p.Approved && !p.TestProfile);
        return q.Where(p => (p.Approved || p.Id == v || includeRelatedProfiles && db.Relationships.Any(r => (r.A == v && r.B == p.Id || r.B == v && r.A == p.Id) && (r.State == "pending" || r.State == "accepted")))
            && (!p.TestProfile || p.Id == v || db.Accounts.Any(a => a.Id == v && a.Admin))
            && (db.Accounts.Any(a => a.Id == v && a.Admin) || p.Id == v || !db.Profiles.Any(x => x.Id == v && x.TestProfile) || db.Accounts.Any(a => a.Id == p.Id && a.Admin))
            && !db.Blocks.Any(b => (b.Owner == v && b.Subject == p.Id) || (b.Subject == v && b.Owner == p.Id)));
    }
    public Task<bool> CanReadPhoto(string url, Guid? id, bool isAdmin, bool isModerator)
        => db.Profiles.AnyAsync(p => p.Photo == url && db.Accounts.Any(a => a.Id == p.Id && !a.Suspended)
                && ((id.HasValue && p.Id == id) || isAdmin || (isModerator && !p.Approved && !p.TestProfile) || (p.Approved && (!p.TestProfile || isAdmin) && (id.HasValue || p.Public)
                    && (!id.HasValue || !db.Blocks.Any(b => (b.Owner == id && b.Subject == p.Id) || (b.Subject == id && b.Owner == p.Id))))));
}
