using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppendOnlyOperatorTaskHandovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperatorTaskHandover",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskKind = table.Column<string>(type: "character varying", maxLength: 20, nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorName = table.Column<string>(type: "character varying", maxLength: 320, nullable: false),
                    IntendedReceiverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkDone = table.Column<string>(type: "character varying", maxLength: 2000, nullable: false),
                    Blockers = table.Column<string>(type: "character varying", maxLength: 2000, nullable: false),
                    NextSteps = table.Column<string>(type: "character varying", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorTaskHandover", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorTaskHandover_Locker_LockerId",
                        column: x => x.LockerId,
                        principalTable: "Locker",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorTaskHandover_User_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorTaskHandover_User_IntendedReceiverUserId",
                        column: x => x.IntendedReceiverUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperatorTaskAcknowledgement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HandoverId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiverUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiverName = table.Column<string>(type: "character varying", maxLength: 320, nullable: false),
                    Notes = table.Column<string>(type: "character varying", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorTaskAcknowledgement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatorTaskAcknowledgement_OperatorTaskHandover_HandoverId",
                        column: x => x.HandoverId,
                        principalTable: "OperatorTaskHandover",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperatorTaskAcknowledgement_User_ReceiverUserId",
                        column: x => x.ReceiverUserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                CREATE FUNCTION reject_operator_task_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $history$
                BEGIN
                    RAISE EXCEPTION 'Operator task handover history is append-only';
                END;
                $history$;
                CREATE TRIGGER operator_task_handover_immutable BEFORE UPDATE OR DELETE ON "OperatorTaskHandover"
                FOR EACH ROW EXECUTE FUNCTION reject_operator_task_history_mutation();
                CREATE TRIGGER operator_task_acknowledgement_immutable BEFORE UPDATE OR DELETE ON "OperatorTaskAcknowledgement"
                FOR EACH ROW EXECUTE FUNCTION reject_operator_task_history_mutation();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_OperatorTaskAcknowledgement_HandoverId_ReceiverUserId",
                table: "OperatorTaskAcknowledgement",
                columns: new[] { "HandoverId", "ReceiverUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatorTaskHandover_TaskKind_TaskId_CreatedAt",
                table: "OperatorTaskHandover",
                columns: new[] { "TaskKind", "TaskId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperatorTaskAcknowledgement");

            migrationBuilder.DropTable(
                name: "OperatorTaskHandover");
            migrationBuilder.Sql("DROP FUNCTION reject_operator_task_history_mutation()");
        }
    }
}
