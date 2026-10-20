using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Connections;

public static class ConnectionsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/connections", (ConnectionsService service) => Json(service.Connections())).WithName("Connections_Get_0");
        api.MapPost("/connections", (HttpContext ctx, ConnectionsService service) => Json(service.ConnectionAction("request", RawBody(ctx)))).WithName("Connections_Post_1");
        api.MapPost("/connections/{action}", (string action, HttpContext ctx, ConnectionsService service) => Json(service.ConnectionAction(action, RawBody(ctx)))).WithName("Connections_Post_2");
        api.MapPost("/people/{id}/{action}", (string id, string action, HttpContext ctx, ConnectionsService service) => Json(service.PersonAction(Guid.Parse(id), action, RawBody(ctx)))).WithName("Connections_Post_3");
        api.MapGet("/blocked", (ConnectionsService service) => Json(service.GetBlocked())).WithName("Connections_Get_4");
    }
}
