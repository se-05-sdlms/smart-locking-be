using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBluetoothOfflineWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableBluetoothUnlock",
                table: "SystemPolicy",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "OfflineLockerCursor",
                columns: table => new
                {
                    DeviceIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineLockerCursor", x => x.DeviceIdentifier);
                });

            migrationBuilder.CreateTable(
                name: "OfflineLockerEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    PinCode = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HardwareChannel = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    RecipientPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ElapsedMs = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReviewReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineLockerEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfflineLockerPin",
                columns: table => new
                {
                    ParcelId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PinCode = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProvisionedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineLockerPin", x => x.ParcelId);
                    table.ForeignKey(
                        name: "FK_OfflineLockerPin_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfflineLockerPin_Parcel_ParcelId",
                        column: x => x.ParcelId,
                        principalTable: "Parcel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineLockerEvent_DeviceIdentifier_DeliveryId_Kind",
                table: "OfflineLockerEvent",
                columns: new[] { "DeviceIdentifier", "DeliveryId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineLockerEvent_DeviceIdentifier_Sequence",
                table: "OfflineLockerEvent",
                columns: new[] { "DeviceIdentifier", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfflineLockerEvent_Status_ReceivedAt",
                table: "OfflineLockerEvent",
                columns: new[] { "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineLockerPin_DeliveryId",
                table: "OfflineLockerPin",
                column: "DeliveryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfflineLockerCursor");

            migrationBuilder.DropTable(
                name: "OfflineLockerEvent");

            migrationBuilder.DropTable(
                name: "OfflineLockerPin");

            migrationBuilder.DropColumn(
                name: "EnableBluetoothUnlock",
                table: "SystemPolicy");
        }
    }
}
