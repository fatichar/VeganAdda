using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveChatPlaintext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "Relationships", "Messages", "ChatKeyRepairs" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF current_setting('veganadda.chat_plaintext_verified', true) IS DISTINCT FROM 'yes'
                        AND (EXISTS (SELECT 1 FROM "Messages") OR EXISTS (SELECT 1 FROM "Relationships")
                            OR EXISTS (SELECT 1 FROM "ChatKeyRepairs"))
                    THEN
                        RAISE EXCEPTION 'Verify chat ciphertext through the application migration command before removing plaintext.';
                    END IF;
                END $$;

                DROP TRIGGER "InvalidateLegacyChatIntroduction" ON "Relationships";
                DROP FUNCTION veganadda_invalidate_legacy_chat_introduction();
                DROP INDEX "IX_Messages_ChatBackfill", "IX_Messages_ChatKeySample",
                    "IX_Relationships_ChatBackfill", "IX_Relationships_ChatKeySample";
                -- Support the key-ring check's loose index scans.
                CREATE INDEX "IX_Messages_BodyKeyId" ON "Messages" ("BodyKeyId");
                CREATE INDEX "IX_Relationships_IntroductionKeyId" ON "Relationships" ("IntroductionKeyId")
                    WHERE "IntroductionKeyId" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "Introduction",
                table: "Relationships");

            migrationBuilder.DropColumn(
                name: "Text",
                table: "Messages");

            migrationBuilder.AlterColumn<byte[]>(
                name: "EncryptedBody",
                table: "Messages",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BodyKeyId",
                table: "Messages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Relationships_EncryptedIntroduction",
                table: "Relationships",
                sql: "(\"IntroductionKeyId\" IS NULL AND \"EncryptedIntroduction\" IS NULL) OR (\"IntroductionKeyId\" IS NOT NULL AND length(\"IntroductionKeyId\") > 0 AND \"EncryptedIntroduction\" IS NOT NULL AND octet_length(\"EncryptedIntroduction\") > 29)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Messages_EncryptedBody",
                table: "Messages",
                sql: "length(\"BodyKeyId\") > 0 AND octet_length(\"EncryptedBody\") >= 29");

            // DROP COLUMN only hides the column: existing tuples keep the plaintext bytes on disk.
            // Rewriting both tables in this transaction stores dropped values as NULL and unlinks
            // the old files (including TOAST) at commit.
            migrationBuilder.Sql("""
                CLUSTER "Messages" USING "PK_Messages";
                CLUSTER "Relationships" USING "PK_Relationships";
                ALTER TABLE "Messages" SET WITHOUT CLUSTER;
                ALTER TABLE "Relationships" SET WITHOUT CLUSTER;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restore a compatible pre-migration backup to return to a plaintext schema.");
        }
    }
}
