using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_RescueUnits_And_DispatchAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByUserId",
                table: "Incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionSummary",
                table: "Incidents",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RescueUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UnitType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentLat = table.Column<double>(type: "double precision", nullable: false),
                    CurrentLng = table.Column<double>(type: "double precision", nullable: false),
                    LastLocationUpdateAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RescueUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RescueUnits_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DispatchAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RescueUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArrivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchAssignments_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DispatchAssignments_RescueUnits_RescueUnitId",
                        column: x => x.RescueUnitId,
                        principalTable: "RescueUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ClosedByUserId",
                table: "Incidents",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchAssignments_IncidentId",
                table: "DispatchAssignments",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchAssignments_RescueUnitId",
                table: "DispatchAssignments",
                column: "RescueUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RescueUnits_PlateNumber",
                table: "RescueUnits",
                column: "PlateNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RescueUnits_StationId",
                table: "RescueUnits",
                column: "StationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_Users_ClosedByUserId",
                table: "Incidents",
                column: "ClosedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_Users_ClosedByUserId",
                table: "Incidents");

            migrationBuilder.DropTable(
                name: "DispatchAssignments");

            migrationBuilder.DropTable(
                name: "RescueUnits");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_ClosedByUserId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ResolutionSummary",
                table: "Incidents");
        }
    }
}
