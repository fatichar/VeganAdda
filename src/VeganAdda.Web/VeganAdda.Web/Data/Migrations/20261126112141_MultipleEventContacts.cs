using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultipleEventContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventContacts",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventContacts", x => new { x.EventId, x.MemberId });
                    table.ForeignKey(
                        name: "FK_EventContacts_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventContacts_Profiles_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventContacts_MemberId",
                table: "EventContacts",
                column: "MemberId");

            migrationBuilder.Sql("""
                INSERT INTO "EventContacts" ("EventId", "MemberId")
                SELECT e."Id", e."ContactId"
                FROM "Events" AS e
                JOIN "Profiles" AS p ON p."Id" = e."ContactId"
                WHERE e."ContactId" IS DISTINCT FROM e."SubmittedBy"
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventContacts");
        }
    }
}
