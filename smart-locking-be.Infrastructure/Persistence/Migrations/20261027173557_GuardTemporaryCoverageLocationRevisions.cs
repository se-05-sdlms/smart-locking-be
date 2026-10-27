using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GuardTemporaryCoverageLocationRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AnchorLocationRevision",
                table: "TemporaryOperatorAssignment",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetLocationRevision",
                table: "TemporaryOperatorAssignment",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnchorLocationRevision",
                table: "TemporaryOperatorAssignment");

            migrationBuilder.DropColumn(
                name: "TargetLocationRevision",
                table: "TemporaryOperatorAssignment");
        }
    }
}
