using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentSlaPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IncidentCriticalAcknowledgeMinutes",
                table: "SystemPolicy",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "IncidentCriticalResolveMinutes",
                table: "SystemPolicy",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "IncidentHighAcknowledgeMinutes",
                table: "SystemPolicy",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "IncidentHighResolveMinutes",
                table: "SystemPolicy",
                type: "integer",
                nullable: false,
                defaultValue: 240);

            migrationBuilder.AddColumn<int>(
                name: "IncidentLowAcknowledgeMinutes",
                table: "SystemPolicy",
                type: "integer",
                nullable: false,
                defaultValue: 480);

            migrationBuilder.AddColumn<int>(
                name: "IncidentLowResolveMinutes",
                table: "SystemPolicy",
                type: "integer",
                nullable: false,
                defaultValue: 4320);

            migrationBuilder.AddColumn<int>(
                name: "IncidentMediumAcknowledgeMinutes",
                table: "SystemPolicy",
                type: "integer",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<int>(
                name: "IncidentMediumResolveMinutes",
                table: "SystemPolicy",
                type: "integer",
                nullable: false,
                defaultValue: 1440);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SystemPolicy_IncidentSla",
                table: "SystemPolicy",
                sql: "\"IncidentCriticalAcknowledgeMinutes\" > 0 AND \"IncidentCriticalResolveMinutes\" >= \"IncidentCriticalAcknowledgeMinutes\" AND \"IncidentHighAcknowledgeMinutes\" > 0 AND \"IncidentHighResolveMinutes\" >= \"IncidentHighAcknowledgeMinutes\" AND \"IncidentMediumAcknowledgeMinutes\" > 0 AND \"IncidentMediumResolveMinutes\" >= \"IncidentMediumAcknowledgeMinutes\" AND \"IncidentLowAcknowledgeMinutes\" > 0 AND \"IncidentLowResolveMinutes\" >= \"IncidentLowAcknowledgeMinutes\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SystemPolicy_IncidentSla",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "IncidentCriticalAcknowledgeMinutes",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "IncidentCriticalResolveMinutes",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "IncidentHighAcknowledgeMinutes",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "IncidentHighResolveMinutes",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "IncidentLowAcknowledgeMinutes",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "IncidentLowResolveMinutes",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "IncidentMediumAcknowledgeMinutes",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "IncidentMediumResolveMinutes",
                table: "SystemPolicy");
        }
    }
}
