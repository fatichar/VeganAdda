using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandChatEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "EncryptedIntroduction",
                table: "Relationships",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntroductionKeyId",
                table: "Relationships",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BodyKeyId",
                table: "Messages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "EncryptedBody",
                table: "Messages",
                type: "bytea",
                nullable: true);

            // The previous image can replace an introduction on reconnection.
            // Discard the old envelope when that image changes its source fields.
            migrationBuilder.Sql("""
                CREATE FUNCTION veganadda_invalidate_legacy_chat_introduction() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF (NEW."Introduction", NEW."RequestedBy") IS DISTINCT FROM (OLD."Introduction", OLD."RequestedBy")
                        AND (NEW."EncryptedIntroduction", NEW."IntroductionKeyId")
                            IS NOT DISTINCT FROM (OLD."EncryptedIntroduction", OLD."IntroductionKeyId")
                    THEN
                        NEW."EncryptedIntroduction" := NULL;
                        NEW."IntroductionKeyId" := NULL;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER "InvalidateLegacyChatIntroduction"
                BEFORE UPDATE OF "Introduction", "RequestedBy" ON "Relationships"
                FOR EACH ROW EXECUTE FUNCTION veganadda_invalidate_legacy_chat_introduction();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "InvalidateLegacyChatIntroduction" ON "Relationships";
                DROP FUNCTION veganadda_invalidate_legacy_chat_introduction();
                """);

            migrationBuilder.DropColumn(
                name: "EncryptedIntroduction",
                table: "Relationships");

            migrationBuilder.DropColumn(
                name: "IntroductionKeyId",
                table: "Relationships");

            migrationBuilder.DropColumn(
                name: "BodyKeyId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "EncryptedBody",
                table: "Messages");
        }
    }
}
