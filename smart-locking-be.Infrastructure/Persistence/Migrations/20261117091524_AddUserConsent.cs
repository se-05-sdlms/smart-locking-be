using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsentedAt",
                table: "User",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrivacyVersion",
                table: "User",
                type: "character varying",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "User",
                type: "character varying",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsentedAt",
                table: "User");

            migrationBuilder.DropColumn(
                name: "PrivacyVersion",
                table: "User");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "User");
        }
    }
}
