using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStationTransferDispatchRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AutoDispatchAttemptedAt",
                table: "IncidentStationTransfers",
                type: "timestamp with time zone",
                nullable: true);

            // Do not replay handovers that happened before this feature was deployed.
            migrationBuilder.Sql("UPDATE \"IncidentStationTransfers\" SET \"AutoDispatchAttemptedAt\" = CURRENT_TIMESTAMP;");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentStationTransfers_AutoDispatchAttemptedAt_CreatedAt",
                table: "IncidentStationTransfers",
                columns: new[] { "AutoDispatchAttemptedAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IncidentStationTransfers_AutoDispatchAttemptedAt_CreatedAt",
                table: "IncidentStationTransfers");

            migrationBuilder.DropColumn(
                name: "AutoDispatchAttemptedAt",
                table: "IncidentStationTransfers");
        }
    }
}
