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
        var fixture = new DatabaseFixture(); await fixture.InitializeAt("ProfilePhotoFocus");
        try
        {
            var a = await fixture.Member();
            var b = await fixture.Member();
            var key = CommunityService.Pair(a, b);
            var (first, second) = string.CompareOrdinal(a.ToString(), b.ToString()) < 0 ? (a, b) : (b, a);
            var clientId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await using (var db = fixture.Db())
            {
                var migrator = db.GetService<IMigrator>();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Relationships" ("Key", "A", "B", "RequestedBy", "State", "Introduction", "Updated", "ReadA", "ReadB")
                    VALUES ({key}, {first}, {second}, {a}, 'accepted', 'Legacy introduction', {now}, 0, 0)
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Messages" ("Conversation", "SenderId", "ClientId", "Text", "SentAt", "PushPending", "PushAttempts")
                    VALUES ({key}, {a}, {clientId}, 'Queued', {now}, TRUE, 0)
                    """);
                await migrator.MigrateAsync("20261021120000_ReportRetention");
                await ChatEncryptionMigration.Migrate(db, fixture.ChatEncryption);
            }
            await using var upgraded = fixture.Db();
            var message = await upgraded.Messages.SingleAsync(m => m.ClientId == clientId);
            Assert.True(message.PushPending);
            Assert.Null(message.PushLeaseId); Assert.Null(message.PushLeaseUntil);
            var history = (MessagePage)await fixture.Call(b, $"conversations/{key}/messages");
            Assert.Equal("Queued", Assert.Single(history.Items).Text);
            var connection = (List<ConnectionItem>)await fixture.Call(b, "connections");
            Assert.Equal("Legacy introduction", Assert.Single(connection).Introduction);
            Assert.Equal("Queued", message.Text);
            Assert.NotNull(message.EncryptedBody);

            // The old image reads and writes only the plaintext columns.
            var newMessage = (Message)await fixture.Call(a, $"conversations/{key}/messages",
                new MessageInput(Guid.NewGuid(), "Sent before rollback"), "POST");
            await fixture.Call(a, $"conversations/{key}/messages",
                new MessageInput(Guid.NewGuid(), "Healthy encrypted sample"), "POST");
            Assert.Equal("Sent before rollback", await upgraded.Database.SqlQuery<string>($"""
                SELECT "Text" AS "Value" FROM "Messages" WHERE "Id" = {newMessage.Id}
                """).SingleAsync());
            await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "EncryptedBody" = decode('00', 'hex') WHERE "Id" = {message.Id}
                """);
            Assert.True(await ChatEncryptionMigration.NeedsBackfill(upgraded, fixture.ChatEncryption));
            await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "BodyKeyId" = NULL WHERE "Id" = {newMessage.Id}
                """);
            var rollbackClientId = Guid.NewGuid();
            await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Messages" ("Conversation", "SenderId", "ClientId", "Text", "SentAt", "PushPending", "PushAttempts")
                VALUES ({key}, {a}, {rollbackClientId}, 'Sent after rollback', {now}, FALSE, 0)
                """);
            await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Relationships" SET "Introduction" = 'Replaced after rollback', "RequestedBy" = {b} WHERE "Key" = {key}
                """);
            Assert.True(await ChatEncryptionMigration.HasLegacyWrites(upgraded));
            history = (MessagePage)await fixture.Call(b, $"conversations/{key}/messages");
            Assert.Contains(history.Items, m => m.ClientId == rollbackClientId && m.Text == "Sent after rollback");
            connection = (List<ConnectionItem>)await fixture.Call(b, "connections");
            Assert.Equal("Replaced after rollback", Assert.Single(connection).Introduction);
            await ChatEncryptionMigration.Migrate(upgraded, fixture.ChatEncryption);
            Assert.False(await ChatEncryptionMigration.HasLegacyWrites(upgraded));
            Assert.False(await ChatEncryptionMigration.NeedsBackfill(upgraded, fixture.ChatEncryption));
            var repaired = await upgraded.Messages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
            Assert.Equal("Queued", fixture.ChatEncryption.DecryptMessage(key, a, clientId,
                repaired.BodyKeyId!, repaired.EncryptedBody!));
            var partial = await upgraded.Messages.AsNoTracking().SingleAsync(m => m.Id == newMessage.Id);
            Assert.Equal("Sent before rollback", fixture.ChatEncryption.DecryptMessage(key, a, partial.ClientId,
                partial.BodyKeyId!, partial.EncryptedBody!));
            Assert.NotNull((await upgraded.Messages.AsNoTracking().SingleAsync(m => m.ClientId == rollbackClientId)).EncryptedBody);
            var relationship = await upgraded.Relationships.AsNoTracking().SingleAsync(r => r.Key == key);
            Assert.Equal("Replaced after rollback", fixture.ChatEncryption.DecryptIntroduction(key, b,
                relationship.IntroductionKeyId!, relationship.EncryptedIntroduction!));
        }
        finally { await fixture.DisposeAsync(); }
    }
}
