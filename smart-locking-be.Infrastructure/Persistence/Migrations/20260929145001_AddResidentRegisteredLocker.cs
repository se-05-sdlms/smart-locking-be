using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentRegisteredLocker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RegisteredLockerId",
                table: "ResidentProfile",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResidentProfile_RegisteredLockerId",
                table: "ResidentProfile",
                column: "RegisteredLockerId");

            migrationBuilder.AddForeignKey(
                name: "FK_ResidentProfile_Locker_RegisteredLockerId",
                table: "ResidentProfile",
                column: "RegisteredLockerId",
                principalTable: "Locker",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResidentProfile_Locker_RegisteredLockerId",
                table: "ResidentProfile");

            migrationBuilder.DropIndex(
                name: "IX_ResidentProfile_RegisteredLockerId",
                table: "ResidentProfile");

            migrationBuilder.DropColumn(
                name: "RegisteredLockerId",
                table: "ResidentProfile");
        }
    }
}
