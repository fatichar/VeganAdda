using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Notifications;

public static class NotificationsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/notifications", (HttpContext ctx, NotificationsService service) => Json(service.GetNotifications(Page(ctx)))).WithName("Notifications_Get_0").RequireAuthorization();
        api.MapGet("/unread", (NotificationsService service) => Json(service.Unread())).WithName("Notifications_Get_1").RequireAuthorization();
        api.MapPost("/notifications/read", (HttpContext ctx, NotificationsService service) => Json(service.NotificationAction("POST", ["notifications", "read"], RawBody(ctx)))).WithName("Notifications_Post_2").RequireAuthorization();
        api.MapPost("/notifications/{id}/read", (string id, HttpContext ctx, NotificationsService service) => Json(service.NotificationAction("POST", ["notifications", id, "read"], RawBody(ctx)))).WithName("Notifications_Post_3").RequireAuthorization();
        api.MapDelete("/notifications", (HttpContext ctx, NotificationsService service) => Json(service.NotificationAction("DELETE", ["notifications"], RawBody(ctx)))).WithName("Notifications_Delete_4").RequireAuthorization();
    }
}
