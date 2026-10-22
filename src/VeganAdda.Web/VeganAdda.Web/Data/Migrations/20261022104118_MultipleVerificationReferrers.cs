using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultipleVerificationReferrers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VerificationRequests_Accounts_ReferrerId",
                table: "VerificationRequests");

            migrationBuilder.DropIndex(
                name: "IX_VerificationRequests_ReferrerId",
                table: "VerificationRequests");

            migrationBuilder.CreateTable(
                name: "VerificationReferrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferrerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Responded = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationReferrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationReferrals_Accounts_ReferrerId",
                        column: x => x.ReferrerId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VerificationReferrals_VerificationRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "VerificationRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationReferrals_ReferrerId",
                table: "VerificationReferrals",
                column: "ReferrerId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationReferrals_RequestId_ReferrerId",
                table: "VerificationReferrals",
                columns: new[] { "RequestId", "ReferrerId" },
                unique: true);

            // Preserve existing confirmations, declines, timestamps and deleted-referrer context.
            migrationBuilder.Sql("""
                INSERT INTO "VerificationReferrals" ("Id", "RequestId", "ReferrerId", "Status", "Responded")
                SELECT "Id", "Id", "ReferrerId", "ReferralStatus", "Responded"
                FROM "VerificationRequests"
                WHERE "ReferrerId" IS NOT NULL OR "ReferralStatus" <> 'Not requested';
                """);

            migrationBuilder.DropColumn(
                name: "ReferralStatus",
                table: "VerificationRequests");

            migrationBuilder.DropColumn(
                name: "ReferrerId",
                table: "VerificationRequests");

            migrationBuilder.DropColumn(
                name: "Responded",
                table: "VerificationRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferralStatus",
                table: "VerificationRequests",
                type: "character varying(600)",
                maxLength: 600,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ReferrerId",
                table: "VerificationRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Responded",
                table: "VerificationRequests",
                type: "timestamp with time zone",
                nullable: true);

            // Legacy schema can retain only one response per request; additional responses are lost on rollback.
            migrationBuilder.Sql("""
                UPDATE "VerificationRequests" SET "ReferralStatus" = 'Not requested';
                UPDATE "VerificationRequests" AS request
                SET "ReferrerId" = referral."ReferrerId", "ReferralStatus" = referral."Status", "Responded" = referral."Responded"
                FROM (SELECT DISTINCT ON ("RequestId") * FROM "VerificationReferrals" ORDER BY "RequestId", "Id") AS referral
                WHERE request."Id" = referral."RequestId";
                """);

            migrationBuilder.DropTable(
                name: "VerificationReferrals");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationRequests_ReferrerId",
                table: "VerificationRequests",
                column: "ReferrerId");

            migrationBuilder.AddForeignKey(
                name: "FK_VerificationRequests_Accounts_ReferrerId",
                table: "VerificationRequests",
                column: "ReferrerId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
