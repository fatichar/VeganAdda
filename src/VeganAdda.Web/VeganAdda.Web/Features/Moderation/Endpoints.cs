using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Moderation;

public static class ModerationEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/admin", (HttpContext ctx, ModerationService service) => Json(service.Admin(Q(ctx, "approval"), Q(ctx, "section") is { Length: > 0 } s ? s : "Members", Page(ctx)))).WithName("Moderation_Get_0");
        api.MapGet("/admin/members/{id}", (string id, ModerationService service) => Json(service.AdminMemberProfile(Guid.Parse(id)))).WithName("Moderation_Get_1");
        api.MapGet("/moderation", (HttpContext ctx, ModerationService service) => Json(service.Moderation(Q(ctx, "section") is { Length: > 0 } s ? s : "Profiles", Page(ctx)))).WithName("Moderation_Get_2");
        api.MapPost("/admin/profile-review", (HttpContext ctx, ModerationService service) => Json(service.ReviewProfile(Body<ProfileReviewInput>(ctx)))).WithName("Moderation_Post_3");
        api.MapPost("/moderation/profile-review", (HttpContext ctx, ModerationService service) => Json(service.ReviewProfile(Body<ProfileReviewInput>(ctx), moderator: true))).WithName("Moderation_Post_4");
        api.MapPost("/admin/moderate", (HttpContext ctx, ModerationService service) => Json(service.Moderate(Body<ModerateInput>(ctx)))).WithName("Moderation_Post_5");
    }
}
