using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Admin_Audit_HeadOperator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HeadOperatorId",
                table: "Stations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActorRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OldValueJson = table.Column<string>(type: "text", nullable: true),
                    NewValueJson = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stations_HeadOperatorId",
                table: "Stations",
                column: "HeadOperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_Actor",
                table: "SystemAuditLogs",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_Entity",
                table: "SystemAuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_Timestamp",
                table: "SystemAuditLogs",
                column: "Timestamp");

            migrationBuilder.AddForeignKey(
                name: "FK_Stations_Users_HeadOperatorId",
                table: "Stations",
                column: "HeadOperatorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stations_Users_HeadOperatorId",
                table: "Stations");

            migrationBuilder.DropTable(
                name: "SystemAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Stations_HeadOperatorId",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "HeadOperatorId",
                table: "Stations");
        }
    }
}
