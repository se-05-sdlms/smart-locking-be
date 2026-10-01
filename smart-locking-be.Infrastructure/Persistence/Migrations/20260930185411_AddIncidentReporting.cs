using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceUrl",
                table: "Incident",
                type: "character varying",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnRequestId",
                table: "Incident",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incident_ReturnRequestId",
                table: "Incident",
                column: "ReturnRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_Incident_ReturnRequest_ReturnRequestId",
                table: "Incident",
                column: "ReturnRequestId",
                principalTable: "ReturnRequest",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incident_ReturnRequest_ReturnRequestId",
                table: "Incident");

            migrationBuilder.DropIndex(
                name: "IX_Incident_ReturnRequestId",
                table: "Incident");

            migrationBuilder.DropColumn(
                name: "EvidenceUrl",
                table: "Incident");

            migrationBuilder.DropColumn(
                name: "ReturnRequestId",
                table: "Incident");
        }
    }
}
