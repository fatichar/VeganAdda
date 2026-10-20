using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Referrals;

public static class ReferralsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/referrals", (ReferralsService service) => Json(service.CreateReferralInvite())).WithName("Referrals_Post_0").RequireAuthorization();
    }
}
