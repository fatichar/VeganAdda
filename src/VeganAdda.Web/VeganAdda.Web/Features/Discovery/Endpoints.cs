using Microsoft.AspNetCore.Authentication;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Shared.EndpointSupport;

namespace VeganAdda.Web.Features.Discovery;

public static class DiscoveryEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/people", (HttpContext ctx, DiscoveryService service) => Json(service.SearchPeople(new DiscoveryInput { Event = Q(ctx, "event"), CheckedIn = Q(ctx, "checkedIn") == "true", Country = Q(ctx, "country"), State = Q(ctx, "state"), City = Q(ctx, "city"), Search = Q(ctx, "search"), Interest = Q(ctx, "interest"), Intent = Q(ctx, "intent"), VerifiedVegansOnly = Q(ctx, "verifiedVegansOnly") == "true", IncludeNonVegan = Q(ctx, "includeNonVegan") == "true", MinAge = BirthDetails.ParseAge(Q(ctx, "minAge")), MaxAge = BirthDetails.ParseAge(Q(ctx, "maxAge")), Page = Page(ctx) }))).WithName("Discovery_Get_0").AllowAnonymous();
        api.MapPost("/people/search", (HttpContext ctx, DiscoveryService service) => Json(service.SearchPeople(Body<DiscoveryInput>(ctx)))).WithName("Discovery_Post_1").AllowAnonymous();
    }
}
