using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefactorV2Operations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemPolicy_DurationsAndRates",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "ShipperPhone",
                table: "ReturnRequest");

            migrationBuilder.DropColumn(
                name: "ShipperName",
                table: "DeliveryRequest");

            migrationBuilder.DropColumn(
                name: "ShipperPhone",
                table: "DeliveryRequest");

            migrationBuilder.AddColumn<int>(
                name: "PickupReminderStartDay",
                table: "SystemPolicy",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.Sql(
                "UPDATE \"SystemPolicy\" SET \"OverdueStartAfterHours\" = 168, \"MaxStorageHours\" = 336, \"PickupReminderStartDay\" = 5 WHERE \"IsActive\" = TRUE;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemPolicy_DurationsAndRates",
                table: "SystemPolicy",
                sql: "\"GuestSessionTimeoutMinutes\" > 0 AND \"ManualApprovalTimeoutMinutes\" > 0 AND \"CompartmentReservationMinutes\" > 0 AND \"OtpMaxAttempts\" > 0 AND \"OtpLockoutMinutes\" > 0 AND \"OverdueStartAfterHours\" >= 0 AND \"OverdueFeePerHour\" >= 0 AND \"MaxStorageHours\" > 0 AND \"ClearanceEligibilityAfterHours\" >= 0 AND \"ClearanceNoticeBeforeHours\" >= 0 AND \"PickupReminderStartDay\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemPolicy_DurationsAndRates",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "PickupReminderStartDay",
                table: "SystemPolicy");

            migrationBuilder.AddColumn<string>(
                name: "ShipperPhone",
                table: "ReturnRequest",
                type: "character varying",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipperName",
                table: "DeliveryRequest",
                type: "character varying",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipperPhone",
                table: "DeliveryRequest",
                type: "character varying",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemPolicy_DurationsAndRates",
                table: "SystemPolicy",
                sql: "\"GuestSessionTimeoutMinutes\" > 0 AND \"ManualApprovalTimeoutMinutes\" > 0 AND \"CompartmentReservationMinutes\" > 0 AND \"OtpMaxAttempts\" > 0 AND \"OtpLockoutMinutes\" > 0 AND \"OverdueStartAfterHours\" >= 0 AND \"OverdueFeePerHour\" >= 0 AND \"MaxStorageHours\" > 0 AND \"ClearanceEligibilityAfterHours\" >= 0 AND \"ClearanceNoticeBeforeHours\" >= 0");
        }
    }
}
