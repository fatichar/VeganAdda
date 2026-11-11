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

    // Loose index scans over the partial sample indexes: one probe per distinct key ID.
    private const string MessageKeys = """
        WITH RECURSIVE keys AS (
            (SELECT "BodyKeyId" FROM "Messages"
             WHERE "BodyKeyId" IS NOT NULL AND "EncryptedBody" IS NOT NULL
             ORDER BY "BodyKeyId" LIMIT 1)
            UNION ALL
            SELECT next_key."BodyKeyId" FROM keys CROSS JOIN LATERAL (
                SELECT source."BodyKeyId" FROM "Messages" source
                WHERE source."BodyKeyId" IS NOT NULL AND "EncryptedBody" IS NOT NULL
                    AND source."BodyKeyId" > keys."BodyKeyId"
                ORDER BY source."BodyKeyId" LIMIT 1
            ) AS next_key
        )
        """;
    private const string IntroductionKeys = """
        WITH RECURSIVE keys AS (
            (SELECT "IntroductionKeyId" FROM "Relationships"
             WHERE "IntroductionKeyId" IS NOT NULL AND "EncryptedIntroduction" IS NOT NULL AND "Introduction" <> ''
             ORDER BY "IntroductionKeyId" LIMIT 1)
            UNION ALL
            SELECT next_key."IntroductionKeyId" FROM keys CROSS JOIN LATERAL (
                SELECT source."IntroductionKeyId" FROM "Relationships" source
                WHERE source."IntroductionKeyId" IS NOT NULL AND "EncryptedIntroduction" IS NOT NULL AND "Introduction" <> ''
                    AND source."IntroductionKeyId" > keys."IntroductionKeyId"
                ORDER BY source."IntroductionKeyId" LIMIT 1
            ) AS next_key
        )
        """;

    public static async Task Migrate(CommunityDb db, ChatEncryption encryption, CancellationToken ct = default, bool checkSamples = true)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({MigrationLock})", ct);
            await db.Database.MigrateAsync(ct);
            // FindRepairs performs the same check while sampling.
            if (!checkSamples) await EnsureKeysAvailable(db, encryption, ct);
            var legacyWrites = await HasLegacyWrites(db, ct);
            var pending = await PendingRepairs(db, ct);
            if (checkSamples || legacyWrites || pending.Length != 0)
            {
                var found = checkSamples ? await FindRepairs(db, encryption, ct) : [];
                var repairs = found.Union(pending, StringComparer.Ordinal).ToArray();
                if (repairs.Length != 0 || legacyWrites)
                {
                    // Record the cohorts first: once a batch moves repaired rows to the active key,
                    // sampling alone can no longer find the rest, so an interrupted run must resume them.
                    if (repairs.Length != 0)
                        await db.Database.ExecuteSqlInterpolatedAsync($"""
                            INSERT INTO "ChatKeyRepairs" ("KeyId") SELECT unnest({repairs}) ON CONFLICT DO NOTHING
                            """, ct);
                    await BackfillMessages(db, encryption, repairs, ct);
                    await BackfillIntroductions(db, encryption, repairs, ct);
                    if (repairs.Length != 0)
                        await db.Database.ExecuteSqlInterpolatedAsync($"""
                            DELETE FROM "ChatKeyRepairs" WHERE "KeyId" = ANY ({repairs})
                            """, ct);
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
        return repairs.Length != 0 || (await PendingRepairs(db, ct)).Length != 0 || await HasLegacyWrites(db, ct);
    }

    // The table is absent until PersistChatKeyRepairs is applied; the release check runs before that.
    private static async Task<string[]> PendingRepairs(CommunityDb db, CancellationToken ct)
    {
        var exists = await db.Database.SqlQueryRaw<bool>("""
            SELECT to_regclass('"ChatKeyRepairs"') IS NOT NULL AS "Value"
            """).SingleAsync(ct);
        return exists ? await db.ChatKeyRepairs.Select(r => r.KeyId).ToArrayAsync(ct) : [];
    }

    /// <summary>Fails when stored chat rows reference a key ID that the configured ring lacks.</summary>
    public static async Task EnsureKeysAvailable(CommunityDb db, ChatEncryption encryption, CancellationToken ct = default)
    {
        var stored = await db.Database.SqlQueryRaw<string>(MessageKeys + """
            SELECT "BodyKeyId" AS "Value" FROM keys
            """).ToListAsync(ct);
        stored.AddRange(await db.Database.SqlQueryRaw<string>(IntroductionKeys + """
            SELECT "IntroductionKeyId" AS "Value" FROM keys
            """).ToListAsync(ct));
        ThrowIfMissing(stored, encryption);
    }

    private static void ThrowIfMissing(IEnumerable<string> keyIds, ChatEncryption encryption)
    {
        if (keyIds.Any(id => !encryption.HasKey(id)))
            throw new InvalidOperationException("Chat rows reference a key missing from the configured ring. "
                + "Set Storage:MessageKeysPath to the ring that encrypted this database before starting.");
    }

    private static async Task<string[]> FindRepairs(CommunityDb db, ChatEncryption encryption, CancellationToken ct)
    {
        // Select representative IDs from narrow indexes, then fetch the bodies.
        var messages = await db.Database.SqlQueryRaw<StoredMessage>(MessageKeys + """
            SELECT sample.* FROM keys CROSS JOIN LATERAL (
                SELECT m."Id", m."Conversation", m."SenderId", m."ClientId", m."BodyKeyId", m."EncryptedBody", m."Text"
                FROM "Messages" m WHERE m."BodyKeyId" = keys."BodyKeyId" AND "EncryptedBody" IS NOT NULL
                ORDER BY "Id" LIMIT 3
            ) AS sample
            """).ToListAsync(ct);
        var introductions = await db.Database.SqlQueryRaw<StoredIntroduction>(IntroductionKeys + """
            SELECT sample.* FROM keys CROSS JOIN LATERAL (
                SELECT r."Key", r."RequestedBy", r."Introduction", r."IntroductionKeyId", r."EncryptedIntroduction"
                FROM "Relationships" r WHERE r."IntroductionKeyId" = keys."IntroductionKeyId"
                    AND "EncryptedIntroduction" IS NOT NULL AND "Introduction" <> ''
                ORDER BY "Key" LIMIT 3
            ) AS sample
            """).ToListAsync(ct);
        ThrowIfMissing(messages.Select(row => row.BodyKeyId).Concat(introductions.Select(row => row.IntroductionKeyId)), encryption);

        var samples = messages.Select(row => (KeyId: row.BodyKeyId, Text: row.Text,
                Read: TryDecrypt(() => encryption.DecryptMessage(row.Conversation, row.SenderId, row.ClientId,
                    row.BodyKeyId, row.EncryptedBody))))
            .Concat(introductions.Select(row => (KeyId: row.IntroductionKeyId, Text: row.Introduction,
                Read: TryDecrypt(() => encryption.DecryptIntroduction(row.Key, row.RequestedBy,
                    row.IntroductionKeyId, row.EncryptedIntroduction))))).ToArray();
        var active = samples.Where(row => row.KeyId == encryption.CurrentKeyId).ToArray();
        if (active.Length != 0 && active.All(row => row.Read == null))
            throw new InvalidOperationException("No chat sample authenticates with the active key. Check the configured key before rewriting data.");

        // Verify the entire key's cohort in one maintenance window so another
        // damaged row under that key cannot trigger a new rollout.
        return samples.Where(row => row.Read != row.Text).Select(row => row.KeyId).Distinct().ToArray();
    }

    private static string? TryDecrypt(Func<string> decrypt)
    {
        try { return decrypt(); }
        catch (CryptographicException) { return null; }
    }

    private static async Task BackfillMessages(CommunityDb db, ChatEncryption encryption, string[] repairs, CancellationToken ct)
    {
        long after = 0;
        while (true)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Only fixed predicates and column names enter the SQL text.
            var sql = $$"""
                SELECT "Id", "Conversation", "SenderId", "ClientId", "Text", "BodyKeyId", "EncryptedBody"
                FROM "Messages" WHERE "Id" > {0} AND {{Candidate(LegacyMessage, "BodyKeyId", repairs.Length)}}
                ORDER BY "Id" LIMIT {2} FOR UPDATE
                """;
            var rows = await db.Database.SqlQueryRaw<MessageSource>(sql, after, repairs, BatchSize).ToListAsync(ct);
            if (rows.Count == 0) return;
            // Rows in a repaired cohort keep their envelope when it still matches the retained text.
            var stale = rows.Where(row => !(row is { BodyKeyId: { } keyId, EncryptedBody: { } body }
                && TryDecrypt(() => encryption.DecryptMessage(row.Conversation, row.SenderId, row.ClientId, keyId, body)) == row.Text)).ToArray();
            if (stale.Length != 0)
            {
                var envelopes = stale.Select(row => encryption.EncryptMessage(row.Conversation, row.SenderId, row.ClientId, row.Text)).ToArray();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" m SET "BodyKeyId" = {encryption.CurrentKeyId}, "EncryptedBody" = batch.body
                    FROM unnest({stale.Select(row => row.Id).ToArray()}, {Bodies(envelopes)}) AS batch(id, body)
                    WHERE m."Id" = batch.id
                    """, ct);
            }
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
                SELECT "Key", "RequestedBy", "Introduction", "IntroductionKeyId", "EncryptedIntroduction"
                FROM "Relationships" WHERE "Key" > {0} AND {{Candidate(LegacyIntroduction, "IntroductionKeyId", repairs.Length)}}
                ORDER BY "Key" LIMIT {2} FOR UPDATE
                """;
            var rows = await db.Database.SqlQueryRaw<IntroductionSource>(sql, after, repairs, BatchSize).ToListAsync(ct);
            if (rows.Count == 0) return;
            var stale = rows.Where(row => row.Introduction.Length == 0
                ? row.IntroductionKeyId != null || row.EncryptedIntroduction != null
                : !(row is { IntroductionKeyId: { } keyId, EncryptedIntroduction: { } body }
                    && TryDecrypt(() => encryption.DecryptIntroduction(row.Key, row.RequestedBy, keyId, body)) == row.Introduction)).ToArray();
            if (stale.Length != 0)
            {
                var envelopes = stale.Select(row => row.Introduction.Length == 0 ? null
                    : encryption.EncryptIntroduction(row.Key, row.RequestedBy, row.Introduction)).ToArray();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Relationships" r SET
                        "IntroductionKeyId" = CASE WHEN batch.body IS NULL THEN NULL ELSE {encryption.CurrentKeyId} END,
                        "EncryptedIntroduction" = batch.body
                    FROM unnest({stale.Select(row => row.Key).ToArray()}, {Bodies(envelopes)}) AS batch(key, body)
                    WHERE r."Key" = batch.key
                    """, ct);
            }
            await transaction.CommitAsync(ct);
            if (rows.Count < BatchSize) return;
            after = rows[^1].Key;
        }
    }

    private static string Candidate(string legacyPredicate, string keyColumn, int repairCount)
        => repairCount == 0 ? legacyPredicate : $"({legacyPredicate} OR \"{keyColumn}\" = ANY ({{1}}))";

    private static NpgsqlParameter Bodies(byte[]?[] envelopes)
        => new() { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bytea, Value = envelopes };

    private sealed record StoredMessage(long Id, string Conversation, Guid SenderId, Guid ClientId,
        string Text, string BodyKeyId, byte[] EncryptedBody);

    private sealed record StoredIntroduction(string Key, Guid RequestedBy, string Introduction,
        string IntroductionKeyId, byte[] EncryptedIntroduction);

    private sealed record MessageSource(long Id, string Conversation, Guid SenderId, Guid ClientId, string Text,
        string? BodyKeyId, byte[]? EncryptedBody);
    private sealed record IntroductionSource(string Key, Guid RequestedBy, string Introduction,
        string? IntroductionKeyId, byte[]? EncryptedIntroduction);
}
