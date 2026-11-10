using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOtpDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DestinationEmail",
                table: "OtpChallenge",
                type: "character varying",
                maxLength: 254,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenge_Email_Purpose_CreatedAt",
                table: "OtpChallenge",
                columns: new[] { "DestinationEmail", "Purpose", "CreatedAt" },
                filter: "\"DestinationEmail\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OtpChallenge_Email_Purpose_CreatedAt",
                table: "OtpChallenge");

            migrationBuilder.DropColumn(
                name: "DestinationEmail",
                table: "OtpChallenge");
        }
    }
}
