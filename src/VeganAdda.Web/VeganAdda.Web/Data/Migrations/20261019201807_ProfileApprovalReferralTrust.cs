using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfileApprovalReferralTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Moderator",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Approved",
                table: "Profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "Profiles",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "Profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                table: "Profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TestProfile",
                table: "Profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TrustRevoked",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Verified",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE \"Profiles\" AS p SET \"Approved\" = TRUE, \"TestProfile\" = TRUE FROM \"Accounts\" AS a WHERE a.\"Id\" = p.\"Id\" AND a.\"Demo\" = TRUE");

            migrationBuilder.CreateTable(
                name: "ReferralInvites",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    ReferrerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Expires = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReferredId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralInvites", x => x.TokenHash);
                });

            migrationBuilder.CreateTable(
                name: "Referrals",
                columns: table => new
                {
                    ReferredId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferrerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.ReferredId);
                    table.ForeignKey(
                        name: "FK_Referrals_Accounts_ReferredId",
                        column: x => x.ReferredId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReferralInvites_ReferredId",
                table: "ReferralInvites",
                column: "ReferredId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralInvites_ReferrerId",
                table: "ReferralInvites",
                column: "ReferrerId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferrerId",
                table: "Referrals",
                column: "ReferrerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReferralInvites");

            migrationBuilder.DropTable(
                name: "Referrals");

            migrationBuilder.DropColumn(
                name: "Approved",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "TestProfile",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "TrustRevoked",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Verified",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Moderator",
                table: "Accounts");
        }
    }
}
