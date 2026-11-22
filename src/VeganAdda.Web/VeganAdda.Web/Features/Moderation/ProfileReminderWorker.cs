using FirebaseAdmin.Auth;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Observability;
using VeganAdda.Web.Services;

namespace VeganAdda.Web.Features.Moderation;

public sealed class ProfileReminderWorker(
    IServiceScopeFactory scopes,
    IConfiguration config,
    IFirebaseUserEmailLookup emailLookup,
    IProfileReminderEmailSender sender,
    LifecycleEvents lifecycle,
    ILogger<ProfileReminderWorker> logger,
    TimeProvider? time = null) : BackgroundService
{
    private const int ParallelSends = 4;
    private const int MaxAttempts = 3;
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly string subjectPrefix = $"firebase:{config["Authentication:Firebase:ProjectId"]}:";

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(Enumerable.Range(0, ParallelSends).Select(_ => RunSlot(stoppingToken)));

    private async Task RunSlot(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await DeliverNext(stoppingToken)) continue;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Profile reminder queue processing failed: {ErrorType}", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    public async Task<bool> DeliverNext(CancellationToken stoppingToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunityDb>();
        var claim = await ClaimNext(db, stoppingToken);
        if (claim == null) return false;

        var status = claim.Expired
            ? ProfileReminderStatus.Unconfirmed
            : await Deliver(db, claim, stoppingToken);
        var now = clock.GetUtcNow();
        var next = now.AddSeconds(claim.Attempt == 1 ? 5 : 20);
        // Finish a known outcome even during a graceful shutdown.
        using var writeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = writeTimeout.Token;
        await FinishAttempt(db, claim, status, now, next, ct);
        return true;
    }

    private sealed record Claim(Guid BatchId, Guid MemberId, Guid LeaseId, int Attempt, bool Expired);

    private async Task<Claim?> ClaimNext(CommunityDb db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var delivery = (await db.ProfileReminderDeliveries.FromSqlInterpolated($"""
            SELECT * FROM "ProfileReminderDeliveries"
            WHERE "Status" = {(int)ProfileReminderStatus.Queued} AND "AvailableAt" <= {now}
            ORDER BY "AvailableAt", "BatchId", "MemberId" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct)).SingleOrDefault();
        if (delivery == null) return null;
        var expired = delivery.LeaseId != null;
        // A previous process could have handed the message to SMTP before exiting.
        if (!expired) delivery.Attempts++;
        var leaseId = Guid.NewGuid();
        delivery.LeaseId = leaseId;
        delivery.AvailableAt = now.AddMinutes(2);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(delivery.BatchId, delivery.MemberId, leaseId, delivery.Attempts, expired);
    }

    private async Task<ProfileReminderStatus> Deliver(CommunityDb db, Claim claim, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var ct = timeout.Token;
        var sending = false;
        try
        {
            var account = await db.Accounts.AsNoTracking().Where(a => a.Id == claim.MemberId && !a.Suspended
                    && !db.Profiles.Where(BasicProfile.Complete).Any(p => p.Id == a.Id)
                    && db.ProfileReminderBatches.Any(b => b.Id == claim.BatchId
                        && db.Accounts.Any(admin => admin.Id == b.AdminId && admin.Admin && !admin.Suspended)))
                .Select(a => new { a.Subject, a.Name }).SingleOrDefaultAsync(ct);
            if (account == null || !account.Subject.StartsWith(subjectPrefix, StringComparison.Ordinal)
                || account.Subject.Length == subjectPrefix.Length)
                return ProfileReminderStatus.Skipped;
            var email = await emailLookup.GetVerifiedEmail(account.Subject[subjectPrefix.Length..], ct);
            if (string.IsNullOrWhiteSpace(email)) return ProfileReminderStatus.Skipped;
            ct.ThrowIfCancellationRequested();
            sending = true;
            await sender.Send(email, account.Name, ct);
            return ProfileReminderStatus.Sent;
        }
        catch (FirebaseAuthException ex) when (!sending && ex.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            return ProfileReminderStatus.Skipped;
        }
        catch (OperationCanceledException) when (sending && ct.IsCancellationRequested)
        {
            return ProfileReminderStatus.Unconfirmed;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Profile reminder attempt {Attempt} failed for batch {BatchId}, error type {ErrorType}.",
                claim.Attempt, claim.BatchId, ex.GetType().Name);
            return claim.Attempt < MaxAttempts ? ProfileReminderStatus.Queued : ProfileReminderStatus.Failed;
        }
    }

    private async Task FinishAttempt(CommunityDb db, Claim claim, ProfileReminderStatus status,
        DateTimeOffset now, DateTimeOffset next, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize result writers within a batch so the last writer can complete it atomically.
        var batch = (await db.ProfileReminderBatches.FromSqlInterpolated($"""
            SELECT * FROM "ProfileReminderBatches" WHERE "Id" = {claim.BatchId} FOR UPDATE
            """).ToListAsync(ct)).SingleOrDefault();
        if (batch == null) return;

        var deliveries = db.ProfileReminderDeliveries.Where(d => d.BatchId == batch.Id);
        var updated = await deliveries.Where(d => d.MemberId == claim.MemberId && d.LeaseId == claim.LeaseId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.AvailableAt, next)
                .SetProperty(d => d.LeaseId, (Guid?)null), ct);
        if (updated == 0) return;
        if (status == ProfileReminderStatus.Queued || await deliveries.AnyAsync(d => d.Status == ProfileReminderStatus.Queued, ct))
        {
            await transaction.CommitAsync(ct);
            return;
        }

        var counts = await deliveries.GroupBy(d => d.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Status, group => group.Count, ct);
        batch.Completed = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        lifecycle.RecordProfileReminders(batch.AdminId, counts.GetValueOrDefault(ProfileReminderStatus.Sent),
            counts.GetValueOrDefault(ProfileReminderStatus.Skipped), counts.GetValueOrDefault(ProfileReminderStatus.Failed),
            counts.GetValueOrDefault(ProfileReminderStatus.Unconfirmed));
    }
}
