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
public sealed class ChatPushWorker(IServiceScopeFactory scopes, IConfiguration config, IChatPushSender sender, ILogger<ChatPushWorker> logger, TimeProvider? time = null) : BackgroundService
{
    private TimeProvider Clock { get; } = time ?? TimeProvider.System;

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
    private sealed record Claim(long Id, string Conversation, Guid SenderId, DateTimeOffset SentAt, int Attempt, Guid Lease);

    public async Task DeliverPending(CancellationToken stoppingToken = default)
    {
        // Claim one message at a time so a slow earlier delivery cannot exhaust a
        // later message's lease before processing even starts.
        for (var i = 0; i < 20; i++)
        {
            var claim = await ClaimNext(stoppingToken);
            if (claim == null) return;
            await Deliver(claim, stoppingToken);
        }
    }

    private async Task<Claim?> ClaimNext(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunityDb>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = Clock.GetUtcNow();
        var message = (await db.Messages.FromSqlInterpolated($"""
            SELECT * FROM "Messages"
            WHERE "PushPending" = TRUE AND ("PushLeaseUntil" IS NULL OR "PushLeaseUntil" <= {now})
            ORDER BY "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct)).SingleOrDefault();
        if (message == null) return null;
        if (message.PushAttempts >= 4)
        {
            // A crash can leave the final attempt leased and pending. Recover it
            // without performing a fifth delivery after the lease expires.
            message.PushPending = false;
            message.PushLeaseId = null;
            message.PushLeaseUntil = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        var lease = Guid.NewGuid();
        message.PushLeaseId = lease;
        message.PushLeaseUntil = now.AddMinutes(2);
        message.PushAttempts++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(message.Id, message.Conversation, message.SenderId, message.SentAt, message.PushAttempts, lease);
    }

    private async Task Deliver(Claim claim, CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunityDb>();
        var retry = false;
        using var deliveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deliveryTimeout.CancelAfter(TimeSpan.FromSeconds(45)); // Always shorter than the lease.
        var ct = deliveryTimeout.Token;
        try
        {
            var relationship = await db.Relationships.FindAsync([claim.Conversation], ct);
            if (relationship != null && relationship.State == "accepted" && claim.SentAt >= Clock.GetUtcNow().AddHours(-1))
            {
                var recipient = relationship.A == claim.SenderId ? relationship.B : relationship.A;
                var through = relationship.A == recipient ? relationship.ReadA : relationship.ReadB;
                var suppressed = through >= claim.Id
                    || await db.Accounts.AnyAsync(a => (a.Id == recipient || a.Id == claim.SenderId) && a.Suspended, ct)
                    || await db.Blocks.AnyAsync(b => (b.Owner == recipient && b.Subject == claim.SenderId) || (b.Owner == claim.SenderId && b.Subject == recipient), ct);
                if (!suppressed)
                {
                    var delivered = await db.PushDeliveries.Where(d => d.MessageId == claim.Id).Select(d => d.DeviceId).ToArrayAsync(ct);
                    var devices = await db.PushDevices.Where(d => d.UserId == recipient && d.Updated > Clock.GetUtcNow().AddDays(-60) && !delivered.Contains(d.Id)).ToListAsync(ct);
                    foreach (var device in devices)
                    {
                        try
                        {
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            timeout.CancelAfter(TimeSpan.FromSeconds(10));
                            await sender.Send(device.Token, claim.Id, recipient, timeout.Token);
                            // No database transaction/row lock is held across Firebase I/O.
                            // A stable message ID also lets clients deduplicate crash-window redelivery.
                            await db.Database.ExecuteSqlInterpolatedAsync($"""
                                INSERT INTO "PushDeliveries" ("MessageId", "DeviceId")
                                VALUES ({claim.Id}, {device.Id}) ON CONFLICT DO NOTHING
                                """, ct);
                        }
                        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
                        {
                            await db.PushDevices.Where(d => d.Id == device.Id).ExecuteDeleteAsync(ct);
                        }
                        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                        {
                            logger.LogWarning("Chat push failed: {Type}", ex.GetType().Name);
                            retry = true;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Chat push delivery failed: {Type}", ex.GetType().Name);
            retry = true;
        }
        // On shutdown the lease deliberately remains recoverable by another worker.
        stoppingToken.ThrowIfCancellationRequested();
        await db.Messages.Where(m => m.Id == claim.Id && m.PushLeaseId == claim.Lease)
            .ExecuteUpdateAsync(update => update
                .SetProperty(m => m.PushPending, retry && claim.Attempt < 4)
                .SetProperty(m => m.PushLeaseId, (Guid?)null)
                .SetProperty(m => m.PushLeaseUntil, retry && claim.Attempt < 4 ? (DateTimeOffset?)Clock.GetUtcNow().AddSeconds(5) : null), stoppingToken);
    }
}
