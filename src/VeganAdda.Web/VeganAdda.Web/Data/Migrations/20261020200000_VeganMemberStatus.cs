using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations;

public partial class VeganMemberStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsVegan",
            table: "Profiles",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "VeganVerificationPending",
            table: "Profiles",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IsVegan", table: "Profiles");
        migrationBuilder.DropColumn(name: "VeganVerificationPending", table: "Profiles");
    }
}
