using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreservePurchasedPlanBenefits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BenefitsSnapshotRecordedAt",
                table: "ResidentSubscription",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationDaysSnapshot",
                table: "ResidentSubscription",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxActiveParcelsSnapshot",
                table: "ResidentSubscription",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanCodeSnapshot",
                table: "ResidentSubscription",
                type: "character varying",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanNameSnapshot",
                table: "ResidentSubscription",
                type: "character varying",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BenefitsSnapshotRecordedAt",
                table: "ResidentSubscription");

            migrationBuilder.DropColumn(
                name: "DurationDaysSnapshot",
                table: "ResidentSubscription");

            migrationBuilder.DropColumn(
                name: "MaxActiveParcelsSnapshot",
                table: "ResidentSubscription");

            migrationBuilder.DropColumn(
                name: "PlanCodeSnapshot",
                table: "ResidentSubscription");

            migrationBuilder.DropColumn(
                name: "PlanNameSnapshot",
                table: "ResidentSubscription");
        }
    }
}
