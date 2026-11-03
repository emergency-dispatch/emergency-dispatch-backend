using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Traffic_Hotspot_Assignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "TrafficHotspots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedByUserId",
                table: "TrafficHotspots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedRescueUnitId",
                table: "TrafficHotspots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                table: "TrafficHotspots",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResponsibleStationId",
                table: "TrafficHotspots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceIncidentId",
                table: "TrafficHotspots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "TrafficHotspots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Dữ liệu cũ: suy ra trạng thái vòng đời từ IsActive / ResolvedAt (0 Active, 2 Resolved, 3 Expired)
            migrationBuilder.Sql("UPDATE \"TrafficHotspots\" SET \"Status\" = CASE WHEN \"IsActive\" THEN 0 WHEN \"ResolvedAt\" IS NOT NULL THEN 2 ELSE 3 END;");

            migrationBuilder.CreateIndex(
                name: "IX_TrafficHotspots_AssignedUnit",
                table: "TrafficHotspots",
                column: "AssignedRescueUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_TrafficHotspots_SourceIncidentId",
                table: "TrafficHotspots",
                column: "SourceIncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_TrafficHotspots_Station",
                table: "TrafficHotspots",
                column: "ResponsibleStationId");

            migrationBuilder.CreateIndex(
                name: "IX_TrafficHotspots_Status",
                table: "TrafficHotspots",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_TrafficHotspots_Incidents_SourceIncidentId",
                table: "TrafficHotspots",
                column: "SourceIncidentId",
                principalTable: "Incidents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TrafficHotspots_RescueUnits_AssignedRescueUnitId",
                table: "TrafficHotspots",
                column: "AssignedRescueUnitId",
                principalTable: "RescueUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TrafficHotspots_Stations_ResponsibleStationId",
                table: "TrafficHotspots",
                column: "ResponsibleStationId",
                principalTable: "Stations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrafficHotspots_Incidents_SourceIncidentId",
                table: "TrafficHotspots");

            migrationBuilder.DropForeignKey(
                name: "FK_TrafficHotspots_RescueUnits_AssignedRescueUnitId",
                table: "TrafficHotspots");

            migrationBuilder.DropForeignKey(
                name: "FK_TrafficHotspots_Stations_ResponsibleStationId",
                table: "TrafficHotspots");

            migrationBuilder.DropIndex(
                name: "IX_TrafficHotspots_AssignedUnit",
                table: "TrafficHotspots");

            migrationBuilder.DropIndex(
                name: "IX_TrafficHotspots_SourceIncidentId",
                table: "TrafficHotspots");

            migrationBuilder.DropIndex(
                name: "IX_TrafficHotspots_Station",
                table: "TrafficHotspots");

            migrationBuilder.DropIndex(
                name: "IX_TrafficHotspots_Status",
                table: "TrafficHotspots");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "TrafficHotspots");

            migrationBuilder.DropColumn(
                name: "AssignedByUserId",
                table: "TrafficHotspots");

            migrationBuilder.DropColumn(
                name: "AssignedRescueUnitId",
                table: "TrafficHotspots");

            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                table: "TrafficHotspots");

            migrationBuilder.DropColumn(
                name: "ResponsibleStationId",
                table: "TrafficHotspots");

            migrationBuilder.DropColumn(
                name: "SourceIncidentId",
                table: "TrafficHotspots");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "TrafficHotspots");
        }
    }
}
