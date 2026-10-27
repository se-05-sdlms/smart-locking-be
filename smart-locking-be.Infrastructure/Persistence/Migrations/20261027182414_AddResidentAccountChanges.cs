using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentAccountChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResidentAccountChange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying", maxLength: 2000, nullable: false),
                    OriginalLockerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetLockerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginalPhoneNumber = table.Column<string>(type: "character varying", maxLength: 20, nullable: true),
                    NewPhoneNumber = table.Column<string>(type: "character varying", maxLength: 20, nullable: true),
                    PhoneVerificationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidentAccountChange", x => x.Id);
                    table.CheckConstraint("CK_ResidentAccountChange_Kind", "(\"Kind\" = 'Locker' AND \"TargetLockerId\" IS NOT NULL AND \"NewPhoneNumber\" IS NULL) OR (\"Kind\" = 'Phone' AND \"TargetLockerId\" IS NULL AND \"NewPhoneNumber\" IS NOT NULL AND \"PhoneVerificationExpiresAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ResidentAccountChange_Locker_OriginalLockerId",
                        column: x => x.OriginalLockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResidentAccountChange_Locker_TargetLockerId",
                        column: x => x.TargetLockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResidentAccountChange_User_ResidentUserId",
                        column: x => x.ResidentUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResidentAccountChange_User_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResidentAccountChangeEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentAccountChangeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "character varying", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidentAccountChangeEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResidentAccountChangeEvent_ResidentAccountChange_ResidentAc~",
                        column: x => x.ResidentAccountChangeId,
                        principalTable: "ResidentAccountChange",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResidentAccountChangeEvent_User_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResidentAccountChange_ResidentUserId_CreatedAt",
                table: "ResidentAccountChange",
                columns: new[] { "ResidentUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ResidentAccountChange_ResidentUserId_Kind",
                table: "ResidentAccountChange",
                columns: new[] { "ResidentUserId", "Kind" },
                unique: true,
                filter: "\"Status\" = 'Submitted'");

            migrationBuilder.CreateIndex(
                name: "IX_ResidentAccountChangeEvent_ResidentAccountChangeId_CreatedAt",
                table: "ResidentAccountChangeEvent",
                columns: new[] { "ResidentAccountChangeId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResidentAccountChangeEvent");

            migrationBuilder.DropTable(
                name: "ResidentAccountChange");
        }
    }
}
