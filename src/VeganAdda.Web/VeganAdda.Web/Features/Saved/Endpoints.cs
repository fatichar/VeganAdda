using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Saved;

public static class SavedEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/saved", (SavedService service) => Json(service.GetSaved())).WithName("Saved_Get_0");
    }
}
