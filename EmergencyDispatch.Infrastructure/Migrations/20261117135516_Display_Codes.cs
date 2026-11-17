using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Display_Codes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "IncidentNumbers");

            migrationBuilder.CreateSequence(
                name: "StaffNumbers");

            migrationBuilder.AddColumn<int>(
                name: "StaffNumber",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Incidents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "Number",
                table: "Incidents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Cấp số + mã cho sự cố cũ theo thứ tự báo (kể cả đã xóa mềm), rồi đẩy sequence qua số lớn nhất
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT "Id", "CreatedAt", ROW_NUMBER() OVER (ORDER BY "CreatedAt", "Id") AS n FROM "Incidents"
                )
                UPDATE "Incidents" i
                SET "Number" = numbered.n,
                    "Code" = 'SC-' || to_char(numbered."CreatedAt" AT TIME ZONE 'Asia/Ho_Chi_Minh', 'YYMMDD') || '-' || lpad(numbered.n::text, 4, '0')
                FROM numbered
                WHERE i."Id" = numbered."Id";

                SELECT setval('"IncidentNumbers"', GREATEST((SELECT COALESCE(MAX("Number"), 0) FROM "Incidents"), 1), (SELECT COUNT(*) > 0 FROM "Incidents"));
                """);

            // Số nhân sự cho tài khoản không phải người dân (Role 0 = Citizen) theo thứ tự tạo
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT "Id", ROW_NUMBER() OVER (ORDER BY "CreatedAt", "Id") AS n FROM "Users" WHERE "Role" <> 0
                )
                UPDATE "Users" u SET "StaffNumber" = numbered.n FROM numbered WHERE u."Id" = numbered."Id";

                SELECT setval('"StaffNumbers"', GREATEST((SELECT COALESCE(MAX("StaffNumber"), 0) FROM "Users"), 1), (SELECT COUNT(*) > 0 FROM "Users" WHERE "StaffNumber" IS NOT NULL));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_StaffNumber",
                table: "Users",
                column: "StaffNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Code",
                table: "Incidents",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Number",
                table: "Incidents",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_StaffNumber",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_Code",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_Number",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "StaffNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Incidents");

            migrationBuilder.DropSequence(
                name: "IncidentNumbers");

            migrationBuilder.DropSequence(
                name: "StaffNumbers");
        }
    }
}
