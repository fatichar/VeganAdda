using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using VeganAdda.Web.Services;

namespace VeganAdda.Web.Data;

public static class ChatEncryptionMigration
{
    public const string Expand = "ExpandChatEncryption";
    public const string Staged = "PersistChatKeyRepairs";
    public const string Contract = "RemoveChatPlaintext";
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
            await db.GetService<IMigrator>().MigrateAsync(Staged, ct);
            // FindRepairs performs the same check while sampling. Authenticate the active key before
            // any backfill writes ciphertext under it.
            if (!checkSamples) await AuthenticateKeys(db, encryption, ct, activeOnly: true);
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

    public static async Task MigrateEncryptedOnly(CommunityDb db, ChatEncryption encryption, CancellationToken ct = default)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        if (Includes(applied, Contract))
        {
            await db.Database.MigrateAsync(ct);
            await AuthenticateKeys(db, encryption, ct);
            return;
        }

        // Finish any staged backfill or recorded repair from the retained plaintext first,
        // so rollback-era rows and interrupted repairs do not block the drop. Unexplained
        // mismatches in complete rows are not rewritten here; verification below rejects them.
        await Migrate(db, encryption, ct, checkSamples: false);

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync("""LOCK TABLE "Relationships", "Messages", "ChatKeyRepairs" IN ACCESS EXCLUSIVE MODE""", ct);
            if (!Includes(await db.Database.GetAppliedMigrationsAsync(ct), Contract))
            {
                await VerifyAllPlaintext(db, encryption, ct);
                await db.Database.ExecuteSqlRawAsync("SET LOCAL veganadda.chat_plaintext_verified = 'yes'", ct);
                // Run the generated script verbatim: EF's raw SQL APIs treat braces as format placeholders.
                var sql = db.GetService<IMigrator>().GenerateScript(Staged, Contract, MigrationsSqlGenerationOptions.NoTransactions);
                await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection(),
                    (NpgsqlTransaction)transaction.GetDbTransaction());
                await command.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// Compares every retained plaintext with its encrypted copy. With <paramref name="allowRepairable"/>,
    /// rows the staged migration rebuilds from plaintext (missing copies and keys with recorded repairs)
    /// are skipped, so the release check fails only on mismatches that need an operator.
    /// </summary>
    public static async Task VerifyAllPlaintext(CommunityDb db, ChatEncryption encryption, CancellationToken ct = default,
        bool allowRepairable = false)
    {
        var pending = (await PendingRepairs(db, ct)).ToHashSet(StringComparer.Ordinal);
        if (!allowRepairable && pending.Count != 0)
            throw new InvalidOperationException("Chat key repairs are pending. Complete the staged repair before removing plaintext.");

        long? lastId = null;
        while (true)
        {
            var rows = await db.Database.SqlQuery<MessageSource>($"""
                SELECT "Id", "Conversation", "SenderId", "ClientId", "Text", "BodyKeyId", "EncryptedBody"
                FROM "Messages" WHERE {lastId}::bigint IS NULL OR "Id" > {lastId}
                ORDER BY "Id" LIMIT {BatchSize}
                """).ToListAsync(ct);
            foreach (var row in rows)
            {
                if (allowRepairable && (row.BodyKeyId == null || row.EncryptedBody == null || pending.Contains(row.BodyKeyId)))
                    continue;
                if (row.BodyKeyId == null || row.EncryptedBody == null ||
                    TryDecrypt(() => encryption.DecryptMessage(row.Conversation, row.SenderId, row.ClientId,
                        row.BodyKeyId, row.EncryptedBody)) != row.Text)
                    throw new InvalidOperationException("A message failed ciphertext verification. Plaintext must be retained.");
            }
            if (rows.Count < BatchSize) break;
            lastId = rows[^1].Id;
        }

        string? lastKey = null;
        while (true)
        {
            var rows = await db.Database.SqlQuery<IntroductionSource>($"""
                SELECT "Key", "RequestedBy", "Introduction", "IntroductionKeyId", "EncryptedIntroduction"
                FROM "Relationships" WHERE {lastKey}::text IS NULL OR "Key" > {lastKey}
                ORDER BY "Key" LIMIT {BatchSize}
                """).ToListAsync(ct);
            foreach (var row in rows)
            {
                var legacy = row.Introduction.Length == 0
                    ? row.IntroductionKeyId != null || row.EncryptedIntroduction != null
                    : row.IntroductionKeyId == null || row.EncryptedIntroduction == null;
                if (allowRepairable && (legacy || (row.IntroductionKeyId != null && pending.Contains(row.IntroductionKeyId))))
                    continue;
                var valid = row.Introduction.Length == 0
                    ? row.IntroductionKeyId == null && row.EncryptedIntroduction == null
                    : row.IntroductionKeyId != null && row.EncryptedIntroduction != null &&
                        TryDecrypt(() => encryption.DecryptIntroduction(row.Key, row.RequestedBy,
                            row.IntroductionKeyId, row.EncryptedIntroduction)) == row.Introduction;
                if (!valid)
                    throw new InvalidOperationException("A connection introduction failed ciphertext verification. Plaintext must be retained.");
            }
            if (rows.Count < BatchSize) break;
            lastKey = rows[^1].Key;
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

    /// <summary>
    /// Confirms that the ring holds every stored key ID and that its key material authenticates stored
    /// ciphertext: up to three envelopes per key ID are decrypted, and a key fails when none of them
    /// authenticate. A ring with the expected IDs but different key bytes is rejected before any write.
    /// With <paramref name="activeOnly"/>, only the active key must authenticate; while plaintext is
    /// retained, rows under other keys are rebuilt from it.
    /// </summary>
    public static async Task AuthenticateKeys(CommunityDb db, ChatEncryption encryption, CancellationToken ct = default,
        bool activeOnly = false)
    {
        // Loose index scans enumerate distinct key IDs; each lateral probe reads at most three rows.
        var samples = await db.Database.SqlQueryRaw<CipherSample>("""
            WITH RECURSIVE keys AS (
                (SELECT "BodyKeyId" AS id FROM "Messages"
                 WHERE "BodyKeyId" IS NOT NULL AND "EncryptedBody" IS NOT NULL ORDER BY "BodyKeyId" LIMIT 1)
                UNION ALL
                SELECT (SELECT "BodyKeyId" FROM "Messages"
                        WHERE "BodyKeyId" IS NOT NULL AND "EncryptedBody" IS NOT NULL AND "BodyKeyId" > keys.id
                        ORDER BY "BodyKeyId" LIMIT 1)
                FROM keys WHERE keys.id IS NOT NULL
            )
            SELECT sample.* FROM keys CROSS JOIN LATERAL (
                SELECT m."BodyKeyId" AS "KeyId", m."Conversation", m."SenderId" AS "Actor",
                    m."ClientId" AS "ClientId", m."EncryptedBody" AS "Envelope"
                FROM "Messages" m WHERE m."BodyKeyId" = keys.id AND m."EncryptedBody" IS NOT NULL LIMIT 3
            ) AS sample
            """).ToListAsync(ct);
        samples.AddRange(await db.Database.SqlQueryRaw<CipherSample>("""
            WITH RECURSIVE keys AS (
                (SELECT "IntroductionKeyId" AS id FROM "Relationships"
                 WHERE "IntroductionKeyId" IS NOT NULL AND "EncryptedIntroduction" IS NOT NULL
                 ORDER BY "IntroductionKeyId" LIMIT 1)
                UNION ALL
                SELECT (SELECT "IntroductionKeyId" FROM "Relationships"
                        WHERE "IntroductionKeyId" IS NOT NULL AND "EncryptedIntroduction" IS NOT NULL
                            AND "IntroductionKeyId" > keys.id
                        ORDER BY "IntroductionKeyId" LIMIT 1)
                FROM keys WHERE keys.id IS NOT NULL
            )
            SELECT sample.* FROM keys CROSS JOIN LATERAL (
                SELECT r."IntroductionKeyId" AS "KeyId", r."Key" AS "Conversation", r."RequestedBy" AS "Actor",
                    NULL::uuid AS "ClientId", r."EncryptedIntroduction" AS "Envelope"
                FROM "Relationships" r
                WHERE r."IntroductionKeyId" = keys.id AND r."EncryptedIntroduction" IS NOT NULL LIMIT 3
            ) AS sample
            """).ToListAsync(ct));
        ThrowIfMissing(samples.Select(sample => sample.KeyId), encryption);

        var failing = samples.GroupBy(sample => sample.KeyId, StringComparer.Ordinal)
            .Where(group => !activeOnly || group.Key == encryption.CurrentKeyId)
            .Where(group => group.All(sample => TryDecrypt(() => sample.ClientId is { } clientId
                ? encryption.DecryptMessage(sample.Conversation, sample.Actor, clientId, sample.KeyId, sample.Envelope)
                : encryption.DecryptIntroduction(sample.Conversation, sample.Actor, sample.KeyId, sample.Envelope)) == null))
            .Select(group => group.Key).ToArray();
        if (failing.Length != 0)
            throw new InvalidOperationException("Stored chat ciphertext does not authenticate with configured key ID(s) "
                + string.Join(", ", failing) + ". Check the key ring before starting or writing data.");
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

    private sealed record CipherSample(string KeyId, string Conversation, Guid Actor, Guid? ClientId, byte[] Envelope);

    private sealed record MessageSource(long Id, string Conversation, Guid SenderId, Guid ClientId, string Text,
        string? BodyKeyId, byte[]? EncryptedBody);
    private sealed record IntroductionSource(string Key, Guid RequestedBy, string Introduction,
        string? IntroductionKeyId, byte[]? EncryptedIntroduction);
}
