using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkDeliveryRequestNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryRequestId",
                table: "Notification",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notification_DeliveryRequestId",
                table: "Notification",
                column: "DeliveryRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notification_DeliveryRequest_DeliveryRequestId",
                table: "Notification",
                column: "DeliveryRequestId",
                principalTable: "DeliveryRequest",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notification_DeliveryRequest_DeliveryRequestId",
                table: "Notification");

            migrationBuilder.DropIndex(
                name: "IX_Notification_DeliveryRequestId",
                table: "Notification");

            migrationBuilder.DropColumn(
                name: "DeliveryRequestId",
                table: "Notification");
        }
    }
}
