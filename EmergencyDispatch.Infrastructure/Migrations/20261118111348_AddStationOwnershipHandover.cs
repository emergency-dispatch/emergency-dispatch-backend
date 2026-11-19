using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStationOwnershipHandover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutomaticStationTransferCount",
                table: "Incidents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StationAcknowledgedAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StationAcknowledgedByUserId",
                table: "Incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StationAssignedAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StationAssignmentVersion",
                table: "Incidents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StationResponseDueAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StationTransferAlertCode",
                table: "Incidents",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StationTransferAlertedAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StationTransferChainId",
                table: "Incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StationTransferLastCheckedAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IncidentStationTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ToStationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChainId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentVersion = table.Column<int>(type: "integer", nullable: false),
                    IsAutomatic = table.Column<bool>(type: "boolean", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PreviousAssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreviousAcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreviousAcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectionMethod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SelectionDistanceKm = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentStationTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentStationTransfers_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Status_StationResponseDueAt",
                table: "Incidents",
                columns: new[] { "Status", "StationResponseDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentStationTransfers_IncidentId_AssignmentVersion",
                table: "IncidentStationTransfers",
                columns: new[] { "IncidentId", "AssignmentVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentStationTransfers_IncidentId_ChainId",
                table: "IncidentStationTransfers",
                columns: new[] { "IncidentId", "ChainId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentStationTransfers");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_Status_StationResponseDueAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "AutomaticStationTransferCount",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "StationAcknowledgedAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "StationAcknowledgedByUserId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "StationAssignedAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "StationAssignmentVersion",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "StationResponseDueAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "StationTransferAlertCode",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "StationTransferAlertedAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "StationTransferChainId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "StationTransferLastCheckedAt",
                table: "Incidents");
        }
    }
}
