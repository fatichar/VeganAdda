using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using VeganAdda.Web.Services;

namespace VeganAdda.Web.Data;

public static class ChatEncryptionMigration
{
    public const string Expand = "ExpandChatEncryption";
    private const long MigrationLock = 8743651203778414;
    private const int BatchSize = 200;
    private const string LegacyMessage = """("BodyKeyId" IS NULL OR "EncryptedBody" IS NULL)""";
    private const string LegacyIntroduction = """
        (("Introduction" <> '' AND ("IntroductionKeyId" IS NULL OR "EncryptedIntroduction" IS NULL))
          OR ("Introduction" = '' AND ("IntroductionKeyId" IS NOT NULL OR "EncryptedIntroduction" IS NOT NULL)))
        """;

    public static async Task Migrate(CommunityDb db, ChatEncryption encryption, CancellationToken ct = default, bool checkSamples = true)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({MigrationLock})", ct);
            await db.Database.MigrateAsync(ct);
            var legacyWrites = await HasLegacyWrites(db, ct);
            if (checkSamples || legacyWrites)
            {
                var repairs = await FindRepairs(db, encryption, ct);
                if (repairs.Any || legacyWrites)
                {
                    await BackfillMessages(db, encryption, repairs.Messages, ct);
                    await BackfillIntroductions(db, encryption, repairs.Introductions, ct);
                }
            }
        }
        finally
        {
            try { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({MigrationLock})"); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }

    public static bool Includes(IEnumerable<string> migrations, string name)
        => migrations.Any(id => id.EndsWith("_" + name, StringComparison.Ordinal));

    public static Task<bool> HasLegacyWrites(CommunityDb db, CancellationToken ct = default)
        => db.Database.SqlQueryRaw<bool>($"""
            SELECT (EXISTS (SELECT 1 FROM "Messages" WHERE {LegacyMessage})
                OR EXISTS (SELECT 1 FROM "Relationships" WHERE {LegacyIntroduction})) AS "Value"
            """).SingleAsync(ct);

    public static async Task<bool> NeedsBackfill(CommunityDb db, ChatEncryption encryption, CancellationToken ct = default)
    {
        var repairs = await FindRepairs(db, encryption, ct);
        return repairs.Any || await HasLegacyWrites(db, ct);
    }

    private static async Task<Repairs> FindRepairs(CommunityDb db, ChatEncryption encryption, CancellationToken ct)
    {
        // Select representative IDs from narrow indexes, then fetch the bodies.
        var messages = await db.Database.SqlQueryRaw<StoredMessage>("""
            SELECT sample.* FROM (
                SELECT DISTINCT "BodyKeyId" FROM "Messages"
                WHERE "BodyKeyId" IS NOT NULL AND "EncryptedBody" IS NOT NULL
            ) AS keys CROSS JOIN LATERAL (
                SELECT "Id", "Conversation", "SenderId", "ClientId", "BodyKeyId", "EncryptedBody", "Text"
                FROM "Messages" WHERE "BodyKeyId" = keys."BodyKeyId" AND "EncryptedBody" IS NOT NULL
                ORDER BY "Id" LIMIT 3
            ) AS sample
            """).ToListAsync(ct);
        var introductions = await db.Database.SqlQueryRaw<StoredIntroduction>("""
            SELECT sample.* FROM (
                SELECT DISTINCT "IntroductionKeyId" FROM "Relationships"
                WHERE "IntroductionKeyId" IS NOT NULL AND "EncryptedIntroduction" IS NOT NULL AND "Introduction" <> ''
            ) AS keys CROSS JOIN LATERAL (
                SELECT "Key", "RequestedBy", "Introduction", "IntroductionKeyId", "EncryptedIntroduction"
                FROM "Relationships" WHERE "IntroductionKeyId" = keys."IntroductionKeyId"
                    AND "EncryptedIntroduction" IS NOT NULL AND "Introduction" <> ''
                ORDER BY "Key" LIMIT 3
            ) AS sample
            """).ToListAsync(ct);
        var samples = messages.Select(row => (KeyId: row.BodyKeyId, row.Id, Introduction: (string?)null, Text: row.Text,
                Read: TryDecrypt(() => encryption.DecryptMessage(row.Conversation, row.SenderId, row.ClientId,
                    row.BodyKeyId, row.EncryptedBody))))
            .Concat(introductions.Select(row => (KeyId: row.IntroductionKeyId, Id: 0L, Introduction: (string?)row.Key,
                Text: row.Introduction, Read: TryDecrypt(() => encryption.DecryptIntroduction(row.Key, row.RequestedBy,
                    row.IntroductionKeyId, row.EncryptedIntroduction))))).ToArray();
        var active = samples.Where(row => row.KeyId == encryption.CurrentKeyId).ToArray();
        if (active.Length != 0 && active.All(row => row.Read == null))
            throw new InvalidOperationException("No chat sample authenticates with the active key. Check the configured key before rewriting data.");

        var repairs = samples.Where(row => row.Read != row.Text).ToArray();
        return new(repairs.Where(row => row.Introduction == null).Select(row => row.Id).ToArray(),
            repairs.Where(row => row.Introduction != null).Select(row => row.Introduction!).ToArray());
    }

    private static string? TryDecrypt(Func<string> decrypt)
    {
        try { return decrypt(); }
        catch (CryptographicException) { return null; }
    }

    private static async Task BackfillMessages(CommunityDb db, ChatEncryption encryption, long[] repairs, CancellationToken ct)
    {
        long after = 0;
        while (true)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Only fixed predicates and column names enter the SQL text.
            var sql = $$"""
                SELECT "Id", "Conversation", "SenderId", "ClientId", "Text"
                FROM "Messages" WHERE "Id" > {0} AND {{Candidate(LegacyMessage, "Id", repairs.Length)}}
                ORDER BY "Id" LIMIT {2} FOR UPDATE
                """;
            var rows = await db.Database.SqlQueryRaw<MessageSource>(sql, after, repairs, BatchSize).ToListAsync(ct);
            if (rows.Count == 0) return;
            var envelopes = rows.Select(row => encryption.EncryptMessage(row.Conversation, row.SenderId, row.ClientId, row.Text)).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" m SET "BodyKeyId" = {encryption.CurrentKeyId}, "EncryptedBody" = batch.body
                FROM unnest({rows.Select(row => row.Id).ToArray()}, {Bodies(envelopes)}) AS batch(id, body)
                WHERE m."Id" = batch.id
                """, ct);
            await transaction.CommitAsync(ct);
            if (rows.Count < BatchSize) return;
            after = rows[^1].Id;
        }
    }

    private static async Task BackfillIntroductions(CommunityDb db, ChatEncryption encryption, string[] repairs, CancellationToken ct)
    {
        var after = "";
        while (true)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var sql = $$"""
                SELECT "Key", "RequestedBy", "Introduction"
                FROM "Relationships" WHERE "Key" > {0} AND {{Candidate(LegacyIntroduction, "Key", repairs.Length)}}
                ORDER BY "Key" LIMIT {2} FOR UPDATE
                """;
            var rows = await db.Database.SqlQueryRaw<IntroductionSource>(sql, after, repairs, BatchSize).ToListAsync(ct);
            if (rows.Count == 0) return;
            var envelopes = rows.Select(row => row.Introduction.Length == 0 ? null
                : encryption.EncryptIntroduction(row.Key, row.RequestedBy, row.Introduction)).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Relationships" r SET
                    "IntroductionKeyId" = CASE WHEN batch.body IS NULL THEN NULL ELSE {encryption.CurrentKeyId} END,
                    "EncryptedIntroduction" = batch.body
                FROM unnest({rows.Select(row => row.Key).ToArray()}, {Bodies(envelopes)}) AS batch(key, body)
                WHERE r."Key" = batch.key
                """, ct);
            await transaction.CommitAsync(ct);
            if (rows.Count < BatchSize) return;
            after = rows[^1].Key;
        }
    }

    private static string Candidate(string legacyPredicate, string keyColumn, int repairCount)
        => repairCount == 0 ? legacyPredicate : $"({legacyPredicate} OR \"{keyColumn}\" = ANY ({{1}}))";

    private static NpgsqlParameter Bodies(byte[]?[] envelopes)
        => new() { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bytea, Value = envelopes };

    private sealed record Repairs(long[] Messages, string[] Introductions)
    {
        public bool Any => Messages.Length != 0 || Introductions.Length != 0;
    }

    private sealed record StoredMessage(long Id, string Conversation, Guid SenderId, Guid ClientId,
        string Text, string BodyKeyId, byte[] EncryptedBody);

    private sealed record StoredIntroduction(string Key, Guid RequestedBy, string Introduction,
        string IntroductionKeyId, byte[] EncryptedIntroduction);

    private sealed record MessageSource(long Id, string Conversation, Guid SenderId, Guid ClientId, string Text);
    private sealed record IntroductionSource(string Key, Guid RequestedBy, string Introduction);
}
