using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveOperatorPerLocker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OperatorAssignment_ActiveLocker",
                table: "OperatorAssignment");

            migrationBuilder.CreateIndex(
                name: "UX_OperatorAssignment_ActiveLocker",
                table: "OperatorAssignment",
                column: "LockerId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OperatorAssignment_ActiveLocker",
                table: "OperatorAssignment");

            migrationBuilder.CreateIndex(
                name: "UX_OperatorAssignment_ActiveLocker",
                table: "OperatorAssignment",
                columns: new[] { "OperatorUserId", "LockerId" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");
        }
    }
}
