using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCitizenIncidentAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CitizenIncidentAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AreaVi = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AreaEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TriggerKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CitizenIncidentAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CitizenIncidentAlerts_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CitizenIncidentAlertDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RealtimeAttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CitizenIncidentAlertDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CitizenIncidentAlertDeliveries_CitizenIncidentAlerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "CitizenIncidentAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CitizenIncidentAlertDeliveries_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CitizenIncidentAlertDeliveries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CitizenIncidentAlertDeliveries_AlertId_UserId",
                table: "CitizenIncidentAlertDeliveries",
                columns: new[] { "AlertId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CitizenIncidentAlertDeliveries_CompletedAt_NextAttemptAt_Le~",
                table: "CitizenIncidentAlertDeliveries",
                columns: new[] { "CompletedAt", "NextAttemptAt", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_CitizenIncidentAlertDeliveries_NotificationId",
                table: "CitizenIncidentAlertDeliveries",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CitizenIncidentAlertDeliveries_UserId",
                table: "CitizenIncidentAlertDeliveries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CitizenIncidentAlerts_CreatedAt",
                table: "CitizenIncidentAlerts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CitizenIncidentAlerts_IncidentId",
                table: "CitizenIncidentAlerts",
                column: "IncidentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CitizenIncidentAlertDeliveries");

            migrationBuilder.DropTable(
                name: "CitizenIncidentAlerts");
        }
    }
}
