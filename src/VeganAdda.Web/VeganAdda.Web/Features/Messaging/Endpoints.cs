using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Messaging;

public static class MessagingEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/inbox", (HttpContext ctx, InboxService service) => Json(service.GetInbox(Page(ctx, "chatPage"), Page(ctx, "requestPage"), Q(ctx, "key") is { Length: > 0 } key ? key : null))).RequireAuthorization();

        api.MapGet("/conversations", (MessagingService service) => Json(service.Conversations())).WithName("Messaging_Get_0").RequireAuthorization();
        api.MapGet("/conversations/{key}/messages", (string key, HttpContext ctx, MessagingService service) => Json(service.Messages(key, long.TryParse(Q(ctx, "before"), out var before) ? before : long.MaxValue, long.TryParse(Q(ctx, "after"), out var after) ? after : null))).WithName("Messaging_Get_1").RequireAuthorization();
        api.MapPost("/conversations/{key}/messages", (string key, HttpContext ctx, MessagingService service) => Json(service.SendMessage(key, Body<MessageInput>(ctx)))).WithName("Messaging_Post_2").RequireAuthorization();
        api.MapPost("/conversations/{key}/read", (string key, HttpContext ctx, MessagingService service) => Json(service.MarkRead(key, Body<ReadInput>(ctx)))).WithName("Messaging_Post_3").RequireAuthorization();
    }
}
