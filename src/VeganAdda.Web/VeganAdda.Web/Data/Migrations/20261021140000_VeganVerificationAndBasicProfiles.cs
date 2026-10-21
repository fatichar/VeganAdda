using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations;

public partial class VeganVerificationAndBasicProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(name: "Approved", table: "Profiles", newName: "VeganVerified");
        migrationBuilder.Sql("""
            UPDATE "Profiles" SET "VeganVerified" = "VeganVerified" AND "IsVegan" IS TRUE,
                "FeatureConsent" = TRUE;
            """);
        migrationBuilder.DropColumn(name: "VeganVerificationPending", table: "Profiles");
        migrationBuilder.DropColumn(name: "TrustRevoked", table: "Accounts");
        migrationBuilder.DropColumn(name: "Verified", table: "Accounts");
        migrationBuilder.AlterColumn<bool>(name: "FeatureConsent", table: "Profiles", type: "boolean",
            nullable: false, defaultValue: true, oldClrType: typeof(bool), oldType: "boolean");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Legacy inherited trust and previous consent choices cannot be reconstructed.
        migrationBuilder.RenameColumn(name: "VeganVerified", table: "Profiles", newName: "Approved");
        migrationBuilder.AddColumn<bool>(name: "VeganVerificationPending", table: "Profiles", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.Sql("""UPDATE "Profiles" SET "VeganVerificationPending" = "IsVegan" IS TRUE AND NOT "Approved";""");
        migrationBuilder.AddColumn<bool>(name: "TrustRevoked", table: "Accounts", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "Verified", table: "Accounts", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AlterColumn<bool>(name: "FeatureConsent", table: "Profiles", type: "boolean",
            nullable: false, oldClrType: typeof(bool), oldType: "boolean", oldDefaultValue: true);
    }
}
