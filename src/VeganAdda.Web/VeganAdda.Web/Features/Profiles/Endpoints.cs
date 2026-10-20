using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Profiles;

public static class ProfilesEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/people/{id}", (string id, ProfilesService service) => Json(service.GetPerson(Guid.Parse(id)))).WithName("Profiles_Get_0").AllowAnonymous();
        api.MapGet("/profile", (ProfilesService service) => Json(service.GetProfile())).WithName("Profiles_Get_1").RequireAuthorization();
        api.MapPost("/profile", (HttpContext ctx, ProfilesService service) => Json(service.SaveProfile(Body<ProfileInput>(ctx)))).WithName("Profiles_Post_2").RequireAuthorization();
        api.MapDelete("/account", async (HttpContext ctx, ProfilesService service) => { if (Body<DeleteInput>(ctx).Confirm != "DELETE") throw new ApiException(400, "Confirm permanent deletion by typing DELETE."); var result = await service.DeleteAccount(); await ctx.SignOutAsync(); return Results.Json(result, Wire.Json); }).WithName("Profiles_Delete_3").RequireAuthorization();
    }
}
