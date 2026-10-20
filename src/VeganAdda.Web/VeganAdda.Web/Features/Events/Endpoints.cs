using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Events;

public static class EventsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/events", (HttpContext ctx, EventsService service) => Json(service.ListEvents(Q(ctx, "city"), Page(ctx), Q(ctx, "includePast") == "true", Q(ctx, "country"), Q(ctx, "state")))).WithName("Events_Get_0").AllowAnonymous();
        api.MapGet("/events/{id}", (string id, EventsService service) => Json(service.GetEvent(Guid.Parse(id)))).WithName("Events_Get_1").AllowAnonymous();
        api.MapPost("/events/{id}/{action}", (string id, string action, HttpContext ctx, EventsService service) => Json(service.EventAction(Guid.Parse(id), action, RawBody(ctx)))).WithName("Events_Post_2").RequireAuthorization();
        api.MapGet("/event-submissions", (EventsService service) => Json(service.MyEventSubmissions())).WithName("Events_Get_3").RequireAuthorization();
        api.MapPost("/event-submissions", (HttpContext ctx, EventsService service) => Json(service.SaveEventSubmission(Body<EventInput>(ctx)))).WithName("Events_Post_4").RequireAuthorization();
        api.MapPost("/admin/events", (HttpContext ctx, EventsService service) => Json(service.SaveEvent(Body<EventInput>(ctx)))).WithName("Events_Post_5").RequireAuthorization();
        api.MapPost("/moderation/event-review", (HttpContext ctx, EventsService service) => Json(service.ReviewEvent(Body<EventReviewInput>(ctx), moderator: true))).WithName("Events_Post_6").RequireAuthorization();
    }
}
