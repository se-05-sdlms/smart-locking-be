using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace smart_locking_be.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFaceRecognitionWithBluetoothUnlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableFaceRecognition",
                table: "SystemPolicy");

            // Giữ giá trị nếu workflow offline đã tạo cột trên nhánh feature trước đó.
            migrationBuilder.Sql("ALTER TABLE \"SystemPolicy\" ADD COLUMN IF NOT EXISTS \"EnableBluetoothUnlock\" boolean NOT NULL DEFAULT FALSE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Khi rollback về migration offline trước đó, cột Bluetooth vẫn thuộc target model.
            // DB chỉ từng chạy policy (#149), chưa có workflow offline, thì mới bỏ cột này.
            migrationBuilder.Sql("""
                DO $block$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                        WHERE "MigrationId" = '20261018115426_AddBluetoothOfflineWorkflow') THEN
                        ALTER TABLE "SystemPolicy" DROP COLUMN IF EXISTS "EnableBluetoothUnlock";
                    END IF;
                END $block$;
                """);

            migrationBuilder.AddColumn<bool>(
                name: "EnableFaceRecognition",
                table: "SystemPolicy",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
