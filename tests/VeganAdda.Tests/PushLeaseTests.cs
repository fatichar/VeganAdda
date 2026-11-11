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
            Assert.NotNull(message.EncryptedBody);

            var newClient = Guid.NewGuid();
            var healthyClient = Guid.NewGuid();
            var newId = await InsertStagedMessage(upgraded, key, a, newClient, "Sent before rollback",
                fixture.ChatEncryption.CurrentKeyId, fixture.ChatEncryption.EncryptMessage(key, a, newClient, "Sent before rollback"));
            var healthyBody = fixture.ChatEncryption.EncryptMessage(key, a, healthyClient, "Healthy encrypted sample");
            var healthyId = await InsertStagedMessage(upgraded, key, a, healthyClient, "Healthy encrypted sample",
                fixture.ChatEncryption.CurrentKeyId, healthyBody);
            Assert.Equal("Sent before rollback", await upgraded.Database.SqlQuery<string>($"""
                SELECT "Text" AS "Value" FROM "Messages" WHERE "Id" = {newId}
                """).SingleAsync());
            await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "EncryptedBody" = decode('00', 'hex') WHERE "Id" = {message.Id}
                """);
            Assert.True(await ChatEncryptionMigration.NeedsBackfill(upgraded, fixture.ChatEncryption));
            await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "BodyKeyId" = NULL WHERE "Id" = {newId}
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
            await ChatEncryptionMigration.Migrate(upgraded, fixture.ChatEncryption);
            Assert.False(await ChatEncryptionMigration.HasLegacyWrites(upgraded));
            Assert.False(await ChatEncryptionMigration.NeedsBackfill(upgraded, fixture.ChatEncryption));
            // Verified rows in a repaired cohort keep their envelope.
            Assert.Equal(healthyBody, (await upgraded.Messages.AsNoTracking().SingleAsync(m => m.Id == healthyId)).EncryptedBody);
            var repaired = await upgraded.Messages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
            Assert.Equal("Queued", fixture.ChatEncryption.DecryptMessage(key, a, clientId,
                repaired.BodyKeyId!, repaired.EncryptedBody!));
            var partial = await upgraded.Messages.AsNoTracking().SingleAsync(m => m.Id == newId);
            Assert.Equal("Sent before rollback", fixture.ChatEncryption.DecryptMessage(key, a, partial.ClientId,
                partial.BodyKeyId!, partial.EncryptedBody!));
            Assert.NotNull((await upgraded.Messages.AsNoTracking().SingleAsync(m => m.ClientId == rollbackClientId)).EncryptedBody);
            var relationship = await upgraded.Relationships.AsNoTracking().SingleAsync(r => r.Key == key);
            Assert.Equal("Replaced after rollback", fixture.ChatEncryption.DecryptIntroduction(key, b,
                relationship.IntroductionKeyId!, relationship.EncryptedIntroduction!));

            // More rows than the release check samples must be repaired in one run.
            await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "BodyKeyId" = 'historical', "EncryptedBody" = decode('00', 'hex')
                WHERE "Conversation" = {key}
                """);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ChatEncryptionMigration.NeedsBackfill(upgraded, fixture.ChatEncryption));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ChatEncryptionMigration.Migrate(upgraded, fixture.ChatEncryption, checkSamples: false));
            using var fixtureRing = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(fixture.ChatKeysPath));
            var current = fixture.ChatEncryption.CurrentKeyId;
            var keys = new Dictionary<string, byte[]>
            {
                [current] = fixtureRing.RootElement.GetProperty("keys").GetProperty(current).GetBytesFromBase64(),
                ["historical"] = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)
            };
            var rotatedPath = fixture.ChatKeysPath + ".rotated";
            try
            {
                File.WriteAllText(rotatedPath, System.Text.Json.JsonSerializer.Serialize(new { current, keys }));
                using var rotated = ChatEncryption.Load(rotatedPath);
                Assert.True(await ChatEncryptionMigration.NeedsBackfill(upgraded, rotated));
                await ChatEncryptionMigration.Migrate(upgraded, rotated);
                Assert.False(await ChatEncryptionMigration.NeedsBackfill(upgraded, rotated));
                var cohort = await upgraded.Messages.AsNoTracking().Where(m => m.Conversation == key).ToListAsync();
                Assert.Equal(4, cohort.Count);
                Assert.All(cohort, row =>
                {
                    Assert.Equal(current, row.BodyKeyId);
                    Assert.NotEmpty(rotated.DecryptMessage(key, row.SenderId, row.ClientId, row.BodyKeyId, row.EncryptedBody));
                });

                // A valid envelope containing different text must also stop the drop.
                var wrongBody = rotated.EncryptMessage(key, a, healthyClient, "Wrong body");
                await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "EncryptedBody" = {wrongBody} WHERE "Id" = {healthyId}
                    """);
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => ChatEncryptionMigration.MigrateEncryptedOnly(upgraded, rotated));
                Assert.Equal("Healthy encrypted sample", await upgraded.Database.SqlQuery<string>($"""
                    SELECT "Text" AS "Value" FROM "Messages" WHERE "Id" = {healthyId}
                    """).SingleAsync());
                await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "EncryptedBody" = {healthyBody} WHERE "Id" = {healthyId}
                    """);
                await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Relationships" SET "EncryptedIntroduction" = decode('00', 'hex') WHERE "Key" = {key}
                    """);
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => ChatEncryptionMigration.MigrateEncryptedOnly(upgraded, rotated));
                await ChatEncryptionMigration.Migrate(upgraded, rotated);

                // A plaintext-only row written by the previous image is rebuilt during the drop, and the
                // release check tolerates it because --migrate repairs it before verifying under lock.
                var lateClientId = Guid.NewGuid();
                await upgraded.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Messages" ("Conversation", "SenderId", "ClientId", "Text", "SentAt", "PushPending", "PushAttempts")
                    VALUES ({key}, {a}, {lateClientId}, 'Written by the previous image', now(), FALSE, 0)
                    """);
                await Assert.ThrowsAsync<InvalidOperationException>(() => ChatEncryptionMigration.VerifyAllPlaintext(upgraded, rotated));
                await ChatEncryptionMigration.VerifyAllPlaintext(upgraded, rotated, allowRepairable: true);

                var messageFile = await RelationFile(upgraded, "Messages");
                var relationshipFile = await RelationFile(upgraded, "Relationships");
                await ChatEncryptionMigration.MigrateEncryptedOnly(upgraded, rotated);
                // Both tables were rewritten, so the dropped plaintext no longer sits in live tuples.
                Assert.NotEqual(messageFile, await RelationFile(upgraded, "Messages"));
                Assert.NotEqual(relationshipFile, await RelationFile(upgraded, "Relationships"));
                var history = (MessagePage)await fixture.Call(b, $"conversations/{key}/messages");
                Assert.Contains(history.Items, m => m.ClientId == rollbackClientId && m.Text == "Sent after rollback");
                Assert.Contains(history.Items, m => m.ClientId == lateClientId && m.Text == "Written by the previous image");
                var connection = (List<ConnectionItem>)await fixture.Call(b, "connections");
                Assert.Equal("Replaced after rollback", Assert.Single(connection).Introduction);
            }
            finally { File.Delete(rotatedPath); }
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Interrupted_key_repair_resumes_on_the_next_release_check()
    {
        var fixture = new DatabaseFixture(); await fixture.InitializeAt(ChatEncryptionMigration.Staged);
        var historicalPath = fixture.ChatKeysPath + ".historical";
        var rotatedPath = fixture.ChatKeysPath + ".rotated";
        try
        {
            var a = await fixture.Member();
            var b = await fixture.Member();
            var key = CommunityService.Pair(a, b);
            await using (var db = fixture.Db())
            {
                var (first, second) = string.CompareOrdinal(a.ToString(), b.ToString()) < 0 ? (a, b) : (b, a);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Relationships" ("Key", "A", "B", "RequestedBy", "State", "Introduction", "Updated", "ReadA", "ReadB")
                    VALUES ({key}, {first}, {second}, {a}, 'accepted', '', now(), 0, 0)
                    """);
            }

            using var fixtureRing = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(fixture.ChatKeysPath));
            var current = fixture.ChatEncryption.CurrentKeyId;
            var historicalKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            File.WriteAllText(historicalPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                current = "historical",
                keys = new Dictionary<string, byte[]> { ["historical"] = historicalKey }
            }));
            File.WriteAllText(rotatedPath, System.Text.Json.JsonSerializer.Serialize(new
            {
                current,
                keys = new Dictionary<string, byte[]>
                {
                    [current] = fixtureRing.RootElement.GetProperty("keys").GetProperty(current).GetBytesFromBase64(),
                    ["historical"] = historicalKey
                }
            }));
            using var historical = ChatEncryption.Load(historicalPath);
            using var rotated = ChatEncryption.Load(rotatedPath);

            // Damaged rows open and close the historical cohort; healthy rows fill the batches between them.
            const int total = 450;
            await using (var db = fixture.Db())
            {
                for (var i = 0; i < total; i++)
                {
                    var clientId = Guid.NewGuid();
                    var text = $"History {i}";
                    var damaged = i < 3 || i >= total - 3;
                    await InsertStagedMessage(db, key, a, clientId, text, "historical",
                        damaged ? [0] : historical.EncryptMessage(key, a, clientId, text));
                }
                await db.SaveChangesAsync();
            }
            await using var upgraded = fixture.Db();
            var ids = await upgraded.Messages.Where(m => m.Conversation == key).OrderBy(m => m.Id).Select(m => m.Id).ToListAsync();
            Assert.Equal(total, ids.Count);
            var last = ids[^1];

            // Fail the final batch after the first batch has committed its repairs.
            await upgraded.Database.ExecuteSqlRawAsync($$"""
                CREATE FUNCTION test_interrupt_chat_repair() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'interrupted'; END; $$;
                CREATE TRIGGER "InterruptChatRepair" BEFORE UPDATE ON "Messages"
                FOR EACH ROW WHEN (OLD."Id" = {{last}}) EXECUTE FUNCTION test_interrupt_chat_repair();
                """);
            await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => ChatEncryptionMigration.Migrate(upgraded, rotated));
            var opening = await upgraded.Messages.AsNoTracking().Where(m => ids.Take(3).Contains(m.Id)).ToListAsync();
            Assert.All(opening, row => Assert.Equal(current, row.BodyKeyId));
            Assert.Equal("historical", (await upgraded.Messages.AsNoTracking().SingleAsync(m => m.Id == last)).BodyKeyId);
            await upgraded.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER "InterruptChatRepair" ON "Messages";
                DROP FUNCTION test_interrupt_chat_repair();
                """);

            // The next three historical samples are healthy, so only the recorded cohort reveals the remaining work.
            Assert.False(await ChatEncryptionMigration.HasLegacyWrites(upgraded));
            Assert.True(await ChatEncryptionMigration.NeedsBackfill(upgraded, rotated));
            await ChatEncryptionMigration.Migrate(upgraded, rotated);
            Assert.False(await ChatEncryptionMigration.NeedsBackfill(upgraded, rotated));
            Assert.Empty(await upgraded.ChatKeyRepairs.ToListAsync());
            var rows = await upgraded.Messages.AsNoTracking().Where(m => m.Conversation == key).ToListAsync();
            for (var i = 0; i < ids.Count; i++)
            {
                var row = rows.Single(m => m.Id == ids[i]);
                Assert.Equal($"History {i}", rotated.DecryptMessage(key, row.SenderId, row.ClientId, row.BodyKeyId, row.EncryptedBody));
            }
            Assert.Equal(6, rows.Count(row => row.BodyKeyId == current));
        }
        finally
        {
            File.Delete(historicalPath);
            File.Delete(rotatedPath);
            await fixture.DisposeAsync();
        }
    }
    private static async Task<long> RelationFile(CommunityDb db, string table)
        => (await db.Database.SqlQuery<long>($"SELECT pg_relation_filenode(quote_ident({table})::regclass)::bigint AS \"Value\"").ToListAsync()).Single();

    private static async Task<long> InsertStagedMessage(CommunityDb db, string conversation, Guid sender,
        Guid clientId, string text, string keyId, byte[] envelope)
        => (await db.Database.SqlQuery<long>($"""
            INSERT INTO "Messages" ("Conversation", "SenderId", "ClientId", "Text", "BodyKeyId", "EncryptedBody", "SentAt", "PushPending", "PushAttempts")
            VALUES ({conversation}, {sender}, {clientId}, {text}, {keyId}, {envelope}, now(), FALSE, 0)
            RETURNING "Id" AS "Value"
            """).ToListAsync()).Single();
}
