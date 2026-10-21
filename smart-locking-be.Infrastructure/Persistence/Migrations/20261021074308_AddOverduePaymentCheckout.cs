using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOverduePaymentCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutUrl",
                table: "PaymentTransaction",
                type: "character varying",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_PaymentTransaction_PendingCharge",
                table: "PaymentTransaction",
                column: "OverdueChargeId",
                unique: true,
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_PaymentTransaction_PendingCharge",
                table: "PaymentTransaction");

            migrationBuilder.DropColumn(
                name: "CheckoutUrl",
                table: "PaymentTransaction");
        }
    }
}
