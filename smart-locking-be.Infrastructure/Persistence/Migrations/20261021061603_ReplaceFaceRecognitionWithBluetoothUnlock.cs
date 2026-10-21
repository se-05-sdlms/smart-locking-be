using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFaceRecognitionWithBluetoothUnlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableFaceRecognition",
                table: "SystemPolicy");

            migrationBuilder.AddColumn<bool>(
                name: "EnableBluetoothUnlock",
                table: "SystemPolicy",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableBluetoothUnlock",
                table: "SystemPolicy");

            migrationBuilder.AddColumn<bool>(
                name: "EnableFaceRecognition",
                table: "SystemPolicy",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
