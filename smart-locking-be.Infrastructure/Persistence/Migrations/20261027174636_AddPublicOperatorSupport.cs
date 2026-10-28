using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicOperatorSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LockerSupportSettings",
                columns: table => new
                {
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FallbackName = table.Column<string>(type: "character varying", maxLength: 200, nullable: true),
                    FallbackPhoneNumber = table.Column<string>(type: "character varying", maxLength: 16, nullable: true),
                    FallbackInstructions = table.Column<string>(type: "character varying", maxLength: 1000, nullable: false),
                    FallbackHours = table.Column<string>(type: "character varying", maxLength: 200, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LockerSupportSettings", x => x.LockerId);
                    table.ForeignKey(
                        name: "FK_LockerSupportSettings_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LockerSupportSettings_User_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperatorSupportContact",
                columns: table => new
                {
                    OperatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying", maxLength: 200, nullable: false),
                    WorkPhoneNumber = table.Column<string>(type: "character varying", maxLength: 16, nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    UtcOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    WeekdaysMask = table.Column<int>(type: "integer", nullable: false),
                    StartsAtMinute = table.Column<int>(type: "integer", nullable: false),
                    EndsAtMinute = table.Column<int>(type: "integer", nullable: false),
                    Instructions = table.Column<string>(type: "character varying", maxLength: 1000, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorSupportContact", x => x.OperatorUserId);
                    table.ForeignKey(
                        name: "FK_OperatorSupportContact_User_OperatorUserId",
                        column: x => x.OperatorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorSupportContact_User_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LockerSupportSettings");

            migrationBuilder.DropTable(
                name: "OperatorSupportContact");
        }
    }
}
