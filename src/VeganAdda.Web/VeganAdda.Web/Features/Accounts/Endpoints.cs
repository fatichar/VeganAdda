using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Accounts;

public static class AccountsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/session", (SessionService service) => Json(service.GetSession())).WithName("Accounts_Get_0");
    }
}
