using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_TrafficJamStatus_To_AiClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrafficJamEvidence",
                table: "AiClassifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrafficJamStatus",
                table: "AiClassifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrafficJamVerifiedAt",
                table: "AiClassifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TrafficJamVerifiedByUserId",
                table: "AiClassifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiClassifications_TrafficJamVerifiedByUserId",
                table: "AiClassifications",
                column: "TrafficJamVerifiedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AiClassifications_Users_TrafficJamVerifiedByUserId",
                table: "AiClassifications",
                column: "TrafficJamVerifiedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Chuyển đổi an toàn dữ liệu lịch sử theo đúng quy tắc nghiệp vụ:
            // 1. Bản ghi cũ có IsTrafficJam = true (chưa có con người thẩm định) -> Suspected (1)
            migrationBuilder.Sql("""
                UPDATE "AiClassifications"
                SET "TrafficJamStatus" = 1,
                    "TrafficJamEvidence" = '[Dữ liệu lịch sử] AI tự động cảnh báo kẹt xe, chưa qua thẩm định của Điều phối viên.'
                WHERE "IsTrafficJam" = TRUE;
                """);

            // 2. Bản ghi AI lỗi -> Unverified (5)
            migrationBuilder.Sql("""
                UPDATE "AiClassifications"
                SET "TrafficJamStatus" = 5,
                    "TrafficJamEvidence" = '[Dữ liệu lịch sử] Phân tích AI trước đây thất bại hoặc chưa hoàn tất.'
                WHERE "IsSuccess" = FALSE;
                """);

            // 3. Bản ghi bình thường -> NoEvidence (0)
            migrationBuilder.Sql("""
                UPDATE "AiClassifications"
                SET "TrafficJamStatus" = 0,
                    "TrafficJamEvidence" = '[Dữ liệu lịch sử] Không ghi nhận cảnh báo kẹt xe từ AI.'
                WHERE "IsTrafficJam" = FALSE AND "IsSuccess" = TRUE;
                """);

            // 4. QUY ƯỚC AN TOÀN: Reset IsTrafficJam = FALSE cho tất cả bản ghi cũ vì chưa có con người nào Confirmed
            migrationBuilder.Sql("""
                UPDATE "AiClassifications"
                SET "IsTrafficJam" = FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiClassifications_Users_TrafficJamVerifiedByUserId",
                table: "AiClassifications");

            migrationBuilder.DropIndex(
                name: "IX_AiClassifications_TrafficJamVerifiedByUserId",
                table: "AiClassifications");

            migrationBuilder.DropColumn(
                name: "TrafficJamEvidence",
                table: "AiClassifications");

            migrationBuilder.DropColumn(
                name: "TrafficJamStatus",
                table: "AiClassifications");

            migrationBuilder.DropColumn(
                name: "TrafficJamVerifiedAt",
                table: "AiClassifications");

            migrationBuilder.DropColumn(
                name: "TrafficJamVerifiedByUserId",
                table: "AiClassifications");
        }
    }
}
