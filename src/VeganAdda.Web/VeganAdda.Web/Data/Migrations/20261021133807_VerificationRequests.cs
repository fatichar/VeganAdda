using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class VerificationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VerificationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EvidenceUrls = table.Column<string[]>(type: "text[]", nullable: false),
                    ReferrerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferralStatus = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Submitted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Responded = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Resolved = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Feedback = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationRequests_Accounts_ApplicantId",
                        column: x => x.ApplicantId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VerificationRequests_Accounts_ReferrerId",
                        column: x => x.ReferrerId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationRequests_ApplicantId",
                table: "VerificationRequests",
                column: "ApplicantId",
                unique: true,
                filter: "\"Status\" = 'Under review'");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationRequests_ApplicantId_ClientId",
                table: "VerificationRequests",
                columns: new[] { "ApplicantId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationRequests_ReferrerId",
                table: "VerificationRequests",
                column: "ReferrerId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationRequests_Status_Submitted",
                table: "VerificationRequests",
                columns: new[] { "Status", "Submitted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VerificationRequests");
        }
    }
}
