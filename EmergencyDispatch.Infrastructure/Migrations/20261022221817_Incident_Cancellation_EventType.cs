using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Incident_Cancellation_EventType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationNote",
                table: "Incidents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventType",
                table: "Incidents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_CancelledByUserId",
                table: "Incidents",
                column: "CancelledByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_Users_CancelledByUserId",
                table: "Incidents",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // 1. Loại sự cố: mặc định lấy từ kết quả AI
            migrationBuilder.Sql("""
                UPDATE "Incidents" i SET "EventType" = a."EventType"
                FROM "AiClassifications" a
                WHERE a."IncidentId" = i."Id" AND i."EventType" = 0 AND a."EventType" <> 0;
                """);

            // 2. Sự cố bị hủy bằng code cũ: code cũ ghi người hủy vào VerifiedByUserId/VerifiedAt
            //    và ghi đè OperatorNotes = "[ĐÃ HỦY bởi ...]: lý do" → tách ra các cột hủy riêng
            migrationBuilder.Sql("""
                UPDATE "Incidents" i SET
                    "CancelledAt" = COALESCE(i."UpdatedAt", i."CreatedAt"),
                    "CancelledByUserId" = CASE WHEN i."OperatorNotes" LIKE '[ĐÃ HỦY%' THEN i."VerifiedByUserId" END,
                    "CancellationNote" = CASE WHEN i."OperatorNotes" LIKE '[ĐÃ HỦY%'
                        THEN left(substring(i."OperatorNotes" from position(']: ' in i."OperatorNotes") + 3), 500) END
                WHERE i."Status" = 7 AND i."CancelledAt" IS NULL;

                -- Citizen tự hủy → CitizenCancelled (3); còn lại → Other (5)
                UPDATE "Incidents" i SET "CancellationReason" = CASE WHEN u."Role" = 0 THEN 3 ELSE 5 END
                FROM "Users" u
                WHERE i."Status" = 7 AND i."CancellationReason" IS NULL AND u."Id" = i."CancelledByUserId";
                UPDATE "Incidents" SET "CancellationReason" = 5 WHERE "Status" = 7 AND "CancellationReason" IS NULL;
                """);

            // 3. Khôi phục mốc xác minh thật (RQ3) từ lịch sử trạng thái; sự cố chưa từng được xác minh thì để trống
            migrationBuilder.Sql("""
                UPDATE "Incidents" i SET
                    "VerifiedAt" = h."ChangedAt",
                    "VerifiedByUserId" = h."ChangedByUserId",
                    "OperatorNotes" = h."Notes"
                FROM (
                    SELECT DISTINCT ON ("IncidentId") "IncidentId", "ChangedAt", "ChangedByUserId", "Notes"
                    FROM "IncidentStatusHistories"
                    WHERE "NewStatus" = 3 AND "IsDeleted" = false
                    ORDER BY "IncidentId", "ChangedAt"
                ) h
                WHERE i."Id" = h."IncidentId" AND i."Status" = 7 AND i."OperatorNotes" LIKE '[ĐÃ HỦY%';

                UPDATE "Incidents" i SET "VerifiedAt" = NULL, "VerifiedByUserId" = NULL, "OperatorNotes" = NULL
                WHERE i."Status" = 7 AND i."OperatorNotes" LIKE '[ĐÃ HỦY%'
                  AND NOT EXISTS (SELECT 1 FROM "IncidentStatusHistories" h
                                  WHERE h."IncidentId" = i."Id" AND h."NewStatus" = 3 AND h."IsDeleted" = false);
                """);

            // 4. Xe đã chuyển trạm nhưng tọa độ vẫn ở trạm cũ: xe không làm nhiệm vụ, không có GPS mới (30 phút),
            //    cách trạm của mình xa hơn bán kính phủ → đưa về tọa độ trạm
            migrationBuilder.Sql("""
                UPDATE "RescueUnits" r SET
                    "CurrentLat" = s."Latitude",
                    "CurrentLng" = s."Longitude",
                    "LastLocationUpdateAt" = now()
                FROM "Stations" s
                WHERE r."StationId" = s."Id" AND r."IsDeleted" = false
                  AND r."Status" NOT IN (1, 2, 3)
                  AND (r."LastLocationUpdateAt" IS NULL OR r."LastLocationUpdateAt" < now() - interval '30 minutes')
                  AND (power(r."CurrentLat" - s."Latitude", 2)
                       + power((r."CurrentLng" - s."Longitude") * cos(radians(s."Latitude")), 2)) * 12392.1
                      > power(s."CoverageRadiusKm", 2);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_Users_CancelledByUserId",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_CancelledByUserId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "CancellationNote",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "EventType",
                table: "Incidents");
        }
    }
}
