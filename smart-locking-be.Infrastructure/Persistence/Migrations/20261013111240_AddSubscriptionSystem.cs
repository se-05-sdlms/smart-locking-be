using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionPlan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    MaxActiveParcels = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlan", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionPlan_PriceAndDuration", "\"Price\" >= 0 AND \"DurationDays\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "ResidentSubscription",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying", nullable: false),
                    PriceSnapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExternalOrderCode = table.Column<string>(type: "character varying", maxLength: 100, nullable: true),
                    ExternalTransactionId = table.Column<string>(type: "character varying", maxLength: 200, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidentSubscription", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResidentSubscription_ResidentProfile_ResidentProfileId",
                        column: x => x.ResidentProfileId,
                        principalTable: "ResidentProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResidentSubscription_SubscriptionPlan_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResidentSubscription_ResidentProfileId",
                table: "ResidentSubscription",
                column: "ResidentProfileId");

            migrationBuilder.CreateIndex(
                name: "UX_ResidentSubscription_ExternalOrderCode",
                table: "ResidentSubscription",
                column: "ExternalOrderCode",
                unique: true,
                filter: "\"ExternalOrderCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_SubscriptionPlan_Code",
                table: "SubscriptionPlan",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResidentSubscription");

            migrationBuilder.DropTable(
                name: "SubscriptionPlan");
        }
    }
}
