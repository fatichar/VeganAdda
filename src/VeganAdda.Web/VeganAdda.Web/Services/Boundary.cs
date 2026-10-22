using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Services;

public interface IActor { Guid? Id { get; } }
public class HttpActor(IHttpContextAccessor context) : IActor
{
    public Guid? Id => Guid.TryParse(context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
public class ServerApi(IServiceScopeFactory scopes) : ICommunityApi
{
    public Task<T> Get<T>(string path) => Execute<T>("GET", path, null);
    public Task<T> Send<T>(string path, object? body = null, string method = "POST") => Execute<T>(method, path, JsonSerializer.SerializeToElement(body, Wire.Json));
    private async Task<T> Execute<T>(string method, string path, JsonElement? body)
    {
        // Layout and page prerenders overlap. Each operation needs its own scoped DbContext.
        await using var scope = scopes.CreateAsyncScope();
        return Convert<T>(await scope.ServiceProvider.GetRequiredService<CommunityService>().Execute(method, path, body));
    }
    private static T Convert<T>(object data) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(data, Wire.Json), Wire.Json)!;
}
public class ServerPlatform : IPlatform
{
    public event Action? Changed { add { } remove { } }
    public void SignIn(string returnTo) { }
    public Task SignInWithGoogle(string returnTo) => Task.CompletedTask;
    public Task ClearFirebaseSession() => Task.CompletedTask;
    public Task StartRealtime() => Task.CompletedTask;
    public Task<string> Upload(Stream stream, string filename, string contentType, bool reportScreenshot = false) => throw new InvalidOperationException("Uploads require an interactive client.");
}
public class CommunityHub(CommunityDb db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var id) || (!await db.Accounts.AnyAsync(x => x.Id == id && !x.Suspended) || !await db.Profiles.Where(BasicProfile.Complete).AnyAsync(p => p.Id == id)))
        { Context.Abort(); return; }
        await base.OnConnectedAsync();
    }
    // No conversation join or message method: all access and writes go through the authorized API.
    // Events contain only invalidation, never names, content, identifiers or message bodies.
}
public interface IChangeBus
{
    Task Changed(params Guid[] users);
    Task Changed(ChangeArea area, params Guid[] users) => Changed(users);
}
public class ChangeBus(IHubContext<CommunityHub> hub, ILogger<ChangeBus> log) : IChangeBus
{
    public Task Changed(params Guid[] users) => Changed(ChangeArea.All, users);
    public async Task Changed(ChangeArea area, params Guid[] users)
    {
        try { await hub.Clients.Users(users.Select(x => x.ToString()).ToList()).SendAsync("Changed", area.ToString()); }
        catch (Exception ex) { log.LogWarning("Realtime invalidation failed: {Type}", ex.GetType().Name); }
    }
}
