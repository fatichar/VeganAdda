using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

public class PushLeaseTests
{
    private sealed class Sender : IChatPushSender
    {
        public readonly List<string> Sent = [];
        public string? FailToken;
        public Func<Task>? Pause;
        public async Task Send(string token, long messageId, Guid recipient, CancellationToken ct)
        {
            if (Pause != null) await Pause().WaitAsync(ct);
            if (token == FailToken) throw new IOException("Temporary provider failure");
            Sent.Add(token);
        }
    }

    private static async Task<(Guid A, Guid B, long Message)> Queue(DatabaseFixture fixture, params string[] tokens)
    {
        var a = await fixture.Member(); var b = await fixture.Member();
        await fixture.Call(a, "connections", new ConnectionInput(b), "POST");
        await fixture.Call(b, "connections/accept", new ConnectionInput(a), "POST");
        await using var db = fixture.Db();
        db.PushDevices.AddRange(tokens.Select(token => new PushDevice { UserId = b, Token = token }));
        await db.SaveChangesAsync();
        var message = Assert.IsType<Message>(await fixture.Call(a, $"conversations/{CommunityService.Pair(a, b)}/messages", new MessageInput(Guid.NewGuid(), "Queued"), "POST"));
        return (a, b, message.Id);
    }

    private static ServiceProvider Services(DatabaseFixture fixture) => new ServiceCollection()
        .AddDbContext<CommunityDb>(options => options.UseNpgsql(fixture.Connection)).BuildServiceProvider();

    private static ChatPushWorker Worker(ServiceProvider services, Sender sender, ManualClock clock)
        => new(services.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), sender, NullLogger<ChatPushWorker>.Instance, clock);

    [Fact]
    public async Task Firebase_IO_holds_no_row_lock_and_another_worker_cannot_claim_the_live_lease()
    {
        var fixture = new DatabaseFixture(); await fixture.InitializeAsync();
        try
        {
            var queued = await Queue(fixture, "device");
            using var services = Services(fixture);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sender = new Sender { Pause = () => { entered.TrySetResult(); return resume.Task; } };
            var clock = new ManualClock();
            var running = Worker(services, sender, clock).DeliverPending();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await using var db = fixture.Db();
                await using (var tx = await db.Database.BeginTransactionAsync())
                {
                    Assert.Single(await db.Messages.FromSqlInterpolated($"SELECT * FROM \"Messages\" WHERE \"Id\" = {queued.Message} FOR UPDATE NOWAIT").ToListAsync());
                    await tx.RollbackAsync();
                }
                var otherSender = new Sender();
                await Worker(services, otherSender, clock).DeliverPending();
                Assert.Empty(otherSender.Sent);
            }
            finally { resume.TrySetResult(); await running; }
            Assert.Single(sender.Sent);
            await using var completed = fixture.Db();
            var message = await completed.Messages.FindAsync(queued.Message);
            Assert.False(message!.PushPending); Assert.Null(message.PushLeaseId);
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Partial_retries_wait_until_due_and_skip_successfully_delivered_devices()
    {
        var fixture = new DatabaseFixture(); await fixture.InitializeAsync();
        try
        {
            var queued = await Queue(fixture, "success", "retry");
            using var services = Services(fixture);
            var sender = new Sender { FailToken = "retry" }; var clock = new ManualClock();
            var worker = Worker(services, sender, clock);
            await worker.DeliverPending();
            Assert.Equal("success", Assert.Single(sender.Sent));
            await worker.DeliverPending(); Assert.Single(sender.Sent);
            await using (var db = fixture.Db()) Assert.Equal(1, (await db.Messages.FindAsync(queued.Message))!.PushAttempts);
            sender.FailToken = null; clock.Advance(TimeSpan.FromSeconds(6));
            await worker.DeliverPending();
            Assert.Equal(new[] { "success", "retry" }, sender.Sent);
            await using (var db = fixture.Db()) Assert.Equal(2, await db.PushDeliveries.CountAsync(d => d.MessageId == queued.Message));
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Expired_claims_are_recovered_after_a_worker_restart()
    {
        var fixture = new DatabaseFixture(); await fixture.InitializeAsync();
        try
        {
            var queued = await Queue(fixture, "device"); var clock = new ManualClock();
            await using (var db = fixture.Db())
                await db.Messages.Where(m => m.Id == queued.Message).ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.PushLeaseId, Guid.NewGuid()).SetProperty(m => m.PushLeaseUntil, clock.GetUtcNow().AddMinutes(2)));
            using var services = Services(fixture); var sender = new Sender();
            await Worker(services, sender, clock).DeliverPending(); Assert.Empty(sender.Sent);
            clock.Advance(TimeSpan.FromMinutes(3));
            await Worker(services, sender, clock).DeliverPending(); Assert.Single(sender.Sent);
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task An_expired_final_attempt_does_not_send_a_fifth_delivery()
    {
        var fixture = new DatabaseFixture(); await fixture.InitializeAsync();
        try
        {
            var queued = await Queue(fixture, "device"); var clock = new ManualClock();
            await using (var db = fixture.Db())
                await db.Messages.Where(m => m.Id == queued.Message).ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.PushAttempts, 4).SetProperty(m => m.PushLeaseId, Guid.NewGuid())
                    .SetProperty(m => m.PushLeaseUntil, clock.GetUtcNow().AddSeconds(-1)));
            using var services = Services(fixture); var sender = new Sender();
            await Worker(services, sender, clock).DeliverPending();
            Assert.Empty(sender.Sent);
            await using var finished = fixture.Db();
            var message = await finished.Messages.FindAsync(queued.Message);
            Assert.False(message!.PushPending); Assert.Equal(4, message.PushAttempts);
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Push_migration_can_be_reversed_and_reapplied_without_losing_chat_history()
    {
        var fixture = new DatabaseFixture(); await fixture.InitializeAsync();
        try
        {
            var queued = await Queue(fixture, "device");
            await using (var db = fixture.Db())
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync("20261021120000_ReportRetention");
                await migrator.MigrateAsync();
            }
            await using var upgraded = fixture.Db();
            var message = await upgraded.Messages.FindAsync(queued.Message);
            Assert.Equal("Queued", message!.Text); Assert.True(message.PushPending);
            Assert.Null(message.PushLeaseId); Assert.Null(message.PushLeaseUntil);
        }
        finally { await fixture.DisposeAsync(); }
    }
}
