using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    public partial class EventSubmissions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "PendingReview", table: "Events", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "ReviewNote", table: "Events", type: "character varying(600)", maxLength: 600, nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "ReviewedAt", table: "Events", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "ReviewedBy", table: "Events", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "SubmittedAt", table: "Events", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "SubmittedBy", table: "Events", type: "uuid", nullable: true);

            migrationBuilder.CreateIndex(name: "IX_Events_PendingReview_Demo", table: "Events", columns: new[] { "PendingReview", "Demo" });
            migrationBuilder.CreateIndex(name: "IX_Events_SubmittedBy", table: "Events", column: "SubmittedBy");
            migrationBuilder.AddForeignKey(name: "FK_Events_Accounts_SubmittedBy", table: "Events", column: "SubmittedBy", principalTable: "Accounts", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Events_Accounts_SubmittedBy", table: "Events");
            migrationBuilder.DropIndex(name: "IX_Events_PendingReview_Demo", table: "Events");
            migrationBuilder.DropIndex(name: "IX_Events_SubmittedBy", table: "Events");
            migrationBuilder.DropColumn(name: "PendingReview", table: "Events");
            migrationBuilder.DropColumn(name: "ReviewNote", table: "Events");
            migrationBuilder.DropColumn(name: "ReviewedAt", table: "Events");
            migrationBuilder.DropColumn(name: "ReviewedBy", table: "Events");
            migrationBuilder.DropColumn(name: "SubmittedAt", table: "Events");
            migrationBuilder.DropColumn(name: "SubmittedBy", table: "Events");
        }
    }
}
