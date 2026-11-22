using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Moderation;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Observability;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

[Collection("Postgres")]
public sealed class ProfileReminderQueueTests(DatabaseFixture fixture)
{
    private static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Authentication:Firebase:ProjectId"] = "reminder-tests" }).Build();

    [Fact]
    public async Task Sends_in_parallel_retries_twice_and_manual_retry_only_queues_failures()
    {
        var admin = await fixture.Member(admin: true);
        var otherAdmin = await fixture.Member(admin: true);
        var sent = await AddRecipient("sent");
        var failed = await AddRecipient("failed");
        var skipped = await fixture.Member();
        var sender = new Sender();
        var clock = new Clock();
        using var scopes = new ServiceCollection().AddScoped(_ => fixture.Db()).BuildServiceProvider();
        ProfileReminderWorker NewWorker() => new(scopes.GetRequiredService<IServiceScopeFactory>(), Config, new EmailLookup(), sender,
            new LifecycleEvents(NullLogger<LifecycleEvents>.Instance), NullLogger<ProfileReminderWorker>.Instance, clock);
        var worker = NewWorker();
        var input = new ProfileReminderInput(Guid.NewGuid(), [sent, failed, skipped, sent]);

        var queued = await Call(admin, sender, service => service.Queue(input));
        Assert.Equal(queued, await Call(admin, sender, service => service.Queue(input)));
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Call(admin, sender,
            service => service.Queue(input with { MemberIds = [sent] })))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Call(otherAdmin, sender,
            service => service.Queue(new(Guid.NewGuid(), [sent]))))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => Call(otherAdmin, sender,
            service => service.Get(queued.BatchId)))).Status);

        var initial = await Call(admin, sender, service => service.Get(queued.BatchId));
        Assert.Equal(3, initial.Members.Count);
        Assert.All(initial.Members, member => Assert.Equal(ProfileReminderStatus.Queued, member.Status));
        Assert.Empty(sender.Attempts);
        Assert.False(Assert.Single(await Call(admin, sender, service => service.Recent())).Complete);

        var firstAttempts = Task.WhenAll(worker.DeliverNext(), worker.DeliverNext(), worker.DeliverNext());
        try { await sender.ParallelSends.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { sender.Release.TrySetResult(); }
        Assert.All(await firstAttempts, processed => Assert.True(processed));
        Assert.False(await worker.DeliverNext());

        // A fresh worker resumes the persisted retry schedule after a restart.
        clock.Advance(TimeSpan.FromSeconds(6));
        worker = NewWorker();
        Assert.True(await worker.DeliverNext());
        Assert.False(await worker.DeliverNext());
        clock.Advance(TimeSpan.FromSeconds(21));
        Assert.True(await worker.DeliverNext());
        Assert.False(await worker.DeliverNext());

        var complete = await Call(admin, sender, service => service.Get(queued.BatchId));
        Assert.True(complete.Complete);
        Assert.Equal(ProfileReminderStatus.Sent, Status(complete, sent));
        Assert.Equal(ProfileReminderStatus.Failed, Status(complete, failed));
        Assert.Equal(ProfileReminderStatus.Skipped, Status(complete, skipped));
        Assert.Equal(1, sender.Attempts["sent@example.test"]);
        Assert.Equal(3, sender.Attempts["failed@example.test"]);
        Assert.Equal(2, sender.Attempts.Count);

        sender.AllowFailed = true;
        var retry = new ProfileReminderRetry(complete.RetryVersion);
        Assert.Equal(queued, await Call(admin, sender, service => service.Retry(queued.BatchId, retry)));
        Assert.Equal(queued, await Call(admin, sender, service => service.Retry(queued.BatchId, retry)));
        Assert.True(await worker.DeliverNext());
        Assert.False(await worker.DeliverNext());
        var retried = await Call(admin, sender, service => service.Get(queued.BatchId));
        Assert.True(retried.Complete);
        Assert.Equal(1, retried.RetryVersion);
        Assert.Equal(ProfileReminderStatus.Sent, Status(retried, failed));
        Assert.Equal(1, sender.Attempts["sent@example.test"]);
        Assert.Equal(4, sender.Attempts["failed@example.test"]);
        Assert.Equal(2, sender.Attempts.Count);
    }

    [Fact]
    public async Task Expired_in_flight_delivery_is_unconfirmed_and_is_not_automatically_resent()
    {
        var admin = await fixture.Member(admin: true);
        var member = await AddRecipient("interrupted");
        var sender = new Sender();
        var receipt = await Call(admin, sender, service => service.Queue(new(Guid.NewGuid(), [member])));
        await using (var db = fixture.Db())
        {
            var row = await db.ProfileReminderDeliveries.SingleAsync(d => d.BatchId == receipt.BatchId);
            row.LeaseId = Guid.NewGuid();
            row.Attempts = 1;
            row.AvailableAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var scopes = new ServiceCollection().AddScoped(_ => fixture.Db()).BuildServiceProvider();
        var worker = new ProfileReminderWorker(scopes.GetRequiredService<IServiceScopeFactory>(), Config, new EmailLookup(), sender,
            new LifecycleEvents(NullLogger<LifecycleEvents>.Instance), NullLogger<ProfileReminderWorker>.Instance);
        Assert.True(await worker.DeliverNext());
        Assert.Empty(sender.Attempts);
        var result = await Call(admin, sender, service => service.Get(receipt.BatchId));
        Assert.True(result.Complete);
        Assert.Equal(ProfileReminderStatus.Unconfirmed, Status(result, member));
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Call(admin, sender,
            service => service.Retry(receipt.BatchId, new(result.RetryVersion))))).Status);
    }

    private async Task<Guid> AddRecipient(string uid)
    {
        await using var db = fixture.Db();
        var account = new Account { Subject = $"firebase:reminder-tests:{uid}-{Guid.NewGuid():N}", Name = uid };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task<T> Call<T>(Guid actor, Sender sender, Func<ProfileReminderService, Task<T>> operation)
    {
        await using var db = fixture.Db();
        return await operation(new(new FeatureContext(db, new Actor(actor), new ChangeBus(), Config, null!), sender));
    }

    private static ProfileReminderStatus Status(ProfileReminderBatchResult result, Guid id)
        => Assert.Single(result.Members, member => member.MemberId == id).Status;

    private sealed class Actor(Guid id) : IActor { public Guid? Id => id; }
    private sealed class ChangeBus : IChangeBus { public Task Changed(params Guid[] users) => Task.CompletedTask; }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow.AddMinutes(1);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class EmailLookup : IFirebaseUserEmailLookup
    {
        public Task<string?> GetVerifiedEmail(string uid, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(uid.Split('-')[0] + "@example.test");
    }

    private sealed class Sender : IProfileReminderEmailSender
    {
        public bool IsConfigured => true;
        public bool AllowFailed { get; set; }
        public ConcurrentDictionary<string, int> Attempts { get; } = new();
        public TaskCompletionSource ParallelSends { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task Send(string recipient, string name, CancellationToken cancellationToken = default)
        {
            Attempts.AddOrUpdate(recipient, 1, (_, count) => count + 1);
            if (Attempts.Count == 2) ParallelSends.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (!AllowFailed && recipient == "failed@example.test")
                throw new System.Net.Mail.SmtpException("Simulated transient SMTP failure.");
        }
    }
}
