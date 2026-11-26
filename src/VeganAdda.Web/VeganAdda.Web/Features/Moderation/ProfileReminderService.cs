using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Services;

namespace VeganAdda.Web.Features.Moderation;

public sealed class ProfileReminderService(FeatureContext context, IProfileReminderEmailSender sender) : FeatureService(context)
{
    public async Task<ProfileReminderReceipt> Queue(ProfileReminderInput input)
    {
        var admin = await User(admin: true);
        if (input.RequestId == Guid.Empty || input.MemberIds is null || input.MemberIds.Count is < 1 or > 100
            || input.MemberIds.Contains(Guid.Empty))
            throw new ApiException(400, "Select between 1 and 100 members.");
        var members = input.MemberIds.Distinct().ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync();
        await PairLock("profile-reminder-queue");
        var existing = await db.ProfileReminderBatches.Where(b => b.AdminId == admin.Id && b.RequestId == input.RequestId)
            .Select(b => new { b.Id, Members = b.Deliveries.Select(d => d.MemberId).ToList() }).SingleOrDefaultAsync();
        if (existing != null)
        {
            if (!members.ToHashSet().SetEquals(existing.Members))
                throw new ApiException(409, "This reminder request was already used for a different selection.");
            return new(existing.Id);
        }

        if (await db.ProfileReminderBatches.AnyAsync(b => b.AdminId == admin.Id && b.Completed == null))
            throw new ApiException(409, "Wait for your current reminder batch to finish.");
        await EnsureCanQueue(members);

        var now = DateTimeOffset.UtcNow;
        var batch = new ProfileReminderBatch
        {
            AdminId = admin.Id,
            RequestId = input.RequestId,
            Created = now,
            Deliveries = members.Select(id => new ProfileReminderDelivery { MemberId = id, AvailableAt = now }).ToList()
        };
        db.ProfileReminderBatches.Add(batch);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(batch.Id);
    }

    public async Task<List<ProfileReminderBatchSummary>> Recent()
    {
        var admin = await User(admin: true);
        return await db.ProfileReminderBatches.AsNoTracking().Where(b => b.AdminId == admin.Id)
            .OrderBy(b => b.Completed != null).ThenByDescending(b => b.Created).ThenByDescending(b => b.Id).Take(10)
            .Select(b => new ProfileReminderBatchSummary(b.Id, b.Created, b.Completed != null)).ToListAsync();
    }

    public async Task<ProfileReminderBatchResult> Get(Guid id)
    {
        var admin = await User(admin: true);
        return await db.ProfileReminderBatches.AsNoTracking().Where(b => b.Id == id && b.AdminId == admin.Id)
            .Select(b => new ProfileReminderBatchResult(b.Id, b.RetryVersion, b.Completed != null,
                b.Deliveries.Select(d => new
                {
                    d.MemberId, d.Status,
                    Name = db.Accounts.Where(a => a.Id == d.MemberId).Select(a => a.Name).FirstOrDefault()
                })
                .OrderBy(d => d.Name).ThenBy(d => d.MemberId)
                .Select(d => new ProfileReminderMemberResult(d.MemberId, d.Name ?? "Account unavailable", d.Status)).ToList()))
            .SingleOrDefaultAsync() ?? throw Missing();
    }

    public async Task<ProfileReminderReceipt> Retry(Guid id, ProfileReminderRetry input)
    {
        var admin = await User(admin: true);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await PairLock("profile-reminder-queue");
        var batch = (await db.ProfileReminderBatches.FromSqlInterpolated($"""
            SELECT * FROM "ProfileReminderBatches" WHERE "Id" = {id} AND "AdminId" = {admin.Id} FOR UPDATE
            """).ToListAsync()).SingleOrDefault() ?? throw Missing();
        if (input.RetryVersion < 0 || input.RetryVersion > batch.RetryVersion)
            throw new ApiException(400, "Refresh the reminder results and try again.");
        if (input.RetryVersion < batch.RetryVersion) return new(batch.Id);
        if (await db.ProfileReminderBatches.AnyAsync(b => b.AdminId == admin.Id && b.Completed == null))
            throw new ApiException(409, "Wait for your current reminder batch to finish.");

        var failed = db.ProfileReminderDeliveries.Where(d => d.BatchId == id && d.Status == ProfileReminderStatus.Failed);
        var members = await failed.Select(d => d.MemberId).ToArrayAsync();
        if (members.Length == 0) throw new ApiException(409, "There are no failed reminders to retry.");
        await EnsureCanQueue(members);
        var now = DateTimeOffset.UtcNow;
        await failed.ExecuteUpdateAsync(update => update
            .SetProperty(d => d.Status, ProfileReminderStatus.Queued)
            .SetProperty(d => d.Attempts, 0)
            .SetProperty(d => d.AvailableAt, now));
        batch.RetryVersion++;
        batch.Completed = null;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(batch.Id);
    }

    private async Task EnsureCanQueue(Guid[] members)
    {
        if (!sender.IsConfigured) throw new ApiException(503, "Profile reminder email delivery is not configured.");
        if (await db.ProfileReminderDeliveries.AnyAsync(d => members.Contains(d.MemberId) && d.Status == ProfileReminderStatus.Queued))
            throw new ApiException(409, "Some selected members already have a reminder queued.");
    }
}
