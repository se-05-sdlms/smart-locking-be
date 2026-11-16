using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnPickupAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReturnPickupAttempt",
                columns: table => new
                {
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientKey = table.Column<string>(type: "character varying", maxLength: 64, nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    WindowExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnPickupAttempt", x => new { x.LockerId, x.ClientKey });
                    table.CheckConstraint("CK_ReturnPickupAttempt_FailedAttempts", "\"FailedAttempts\" >= 0 AND \"FailedAttempts\" <= 5");
                    table.ForeignKey(
                        name: "FK_ReturnPickupAttempt_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnPickupAttempt_WindowExpiresAt",
                table: "ReturnPickupAttempt",
                column: "WindowExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReturnPickupAttempt");
        }
    }
}
