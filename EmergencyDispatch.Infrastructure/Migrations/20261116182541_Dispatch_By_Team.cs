using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Giao sự cố theo tổ: lượt điều phối lưu tổ được phân công (chốt tại thời điểm phát lệnh)
    /// </summary>
    public partial class Dispatch_By_Team : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StationTeamId",
                table: "DispatchAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchAssignments_Team_Status",
                table: "DispatchAssignments",
                columns: new[] { "StationTeamId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_DispatchAssignments_StationTeams_StationTeamId",
                table: "DispatchAssignments",
                column: "StationTeamId",
                principalTable: "StationTeams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Lượt điều phối cũ: chốt tổ theo tổ hiện tại của xe (xe chưa gán tổ → để null)
            migrationBuilder.Sql(@"
UPDATE ""DispatchAssignments"" a
SET ""StationTeamId"" = u.""StationTeamId""
FROM ""RescueUnits"" u
WHERE a.""RescueUnitId"" = u.""Id"" AND u.""StationTeamId"" IS NOT NULL AND a.""StationTeamId"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DispatchAssignments_StationTeams_StationTeamId",
                table: "DispatchAssignments");

            migrationBuilder.DropIndex(
                name: "IX_DispatchAssignments_Team_Status",
                table: "DispatchAssignments");

            migrationBuilder.DropColumn(
                name: "StationTeamId",
                table: "DispatchAssignments");
        }
    }
}
