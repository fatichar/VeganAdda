using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EventMemberContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContactId",
                table: "Events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_ContactId",
                table: "Events",
                column: "ContactId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Accounts_ContactId",
                table: "Events",
                column: "ContactId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Accounts_ContactId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_ContactId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ContactId",
                table: "Events");
        }
    }
}
