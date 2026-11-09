using Microsoft.EntityFrameworkCore;
using VeganAdda.Web.Services;

namespace VeganAdda.Web.Data;

public static class ChatEncryptionMigration
{
    public const string Expand = "ExpandChatEncryption";
    private const long MigrationLock = 8743651203778414;
    private const int BatchSize = 200;

    public static async Task Migrate(CommunityDb db, ChatEncryption encryption, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({MigrationLock})", ct);
            await db.Database.MigrateAsync(ct);
            await BackfillMessages(db, encryption, ct);
            await BackfillIntroductions(db, encryption, ct);
            await VerifyKeys(db, encryption, ct);
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
        => db.Database.SqlQueryRaw<bool>("""
            SELECT (EXISTS (SELECT 1 FROM "Messages" WHERE "BodyKeyId" IS NULL AND "EncryptedBody" IS NULL)
                OR EXISTS (SELECT 1 FROM "Relationships" WHERE "Introduction" <> ''
                    AND "IntroductionKeyId" IS NULL AND "EncryptedIntroduction" IS NULL)) AS "Value"
            """).SingleAsync(ct);

    public static async Task VerifyKeys(CommunityDb db, ChatEncryption encryption, CancellationToken ct = default)
    {
        // Validate representative ciphertext as well as IDs. A correctly shaped
        // key file may still contain the wrong bytes for a historical key.
        var messages = await db.Database.SqlQueryRaw<StoredMessage>("""
            SELECT DISTINCT ON ("BodyKeyId") "Id", "Conversation", "SenderId", "ClientId", "BodyKeyId", "EncryptedBody", '' AS "Text"
            FROM "Messages" WHERE "BodyKeyId" IS NOT NULL
            ORDER BY "BodyKeyId", "Id"
            """).ToListAsync(ct);
        foreach (var row in messages)
            encryption.DecryptMessage(row.Conversation, row.SenderId, row.ClientId, row.BodyKeyId!, row.EncryptedBody!);

        var introductions = await db.Database.SqlQueryRaw<StoredIntroduction>("""
            SELECT DISTINCT ON ("IntroductionKeyId") "Key", "RequestedBy", "IntroductionKeyId", "EncryptedIntroduction", '' AS "Introduction"
            FROM "Relationships" WHERE "IntroductionKeyId" IS NOT NULL
            ORDER BY "IntroductionKeyId", "Key"
            """).ToListAsync(ct);
        foreach (var row in introductions)
            encryption.DecryptIntroduction(row.Key, row.RequestedBy, row.IntroductionKeyId!, row.EncryptedIntroduction!);
    }

    private static async Task BackfillMessages(CommunityDb db, ChatEncryption encryption, CancellationToken ct)
    {
        long after = 0;
        while (true)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Database.SqlQuery<StoredMessage>($"""
                SELECT "Id", "Conversation", "SenderId", "ClientId", "Text", "BodyKeyId", "EncryptedBody"
                FROM "Messages" WHERE "Id" > {after} ORDER BY "Id" LIMIT {BatchSize} FOR UPDATE
                """).ToListAsync(ct);
            foreach (var row in rows)
            {
                var keyId = row.BodyKeyId ?? encryption.CurrentKeyId;
                var envelope = row.EncryptedBody ?? encryption.EncryptMessage(row.Conversation, row.SenderId, row.ClientId, row.Text);
                if (encryption.DecryptMessage(row.Conversation, row.SenderId, row.ClientId, keyId, envelope) != row.Text)
                    throw new InvalidOperationException("An encrypted chat message did not match its migration source.");
                if (row.EncryptedBody == null)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE "Messages" SET "BodyKeyId" = {keyId}, "EncryptedBody" = {envelope} WHERE "Id" = {row.Id}
                        """, ct);
            }
            await transaction.CommitAsync(ct);
            if (rows.Count < BatchSize) return;
            after = rows[^1].Id;
        }
    }

    private static async Task BackfillIntroductions(CommunityDb db, ChatEncryption encryption, CancellationToken ct)
    {
        var after = "";
        while (true)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Database.SqlQuery<StoredIntroduction>($"""
                SELECT "Key", "RequestedBy", "Introduction", "IntroductionKeyId", "EncryptedIntroduction"
                FROM "Relationships" WHERE "Key" > {after} AND "Introduction" <> ''
                ORDER BY "Key" LIMIT {BatchSize} FOR UPDATE
                """).ToListAsync(ct);
            foreach (var row in rows)
            {
                var keyId = row.IntroductionKeyId ?? encryption.CurrentKeyId;
                var envelope = row.EncryptedIntroduction ?? encryption.EncryptIntroduction(row.Key, row.RequestedBy, row.Introduction);
                if (encryption.DecryptIntroduction(row.Key, row.RequestedBy, keyId, envelope) != row.Introduction)
                    throw new InvalidOperationException("An encrypted introduction did not match its migration source.");
                if (row.EncryptedIntroduction == null)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE "Relationships" SET "IntroductionKeyId" = {keyId}, "EncryptedIntroduction" = {envelope} WHERE "Key" = {row.Key}
                        """, ct);
            }
            await transaction.CommitAsync(ct);
            if (rows.Count < BatchSize) return;
            after = rows[^1].Key;
        }
    }

    private sealed record StoredMessage(long Id, string Conversation, Guid SenderId, Guid ClientId,
        string Text, string? BodyKeyId, byte[]? EncryptedBody);

    private sealed record StoredIntroduction(string Key, Guid RequestedBy, string Introduction,
        string? IntroductionKeyId, byte[]? EncryptedIntroduction);
}
