using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Reconcile_Legacy_Schema_And_Dispatch_Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DB thực tế (Neon) đã không còn các bảng schema v2 này do một migration ngoài repo (SyncPendingChanges)
            // nên dùng IF EXISTS để đồng bộ model ↔ DB mà không lỗi; dữ liệu các bảng này vốn không tồn tại.
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"AssignmentStatusHistories\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"CompletionReportMedias\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"Equipments\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"LocationUpdates\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"Notifications\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"CompletionReports\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"IncidentAssignments\" CASCADE;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"Vehicles\" CASCADE;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_DispatchAssignments_IncidentId\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_DispatchAssignments_RescueUnitId\";");

            migrationBuilder.AddColumn<DateTime>(
                name: "CoolDownUntil",
                table: "RescueUnits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockExpiresAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LockedByOperatorId",
                table: "Incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedByUserId",
                table: "DispatchAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchAlgorithm",
                table: "DispatchAssignments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DistanceKm",
                table: "DispatchAssignments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EnRouteAt",
                table: "DispatchAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EstimatedArrivalMinutes",
                table: "DispatchAssignments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "DispatchAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionNotes",
                table: "DispatchAssignments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectionReason",
                table: "DispatchAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventType",
                table: "AiClassifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsTrafficJam",
                table: "AiClassifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_LockedByOperatorId",
                table: "Incidents",
                column: "LockedByOperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchAssignments_AssignedByUserId",
                table: "DispatchAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchAssignments_Incident_Status",
                table: "DispatchAssignments",
                columns: new[] { "IncidentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DispatchAssignments_Unit_Status",
                table: "DispatchAssignments",
                columns: new[] { "RescueUnitId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_DispatchAssignments_Users_AssignedByUserId",
                table: "DispatchAssignments",
                column: "AssignedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_Users_LockedByOperatorId",
                table: "Incidents",
                column: "LockedByOperatorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DispatchAssignments_Users_AssignedByUserId",
                table: "DispatchAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_Users_LockedByOperatorId",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_LockedByOperatorId",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_DispatchAssignments_AssignedByUserId",
                table: "DispatchAssignments");

            migrationBuilder.DropIndex(
                name: "IX_DispatchAssignments_Incident_Status",
                table: "DispatchAssignments");

            migrationBuilder.DropIndex(
                name: "IX_DispatchAssignments_Unit_Status",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "CoolDownUntil",
                table: "RescueUnits");

            migrationBuilder.DropColumn(
                name: "LockExpiresAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "LockedByOperatorId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "AssignedByUserId",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "DispatchAlgorithm",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "DistanceKm",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "EnRouteAt",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "EstimatedArrivalMinutes",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "RejectionNotes",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "EventType",
                table: "AiClassifications");

            migrationBuilder.DropColumn(
                name: "IsTrafficJam",
                table: "AiClassifications");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchAssignments_IncidentId",
                table: "DispatchAssignments",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchAssignments_RescueUnitId",
                table: "DispatchAssignments",
                column: "RescueUnitId");
        }
    }
}
