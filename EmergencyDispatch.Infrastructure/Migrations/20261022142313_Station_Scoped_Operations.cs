using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Station_Scoped_Operations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stations_HeadOperatorId",
                table: "Stations");

            migrationBuilder.AddColumn<double>(
                name: "DistanceOutsideAreaKm",
                table: "Incidents",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOutOfServiceArea",
                table: "Incidents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ResponsibleStationId",
                table: "Incidents",
                type: "uuid",
                nullable: true);

            // Dữ liệu cũ có thể có 1 Operator làm trưởng nhiều trạm → giữ trạm tạo sớm nhất trước khi tạo unique index
            migrationBuilder.Sql("""
                UPDATE "Stations" SET "HeadOperatorId" = NULL
                WHERE "Id" IN (
                    SELECT "Id" FROM (
                        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "HeadOperatorId" ORDER BY "CreatedAt") AS rn
                        FROM "Stations"
                        WHERE "HeadOperatorId" IS NOT NULL AND "IsDeleted" = false
                    ) d WHERE d.rn > 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "UQ_Stations_HeadOperator",
                table: "Stations",
                column: "HeadOperatorId",
                unique: true,
                filter: "\"HeadOperatorId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ResponsibleStation_Status",
                table: "Incidents",
                columns: new[] { "ResponsibleStationId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_Stations_ResponsibleStationId",
                table: "Incidents",
                column: "ResponsibleStationId",
                principalTable: "Stations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Sự cố đang mở: gán trạm phụ trách = trạm đang hoạt động gần nhất (khoảng cách phẳng xấp xỉ, đủ để xếp hạng trong 1 khu vực)
            migrationBuilder.Sql("""
                UPDATE "Incidents" i SET "ResponsibleStationId" = (
                    SELECT s."Id" FROM "Stations" s
                    WHERE s."IsDeleted" = false AND s."IsActive" = true
                    ORDER BY power(s."Latitude" - i."Latitude", 2)
                           + power((s."Longitude" - i."Longitude") * cos(radians(i."Latitude")), 2)
                    LIMIT 1)
                WHERE i."IsDeleted" = false AND i."ResponsibleStationId" IS NULL
                  AND i."Status" NOT IN (6, 7);
                """);

            // Operator đang là trưởng trạm → thuộc đúng trạm đó (claim StationId dùng để lọc theo trạm)
            migrationBuilder.Sql("""
                UPDATE "Users" u SET "StationId" = s."Id"
                FROM "Stations" s
                WHERE s."HeadOperatorId" = u."Id" AND s."IsDeleted" = false
                  AND (u."StationId" IS DISTINCT FROM s."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_Stations_ResponsibleStationId",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "UQ_Stations_HeadOperator",
                table: "Stations");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_ResponsibleStation_Status",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "DistanceOutsideAreaKm",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "IsOutOfServiceArea",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ResponsibleStationId",
                table: "Incidents");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_HeadOperatorId",
                table: "Stations",
                column: "HeadOperatorId");
        }
    }
}
