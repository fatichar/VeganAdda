using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;

namespace VeganAdda.Web.Features.Shared;

public static class EndpointSupport
{
    internal const string BodyKey = "VeganAdda.ApiBody";
    public static string Q(HttpContext ctx, string name) => ctx.Request.Query[name].ToString();
    public static int Page(HttpContext ctx, string name = "page") => int.TryParse(Q(ctx, name), out var n) ? Math.Clamp(n, 1, 10000) : 1;
    public static JsonElement? RawBody(HttpContext ctx) => ctx.Items.TryGetValue(BodyKey, out var b) ? (JsonElement?)b : null;
    public static T Body<T>(HttpContext ctx) where T : class => RawBody(ctx)?.Deserialize<T>(Wire.Json) ?? throw new ApiException(400, "Please check the form and try again.");
    public static async Task<IResult> Json<T>(Task<T> operation) => Results.Json(await operation, Wire.Json);
}

// Applied to explicit feature routes, including prerender-compatible authorization rules.
public sealed class FeatureRequestFilter(IAntiforgery csrf, FeatureContext context) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var ctx = invocation.HttpContext;
        if (context.Actor.Id is { } id && !await context.Db.Accounts.AnyAsync(a => a.Id == id && !a.Suspended, ctx.RequestAborted))
            throw new ApiException(403, "Your account is suspended. Contact the community team for help.");
        if (ctx.Request.Method != "GET")
        {
            await csrf.ValidateRequestAsync(ctx);
            var bytes = new byte[32001];
            var length = 0;
            while (length < bytes.Length)
            {
                var count = await ctx.Request.Body.ReadAsync(bytes.AsMemory(length), ctx.RequestAborted);
                if (count == 0) break;
                length += count;
            }
            if (length > 32000) throw new ApiException(413, "This request is too large.");
            if (length > 0)
            {
                try { ctx.Items[EndpointSupport.BodyKey] = JsonSerializer.Deserialize<JsonElement>(bytes.AsSpan(0, length)); }
                catch (JsonException) { throw new ApiException(400, "Please check your input."); }
            }
        }
        try { return await next(invocation); }
        catch (FormatException) { throw new ApiException(400, "This identifier is invalid."); }
    }
}
