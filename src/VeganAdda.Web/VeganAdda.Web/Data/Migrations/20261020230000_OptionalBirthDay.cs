using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptionalBirthDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasExactBirthDate",
                table: "Profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Existing non-null birth dates were entered as full dates before day
            // became optional. Preserve their precision; leave unknown dates unknown.
            migrationBuilder.Sql("UPDATE \"Profiles\" SET \"HasExactBirthDate\" = TRUE WHERE \"BirthDate\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasExactBirthDate",
                table: "Profiles");
        }
    }
}
