using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Station_Code : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DB thực tế có thể không có IX_Stations_Name (schema lệch snapshot) → không được để DROP INDEX làm hỏng cả lô migration
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Stations_Name";""");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Stations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Gán mã cho trạm đã có (theo thứ tự tạo) trước khi bật unique index
            migrationBuilder.Sql("""
                UPDATE "Stations" AS s
                SET "Code" = 'ST-' || lpad(r.rn::text, 3, '0')
                FROM (SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS rn FROM "Stations") AS r
                WHERE s."Id" = r."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Stations_Code",
                table: "Stations",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_Name",
                table: "Stations",
                column: "Name",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stations_Code",
                table: "Stations");

            migrationBuilder.DropIndex(
                name: "IX_Stations_Name",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Stations");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_Name",
                table: "Stations",
                column: "Name",
                unique: true);
        }
    }
}
