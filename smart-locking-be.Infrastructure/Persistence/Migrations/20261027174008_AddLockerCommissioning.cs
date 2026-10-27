using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLockerCommissioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LockerCommissioning",
                columns: table => new
                {
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying", maxLength: 30, nullable: false),
                    BoundDeviceIdentifier = table.Column<string>(type: "character varying", maxLength: 100, nullable: true),
                    FirmwareVersion = table.Column<string>(type: "character varying", maxLength: 100, nullable: true),
                    FirmwareHardwareModel = table.Column<string>(type: "character varying", maxLength: 100, nullable: true),
                    FirmwareSha256 = table.Column<string>(type: "character varying", maxLength: 64, nullable: true),
                    VerifiedConfigurationHash = table.Column<string>(type: "character varying", maxLength: 64, nullable: true),
                    BoundAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfiguredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    HandedOverAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HandedOverByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockerCommissioning", x => x.LockerId);
                    table.ForeignKey(
                        name: "FK_LockerCommissioning_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerCommissioning_User_HandedOverByUserId",
                        column: x => x.HandedOverByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerCommissioning_User_VerifiedByUserId",
                        column: x => x.VerifiedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LockerCommissioning");
        }
    }
}
