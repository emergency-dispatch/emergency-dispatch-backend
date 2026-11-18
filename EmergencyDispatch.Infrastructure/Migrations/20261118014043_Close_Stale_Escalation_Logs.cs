using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Close_Stale_Escalation_Logs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Đóng log leo thang còn mở của sự cố không còn ở trạng thái Escalated (8): đã xác minh, đã điều xe, đã đóng hoặc đã hủy.
            // Thời điểm đóng = lần đầu sự cố rời Escalated sau khi log được mở (lịch sử trạng thái); không có thì lấy UpdatedAt của sự cố
            migrationBuilder.Sql("""
                WITH stale AS (
                    SELECT l."Id",
                           COALESCE(
                               (SELECT MIN(h."ChangedAt") FROM "IncidentStatusHistories" h
                                WHERE h."IncidentId" = l."IncidentId" AND h."OldStatus" = 8 AND h."NewStatus" <> 8
                                  AND h."ChangedAt" >= l."TriggerTime"),
                               GREATEST(COALESCE(i."UpdatedAt", i."CreatedAt"), l."TriggerTime")) AS resolved
                    FROM "EscalationLogs" l
                    JOIN "Incidents" i ON i."Id" = l."IncidentId"
                    WHERE l."ResolvedAt" IS NULL AND i."Status" <> 8
                )
                UPDATE "EscalationLogs" l
                SET "ResolvedAt" = stale.resolved,
                    "ResolutionTimeMs" = (EXTRACT(EPOCH FROM (stale.resolved - l."TriggerTime")) * 1000)::bigint
                FROM stale
                WHERE l."Id" = stale."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Chỉ sửa dữ liệu lệch, không hoàn tác
        }
    }
}
