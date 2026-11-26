using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Moderation;

public static class ModerationEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/admin", (HttpContext ctx, ModerationService service) => Json(service.Admin(Q(ctx, "verification"), Q(ctx, "section") is { Length: > 0 } s ? s : "Members", Page(ctx), Q(ctx, "country"), Q(ctx, "state"), Q(ctx, "city"), ctx.Request.Query.TryGetValue("pageSize", out var size) ? size.ToString() : null, Q(ctx, "memberSort")))).WithName("Moderation_Get_0").RequireAuthorization();
        api.MapGet("/admin/members/{id}", (string id, ModerationService service) => Json(service.AdminMemberProfile(Guid.Parse(id)))).WithName("Moderation_Get_1").RequireAuthorization();
        api.MapGet("/moderation", (HttpContext ctx, ModerationService service) => Json(service.Moderation(Q(ctx, "section") is { Length: > 0 } s ? s : "Profiles", Page(ctx)))).WithName("Moderation_Get_2").RequireAuthorization();
        api.MapPost("/admin/profile-review", (HttpContext ctx, ModerationService service) => Json(service.ReviewProfile(Body<VeganVerificationInput>(ctx)))).WithName("Moderation_Post_3").RequireAuthorization();
        api.MapPost("/admin/profile-feedback", (HttpContext ctx, ModerationService service) => Json(service.RequestProfileChanges(Body<ProfileFeedbackInput>(ctx)))).WithName("Moderation_RequestProfileChanges").RequireAuthorization();
        api.MapPost("/moderation/profile-review", (HttpContext ctx, ModerationService service) => Json(service.ReviewProfile(Body<VeganVerificationInput>(ctx), moderator: true))).WithName("Moderation_Post_4").RequireAuthorization();
        api.MapPost("/admin/moderate", (HttpContext ctx, ModerationService service) => Json(service.Moderate(Body<ModerateInput>(ctx)))).WithName("Moderation_Post_5").RequireAuthorization();
        api.MapPost("/admin/profile-reminders", async (HttpContext ctx, ProfileReminderService service) =>
        {
            var receipt = await service.Queue(Body<ProfileReminderInput>(ctx));
            return Results.Accepted($"/api/admin/profile-reminders/{receipt.BatchId}", receipt);
        }).WithName("Moderation_QueueProfileReminders").RequireAuthorization();
        api.MapGet("/admin/profile-reminders", (ProfileReminderService service) => Json(service.Recent()))
            .WithName("Moderation_RecentProfileReminders").RequireAuthorization();
        api.MapGet("/admin/profile-reminders/{id:guid}", (Guid id, ProfileReminderService service) => Json(service.Get(id)))
            .WithName("Moderation_GetProfileReminders").RequireAuthorization();
        api.MapPost("/admin/profile-reminders/{id:guid}/retry", async (Guid id, HttpContext ctx, ProfileReminderService service) =>
        {
            var receipt = await service.Retry(id, Body<ProfileReminderRetry>(ctx));
            return Results.Accepted($"/api/admin/profile-reminders/{id}", receipt);
        }).WithName("Moderation_RetryProfileReminders").RequireAuthorization();
        api.MapPost("/admin/members/bulk-action", (HttpContext ctx, ModerationService service) => Json(service.BulkMemberAction(Body<BulkMemberActionInput>(ctx), ctx.RequestAborted))).WithName("Moderation_Post_6").RequireAuthorization();
    }
}
