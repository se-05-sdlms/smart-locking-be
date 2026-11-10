using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentReconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying", maxLength: 40, nullable: false),
                    ResidentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying", maxLength: 40, nullable: false),
                    AssignedAdminUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Response = table.Column<string>(type: "character varying", maxLength: 2000, nullable: true),
                    ProviderStatus = table.Column<string>(type: "character varying", maxLength: 40, nullable: true),
                    ProviderCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReconciliation", x => x.Id);
                    table.CheckConstraint("CK_PaymentReconciliation_Subject", "(\"SubscriptionId\" IS NOT NULL AND \"PaymentTransactionId\" IS NULL) OR (\"SubscriptionId\" IS NULL AND \"PaymentTransactionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PaymentReconciliation_PaymentTransaction_PaymentTransaction~",
                        column: x => x.PaymentTransactionId,
                        principalTable: "PaymentTransaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliation_ResidentSubscription_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "ResidentSubscription",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliation_User_AssignedAdminUserId",
                        column: x => x.AssignedAdminUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliation_User_ResidentUserId",
                        column: x => x.ResidentUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReconciliationEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentReconciliationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "character varying", maxLength: 2000, nullable: false),
                    ProviderEvidence = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReconciliationEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationEvent_PaymentReconciliation_PaymentRec~",
                        column: x => x.PaymentReconciliationId,
                        principalTable: "PaymentReconciliation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationEvent_User_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliation_PaymentTransactionId",
                table: "PaymentReconciliation",
                column: "PaymentTransactionId",
                unique: true,
                filter: "\"PaymentTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliation_Reference",
                table: "PaymentReconciliation",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliation_ResidentUserId_CreatedAt",
                table: "PaymentReconciliation",
                columns: new[] { "ResidentUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliation_SubscriptionId",
                table: "PaymentReconciliation",
                column: "SubscriptionId",
                unique: true,
                filter: "\"SubscriptionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationEvent_PaymentReconciliationId_CreatedAt",
                table: "PaymentReconciliationEvent",
                columns: new[] { "PaymentReconciliationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentReconciliationEvent");

            migrationBuilder.DropTable(
                name: "PaymentReconciliation");
        }
    }
}
