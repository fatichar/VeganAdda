using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Services;

public static class ChatPushEndpoints
{
    public const string DeviceCookie = "VeganAdda.PushDevice";
    public static void MapChatPush(this WebApplication app)
    {
        var config = app.Configuration;
        var sender = config["Push:SenderId"];
        var vapid = config["Push:VapidKey"];
        var enabled = !string.IsNullOrWhiteSpace(sender) && !string.IsNullOrWhiteSpace(vapid);
        object FirebaseConfig() => new { apiKey = config["Authentication:Firebase:ApiKey"], projectId = config["Authentication:Firebase:ProjectId"], appId = config["Authentication:Firebase:AppId"], messagingSenderId = sender };
        app.MapGet("/api/push/config", () => Results.Json(new { enabled, vapidKey = vapid, firebase = FirebaseConfig() }));
        app.MapGet("/firebase-messaging-sw.js", () => Results.Text("self.firebaseConfig=" + JsonSerializer.Serialize(FirebaseConfig()) + ";importScripts('/js/push-worker.js');", "application/javascript"));
        app.MapGet("/api/push/device", async (HttpContext ctx, CommunityDb db) =>
        {
            var id = Guid.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            Guid.TryParse(ctx.Request.Cookies[DeviceCookie], out var device);
            return Results.Json(new { userId = id, enabled = await db.PushDevices.AnyAsync(d => d.Id == device && d.UserId == id) });
        }).RequireAuthorization();
        app.MapMethods("/api/push/device", ["POST", "DELETE"], async (HttpContext ctx, CommunityDb db, IAntiforgery csrf) =>
        {
            await csrf.ValidateRequestAsync(ctx);
            var id = Guid.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            Guid.TryParse(ctx.Request.Cookies[DeviceCookie], out var deviceId);
            if (ctx.Request.Method == "DELETE")
            {
                await db.PushDevices.Where(d => d.Id == deviceId && d.UserId == id).ExecuteDeleteAsync();
                ctx.Response.Cookies.Delete(DeviceCookie);
                return Results.Ok(new Ok());
            }
            if (!enabled) throw new ApiException(503, "Push notifications are not configured yet.");
            var input = await ctx.Request.ReadFromJsonAsync<PushRegistration>();
            var token = CommunityService.Text(input?.Token, 2000, true);
            if (token.Any(c => !char.IsAscii(c) || char.IsWhiteSpace(c))) throw new ApiException(400, "Invalid notification token.");
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"push:" + token}, 0))");
            // A browser token has one owner, including after switching accounts.
            await db.PushDevices.Where(d => d.Id == deviceId || d.Token == token).ExecuteDeleteAsync();
            await db.PushDevices.Where(d => d.UserId == id && d.Updated < DateTimeOffset.UtcNow.AddDays(-60)).ExecuteDeleteAsync();
            if (await db.PushDevices.CountAsync(d => d.UserId == id) >= 20) throw new ApiException(400, "Turn off notifications on an older device first.");
            var device = new PushDevice { UserId = id, Token = token };
            db.PushDevices.Add(device); await db.SaveChangesAsync(); await tx.CommitAsync();
            ctx.Response.Cookies.Append(DeviceCookie, device.Id.ToString(), new CookieOptions { HttpOnly = true, Secure = ctx.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromDays(60) });
            return Results.Ok(new Ok());
        }).RequireAuthorization();
    }
    public sealed record PushRegistration(string Token);
}

// The message and pending flag commit together. Network failures cannot roll back chat.
public interface IChatPushSender
{
    Task Send(string token, long messageId, Guid recipient, CancellationToken ct);
}
public sealed class FirebaseChatPushSender(IConfiguration config) : IChatPushSender
{
    private FirebaseApp? pushApp;
    public async Task Send(string token, long messageId, Guid recipient, CancellationToken ct)
    {
        pushApp ??= FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.GetApplicationDefault(), ProjectId = config["Authentication:Firebase:ProjectId"] }, "chat-push-server");
        await FirebaseMessaging.GetMessaging(pushApp).SendAsync(new FirebaseAdmin.Messaging.Message
        {
            // The pinned web SDK registers via getToken; FID migration is separate.
#pragma warning disable CS0618
            Token = token,
#pragma warning restore CS0618
            Data = new Dictionary<string, string> { ["kind"] = "chat", ["id"] = messageId.ToString(), ["userId"] = recipient.ToString() },
            Webpush = new WebpushConfig { Headers = new Dictionary<string, string> { ["TTL"] = "3600", ["Urgency"] = "high" } }
        }, ct);
    }
}
public sealed class ChatPushWorker(IServiceScopeFactory scopes, IConfiguration config, IChatPushSender sender, ILogger<ChatPushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(config["Push:VapidKey"]) || string.IsNullOrWhiteSpace(config["Push:SenderId"])) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DeliverPending(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning("Chat push batch failed: {Type}", ex.GetType().Name); }
        }
    }
    public async Task DeliverPending(CancellationToken stoppingToken = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunityDb>();
        await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);
        // Avoid two replicas sending the same pending batch.
        var messages = await db.Messages.FromSqlRaw("SELECT * FROM \"Messages\" WHERE \"PushPending\" = TRUE ORDER BY \"Id\" LIMIT 20 FOR UPDATE SKIP LOCKED").ToListAsync(stoppingToken);
        foreach (var message in messages)
        {
            message.PushPending = false;
            var r = await db.Relationships.FindAsync([message.Conversation], stoppingToken);
            if (r == null || r.State != "accepted" || message.SentAt < DateTimeOffset.UtcNow.AddHours(-1)) continue;
            var recipient = r.A == message.SenderId ? r.B : r.A;
            if ((r.A == recipient ? r.ReadA : r.ReadB) >= message.Id
                || await db.Accounts.AnyAsync(a => (a.Id == recipient || a.Id == message.SenderId) && a.Suspended, stoppingToken)
                || await db.Blocks.AnyAsync(b => (b.Owner == recipient && b.Subject == message.SenderId) || (b.Owner == message.SenderId && b.Subject == recipient), stoppingToken)) continue;
            var devices = await db.PushDevices.Where(d => d.UserId == recipient && d.Updated > DateTimeOffset.UtcNow.AddDays(-60)).ToListAsync(stoppingToken);
            foreach (var device in devices)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await sender.Send(device.Token, message.Id, recipient, timeout.Token);
                }
                catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
                { db.PushDevices.Remove(device); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Do not log tokens, credentials or message contents.
                    logger.LogWarning("Chat push failed: {Type}", ex.GetType().Name);
                    message.PushPending = message.PushAttempts < 3;
                }
            }
            message.PushAttempts++;
        }
        await db.SaveChangesAsync(stoppingToken); await tx.CommitAsync(stoppingToken);
    }
}
