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

namespace VeganAdda.Web.Features.Accounts;

public sealed class SessionService(FeatureContext context) : FeatureService(context)
{
    public async Task<Session> GetSession()
    {
        var u = Viewer.HasValue ? await User() : null;
                    var ownProfile = u == null ? null : await db.Profiles.SingleOrDefaultAsync(p => p.Id == u.Id);
                    return new Session(u?.Id, u?.Name ?? "Visitor", ownProfile != null, u?.Admin ?? false,
                        env.IsDevelopment() && config.GetValue<bool>("Development:Enabled"),
                        new[] { config["Authentication:Firebase:ProjectId"], config["Authentication:Firebase:ApiKey"], config["Authentication:Firebase:AuthDomain"], config["Authentication:Firebase:AppId"] }.All(x => !string.IsNullOrWhiteSpace(x)), u?.Moderator == true && ownProfile?.IsVegan == true && ownProfile?.VeganVerificationPending != true, ownProfile?.IsVegan, ownProfile?.VeganVerificationPending ?? false);
    }
}
