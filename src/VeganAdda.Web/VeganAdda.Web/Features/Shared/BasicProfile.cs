using System.Linq.Expressions;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Features.Shared;

public static class BasicProfile
{
    // One rule shared by session, access checks, and SQL visibility queries.
    public static readonly Expression<Func<Profile, bool>> Complete = p =>
        p.Name.Trim() != "" && p.IsVegan != null && p.BirthDate != null
        && p.Country.Trim() != "" && p.State.Trim() != "" && p.City.Trim() != ""
        && p.AdultConfirmedAt != default;
    private static readonly Func<Profile, bool> complete = Complete.Compile();
    public static bool IsComplete(Profile? profile) => profile != null && complete(profile);
}
