using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequireChatEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "Relationships", "Messages" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Messages"
                        WHERE "BodyKeyId" IS NULL OR length("BodyKeyId") = 0
                            OR "EncryptedBody" IS NULL OR octet_length("EncryptedBody") < 29)
                    OR EXISTS (SELECT 1 FROM "Relationships"
                        WHERE "Introduction" <> '' AND ("IntroductionKeyId" IS NULL
                            OR length("IntroductionKeyId") = 0 OR "EncryptedIntroduction" IS NULL
                            OR octet_length("EncryptedIntroduction") <= 29))
                    OR EXISTS (SELECT 1 FROM "Relationships"
                        WHERE "Introduction" = '' AND ("IntroductionKeyId" IS NOT NULL OR "EncryptedIntroduction" IS NOT NULL))
                    THEN
                        RAISE EXCEPTION 'Chat backfill is incomplete. Run the application migration command.';
                    END IF;
                END $$;
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Chat encryption cannot be reversed by a schema downgrade. Restore a compatible backup instead.");
        }
    }
}
