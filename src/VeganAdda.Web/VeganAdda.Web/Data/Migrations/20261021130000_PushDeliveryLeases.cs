using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class PushDeliveryLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PushLeaseId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PushLeaseUntil",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PushDeliveries",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDeliveries", x => new { x.MessageId, x.DeviceId });
                    table.ForeignKey(
                        name: "FK_PushDeliveries_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PushDeliveries_PushDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "PushDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_PendingPush",
                table: "Messages",
                column: "Id",
                filter: "\"PushPending\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PushDeliveries_DeviceId",
                table: "PushDeliveries",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_Messages_PendingPush",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "PushLeaseId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "PushLeaseUntil",
                table: "Messages");
        }
    }
}
