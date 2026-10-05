using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnWorkflowNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReturnRequestId",
                table: "Notification",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notification_ReturnRequestId",
                table: "Notification",
                column: "ReturnRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notification_ReturnRequest_ReturnRequestId",
                table: "Notification",
                column: "ReturnRequestId",
                principalTable: "ReturnRequest",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notification_ReturnRequest_ReturnRequestId",
                table: "Notification");

            migrationBuilder.DropIndex(
                name: "IX_Notification_ReturnRequestId",
                table: "Notification");

            migrationBuilder.DropColumn(
                name: "ReturnRequestId",
                table: "Notification");
        }
    }
}
