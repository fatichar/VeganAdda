using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class QueueProfileReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfileReminderBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Completed = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileReminderBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileReminderBatches_Accounts_AdminId",
                        column: x => x.AdminId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileReminderDeliveries",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileReminderDeliveries", x => new { x.BatchId, x.MemberId });
                    table.ForeignKey(
                        name: "FK_ProfileReminderDeliveries_ProfileReminderBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ProfileReminderBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileReminderBatches_AdminId_Created",
                table: "ProfileReminderBatches",
                columns: new[] { "AdminId", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileReminderBatches_AdminId_RequestId",
                table: "ProfileReminderBatches",
                columns: new[] { "AdminId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileReminderBatches_Completed",
                table: "ProfileReminderBatches",
                column: "Completed");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileReminderDeliveries_MemberId_Status",
                table: "ProfileReminderDeliveries",
                columns: new[] { "MemberId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileReminderDeliveries_Status_AvailableAt",
                table: "ProfileReminderDeliveries",
                columns: new[] { "Status", "AvailableAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileReminderDeliveries");

            migrationBuilder.DropTable(
                name: "ProfileReminderBatches");
        }
    }
}
