using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemovePersonalQr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ResidentProfile_PersonalQrTokenHash",
                table: "ResidentProfile");

            migrationBuilder.DropColumn(
                name: "EnablePersonalQr",
                table: "SystemPolicy");

            migrationBuilder.DropColumn(
                name: "PersonalQrIssuedAt",
                table: "ResidentProfile");

            migrationBuilder.DropColumn(
                name: "PersonalQrTokenHash",
                table: "ResidentProfile");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnablePersonalQr",
                table: "SystemPolicy",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PersonalQrIssuedAt",
                table: "ResidentProfile",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonalQrTokenHash",
                table: "ResidentProfile",
                type: "character varying",
                maxLength: 256,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "ResidentProfile"
                SET "PersonalQrTokenHash" = md5(random()::text || clock_timestamp()::text || "Id"::text),
                    "PersonalQrIssuedAt" = CURRENT_TIMESTAMP;
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PersonalQrIssuedAt",
                table: "ResidentProfile",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PersonalQrTokenHash",
                table: "ResidentProfile",
                type: "character varying",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_ResidentProfile_PersonalQrTokenHash",
                table: "ResidentProfile",
                column: "PersonalQrTokenHash",
                unique: true);
        }
    }
}
