using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations;

[DbContext(typeof(CommunityDb))]
[Migration("20261021170000_NormalizeProfileGender")]
public partial class NormalizeProfileGender : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            UPDATE "Profiles" SET "Gender" = 'Other'
            WHERE "Gender" IN ('Non-binary', 'Another gender');
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The two old choices merge into Other; their original values cannot be reconstructed.
    }
}
