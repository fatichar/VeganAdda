using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Stats;

public static class StatsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/admin/stats", (HttpContext ctx, StatsService service) => Json(service.GetStats(int.TryParse(Q(ctx, "days"), out var days) ? days : 30))).WithName("Stats_Get").RequireAuthorization();
    }
}
