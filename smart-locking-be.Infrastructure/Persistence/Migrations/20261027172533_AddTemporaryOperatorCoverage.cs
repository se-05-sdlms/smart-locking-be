using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryOperatorCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "OperatorReplacementAreas",
                table: "SystemPolicy",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "OperatorReplacementEligibleUserIds",
                table: "SystemPolicy",
                type: "uuid[]",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OperatorReplacementMaxLoad",
                table: "SystemPolicy",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OperatorReplacementRadiusKm",
                table: "SystemPolicy",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Locker",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LocationConfirmedAt",
                table: "Locker",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationConfirmedByUserId",
                table: "Locker",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationRevision",
                table: "Locker",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Locker",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceArea",
                table: "Locker",
                type: "character varying",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperatorAbsence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorAbsence", x => x.Id);
                    table.CheckConstraint("CK_OperatorAbsence_Interval", "\"StartsAt\" < \"EndsAt\"");
                    table.ForeignKey(
                        name: "FK_OperatorAbsence_User_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorAbsence_User_OperatorUserId",
                        column: x => x.OperatorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemporaryOperatorAssignment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AbsenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AnchorLockerId = table.Column<Guid>(type: "uuid", nullable: true),
                    DistanceKm = table.Column<double>(type: "double precision", nullable: true),
                    LoadBefore = table.Column<int>(type: "integer", nullable: true),
                    LoadAfter = table.Column<int>(type: "integer", nullable: true),
                    ServiceArea = table.Column<string>(type: "character varying", maxLength: 100, nullable: true),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectionFingerprint = table.Column<string>(type: "character varying", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying", maxLength: 1000, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryOperatorAssignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemporaryOperatorAssignment_Locker_AnchorLockerId",
                        column: x => x.AnchorLockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryOperatorAssignment_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryOperatorAssignment_OperatorAbsence_AbsenceId",
                        column: x => x.AbsenceId,
                        principalTable: "OperatorAbsence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryOperatorAssignment_OperatorAssignment_BaseAssignme~",
                        column: x => x.BaseAssignmentId,
                        principalTable: "OperatorAssignment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryOperatorAssignment_SystemPolicy_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "SystemPolicy",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryOperatorAssignment_User_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TemporaryOperatorAssignment_User_OperatorUserId",
                        column: x => x.OperatorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Locker_Coordinates",
                table: "Locker",
                sql: "(\"Latitude\" IS NULL AND \"Longitude\" IS NULL) OR (\"Latitude\" BETWEEN -90 AND 90 AND \"Longitude\" BETWEEN -180 AND 180)");

            migrationBuilder.CreateIndex(
                name: "IX_OperatorAbsence_OperatorUserId_StartsAt_EndsAt",
                table: "OperatorAbsence",
                columns: new[] { "OperatorUserId", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryOperatorAssignment_AbsenceId_LockerId",
                table: "TemporaryOperatorAssignment",
                columns: new[] { "AbsenceId", "LockerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryOperatorAssignment_LockerId_Status",
                table: "TemporaryOperatorAssignment",
                columns: new[] { "LockerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryOperatorAssignment_OperatorUserId_Status",
                table: "TemporaryOperatorAssignment",
                columns: new[] { "OperatorUserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Locker_User_LocationConfirmedByUserId",
                table: "Locker",
                column: "LocationConfirmedByUserId",
                principalTable: "User",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Locker_User_LocationConfirmedByUserId",
                table: "Locker");

            migrationBuilder.DropTable(
                name: "TemporaryOperatorAssignment");

            migrationBuilder.DropTable(
                name: "OperatorAbsence");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Locker_Coordinates",
                table: "Locker");

            migrationBuilder.DropColumn(
                name: "OperatorReplacementAreas",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "OperatorReplacementEligibleUserIds",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "OperatorReplacementMaxLoad",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "OperatorReplacementRadiusKm",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Locker");

            migrationBuilder.DropColumn(
                name: "LocationConfirmedAt",
                table: "Locker");

            migrationBuilder.DropColumn(
                name: "LocationConfirmedByUserId",
                table: "Locker");

            migrationBuilder.DropColumn(
                name: "LocationRevision",
                table: "Locker");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Locker");

            migrationBuilder.DropColumn(
                name: "ServiceArea",
                table: "Locker");
        }
    }
}
