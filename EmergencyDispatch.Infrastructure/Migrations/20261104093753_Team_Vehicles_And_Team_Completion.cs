using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Một tổ phụ trách nhiều xe (RescueUnits.StationTeamId thay cho StationTeams.RescueUnitId) và
    /// báo cáo nghiệm thu theo tổ (CompletionReports theo sự cố + tổ, DispatchAssignments.CompletionReportId).
    /// Thứ tự viết tay để chuyển dữ liệu trước khi xóa cột cũ.
    /// </summary>
    public partial class Team_Vehicles_And_Team_Completion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- 1. Tổ – xe: 1–1 → 1–nhiều ----------
            migrationBuilder.AddColumn<Guid>(
                name: "StationTeamId",
                table: "RescueUnits",
                type: "uuid",
                nullable: true);

            // Giữ liên kết tổ – xe hiện có
            migrationBuilder.Sql("""
                UPDATE "RescueUnits" AS r
                SET "StationTeamId" = t."Id"
                FROM "StationTeams" AS t
                WHERE t."RescueUnitId" = r."Id" AND t."IsDeleted" = false;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_StationTeams_RescueUnits_RescueUnitId",
                table: "StationTeams");

            migrationBuilder.DropIndex(
                name: "UQ_StationTeams_RescueUnit",
                table: "StationTeams");

            migrationBuilder.DropColumn(
                name: "RescueUnitId",
                table: "StationTeams");

            migrationBuilder.CreateIndex(
                name: "IX_RescueUnits_StationTeamId",
                table: "RescueUnits",
                column: "StationTeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_RescueUnits_StationTeams_StationTeamId",
                table: "RescueUnits",
                column: "StationTeamId",
                principalTable: "StationTeams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ---------- 2. Báo cáo nghiệm thu: theo xe → theo tổ ----------
            migrationBuilder.AddColumn<Guid>(
                name: "CompletionReportId",
                table: "DispatchAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IncidentId",
                table: "CompletionReports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StationTeamId",
                table: "CompletionReports",
                type: "uuid",
                nullable: true);

            // Báo cáo cũ (mỗi xe một báo cáo): lượt điều phối trỏ về báo cáo, báo cáo lấy sự cố của lượt; không gán tổ
            migrationBuilder.Sql("""
                UPDATE "DispatchAssignments" AS a
                SET "CompletionReportId" = c."Id"
                FROM "CompletionReports" AS c
                WHERE c."AssignmentId" = a."Id";

                UPDATE "CompletionReports" AS c
                SET "IncidentId" = a."IncidentId"
                FROM "DispatchAssignments" AS a
                WHERE a."Id" = c."AssignmentId";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_CompletionReports_DispatchAssignments_AssignmentId",
                table: "CompletionReports");

            migrationBuilder.DropIndex(
                name: "IX_CompletionReports_AssignmentId",
                table: "CompletionReports");

            migrationBuilder.DropColumn(
                name: "AssignmentId",
                table: "CompletionReports");

            migrationBuilder.AlterColumn<Guid>(
                name: "IncidentId",
                table: "CompletionReports",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchAssignments_CompletionReportId",
                table: "DispatchAssignments",
                column: "CompletionReportId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletionReports_StationTeamId",
                table: "CompletionReports",
                column: "StationTeamId");

            migrationBuilder.CreateIndex(
                name: "UQ_CompletionReports_Incident_Team",
                table: "CompletionReports",
                columns: new[] { "IncidentId", "StationTeamId" },
                unique: true,
                filter: "\"StationTeamId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_CompletionReports_Incidents_IncidentId",
                table: "CompletionReports",
                column: "IncidentId",
                principalTable: "Incidents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CompletionReports_StationTeams_StationTeamId",
                table: "CompletionReports",
                column: "StationTeamId",
                principalTable: "StationTeams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DispatchAssignments_CompletionReports_CompletionReportId",
                table: "DispatchAssignments",
                column: "CompletionReportId",
                principalTable: "CompletionReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ---------- 2. Báo cáo nghiệm thu: theo tổ → theo xe ----------
            migrationBuilder.DropForeignKey(
                name: "FK_CompletionReports_Incidents_IncidentId",
                table: "CompletionReports");

            migrationBuilder.DropForeignKey(
                name: "FK_CompletionReports_StationTeams_StationTeamId",
                table: "CompletionReports");

            migrationBuilder.DropForeignKey(
                name: "FK_DispatchAssignments_CompletionReports_CompletionReportId",
                table: "DispatchAssignments");

            migrationBuilder.DropIndex(
                name: "IX_DispatchAssignments_CompletionReportId",
                table: "DispatchAssignments");

            migrationBuilder.DropIndex(
                name: "IX_CompletionReports_StationTeamId",
                table: "CompletionReports");

            migrationBuilder.DropIndex(
                name: "UQ_CompletionReports_Incident_Team",
                table: "CompletionReports");

            migrationBuilder.AddColumn<Guid>(
                name: "AssignmentId",
                table: "CompletionReports",
                type: "uuid",
                nullable: true);

            // Báo cáo của tổ dùng chung nhiều xe: gắn lại với một lượt (lượt hoàn tất sớm nhất) — các xe còn lại mất liên kết báo cáo
            migrationBuilder.Sql("""
                UPDATE "CompletionReports" AS c
                SET "AssignmentId" = (
                    SELECT a."Id" FROM "DispatchAssignments" AS a
                    WHERE a."CompletionReportId" = c."Id"
                    ORDER BY a."CompletedAt" NULLS LAST, a."Id"
                    LIMIT 1);

                DELETE FROM "CompletionReports" WHERE "AssignmentId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignmentId",
                table: "CompletionReports",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "CompletionReportId",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "IncidentId",
                table: "CompletionReports");

            migrationBuilder.DropColumn(
                name: "StationTeamId",
                table: "CompletionReports");

            migrationBuilder.CreateIndex(
                name: "IX_CompletionReports_AssignmentId",
                table: "CompletionReports",
                column: "AssignmentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CompletionReports_DispatchAssignments_AssignmentId",
                table: "CompletionReports",
                column: "AssignmentId",
                principalTable: "DispatchAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // ---------- 1. Tổ – xe: 1–nhiều → 1–1 ----------
            migrationBuilder.DropForeignKey(
                name: "FK_RescueUnits_StationTeams_StationTeamId",
                table: "RescueUnits");

            migrationBuilder.DropIndex(
                name: "IX_RescueUnits_StationTeamId",
                table: "RescueUnits");

            migrationBuilder.AddColumn<Guid>(
                name: "RescueUnitId",
                table: "StationTeams",
                type: "uuid",
                nullable: true);

            // Tổ nhiều xe: giữ lại một xe (biển số nhỏ nhất) — các xe còn lại mất liên kết tổ
            migrationBuilder.Sql("""
                UPDATE "StationTeams" AS t
                SET "RescueUnitId" = (
                    SELECT r."Id" FROM "RescueUnits" AS r
                    WHERE r."StationTeamId" = t."Id" AND r."IsDeleted" = false
                    ORDER BY r."PlateNumber"
                    LIMIT 1);
                """);

            migrationBuilder.DropColumn(
                name: "StationTeamId",
                table: "RescueUnits");

            migrationBuilder.CreateIndex(
                name: "UQ_StationTeams_RescueUnit",
                table: "StationTeams",
                column: "RescueUnitId",
                unique: true,
                filter: "\"RescueUnitId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_StationTeams_RescueUnits_RescueUnitId",
                table: "StationTeams",
                column: "RescueUnitId",
                principalTable: "RescueUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
