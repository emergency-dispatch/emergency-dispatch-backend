using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Missing_Modules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StationTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RescueUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StationTeams_RescueUnits_RescueUnitId",
                        column: x => x.RescueUnitId,
                        principalTable: "RescueUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StationTeams_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ValueJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemSettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StationTeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffId = table.Column<Guid>(type: "uuid", nullable: false),
                    CrewRole = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationTeamMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StationTeamMembers_StationTeams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "StationTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StationTeamMembers_Users_StaffId",
                        column: x => x.StaffId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StationTeamMembers_TeamId",
                table: "StationTeamMembers",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "UQ_StationTeamMembers_Staff",
                table: "StationTeamMembers",
                column: "StaffId",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_StationTeams_RescueUnit",
                table: "StationTeams",
                column: "RescueUnitId",
                unique: true,
                filter: "\"RescueUnitId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_StationTeams_Station_Name",
                table: "StationTeams",
                columns: new[] { "StationId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_UpdatedByUserId",
                table: "SystemSettings",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);

            // Chuyển "tổ làm việc" đang lưu tạm trong Users.DependentsNote (RescueStaff) sang bảng StationTeams
            migrationBuilder.Sql(@"
INSERT INTO ""StationTeams"" (""Id"", ""StationId"", ""Name"", ""RescueUnitId"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
SELECT gen_random_uuid(), t.""StationId"", t.""Name"", NULL, NOW(), NULL, FALSE
FROM (
    SELECT DISTINCT u.""StationId"", LEFT(BTRIM(u.""DependentsNote""), 100) AS ""Name""
    FROM ""Users"" u
    WHERE u.""Role"" = 2 AND u.""StationId"" IS NOT NULL AND u.""IsDeleted"" = FALSE
      AND u.""DependentsNote"" IS NOT NULL AND BTRIM(u.""DependentsNote"") <> ''
) t;");

            migrationBuilder.Sql(@"
INSERT INTO ""StationTeamMembers"" (""Id"", ""TeamId"", ""StaffId"", ""CrewRole"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
SELECT gen_random_uuid(), st.""Id"", u.""Id"", 3, NOW(), NULL, FALSE
FROM ""Users"" u
JOIN ""StationTeams"" st ON st.""StationId"" = u.""StationId"" AND st.""Name"" = LEFT(BTRIM(u.""DependentsNote""), 100)
WHERE u.""Role"" = 2 AND u.""IsDeleted"" = FALSE;");

            migrationBuilder.Sql(@"
UPDATE ""Users"" SET ""DependentsNote"" = NULL
WHERE ""Role"" = 2 AND ""Id"" IN (SELECT ""StaffId"" FROM ""StationTeamMembers"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StationTeamMembers");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "StationTeams");
        }
    }
}
