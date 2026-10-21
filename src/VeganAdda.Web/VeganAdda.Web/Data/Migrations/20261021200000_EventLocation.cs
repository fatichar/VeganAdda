using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EventLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Events",
                type: "character varying(600)",
                maxLength: 600,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Events",
                type: "character varying(600)",
                maxLength: 600,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Events");
        }
    }
}
