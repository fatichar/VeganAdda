using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

using System.Text.Json;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Profiles;

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfileCityCenterFallback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasPreciseLocation",
                table: "Profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
            migrationBuilder.Sql("UPDATE \"Profiles\" SET \"HasPreciseLocation\" = TRUE WHERE \"Latitude\" IS NOT NULL AND \"Longitude\" IS NOT NULL;");
            // Keep the catalog associated with this migration. Unknown and ambiguous places remain unset.
            var json = JsonSerializer.Serialize(CityCenterCatalog.All(), Wire.Json).Replace("'", "''");
            migrationBuilder.Sql($"""
                UPDATE "Profiles" AS p SET "Latitude" = c.latitude, "Longitude" = c.longitude
                FROM jsonb_to_recordset('{json}'::jsonb) AS c(country text, state text, city text, latitude double precision, longitude double precision)
                WHERE p."HasPreciseLocation" = FALSE AND p."Latitude" IS NULL AND p."Longitude" IS NULL
                  AND upper(trim(p."Country")) = upper(trim(c.country))
                  AND upper(trim(p."State")) = upper(trim(c.state))
                  AND upper(trim(p."City")) = upper(trim(c.city));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Profiles\" SET \"Latitude\" = NULL, \"Longitude\" = NULL WHERE \"HasPreciseLocation\" = FALSE;");
            migrationBuilder.DropColumn(
                name: "HasPreciseLocation",
                table: "Profiles");
        }
    }
}
