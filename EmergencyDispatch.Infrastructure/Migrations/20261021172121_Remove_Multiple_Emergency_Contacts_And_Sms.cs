using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Remove_Multiple_Emergency_Contacts_And_Sms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve one contact before retiring the multi-contact feature.
            // Existing legacy contact details are never overwritten.
            migrationBuilder.Sql("""
                WITH selected_contacts AS (
                    SELECT DISTINCT ON ("UserId")
                        "UserId", "Name", "PhoneNumber", "Relationship"
                    FROM "IceContacts"
                    WHERE NOT "IsDeleted"
                    ORDER BY "UserId", "IsPrimary" DESC, "CreatedAt", "Id"
                )
                UPDATE "Users" AS u
                SET "EmergencyContactName" = c."Name",
                    "EmergencyContactPhone" = c."PhoneNumber",
                    "EmergencyContactRelationship" = c."Relationship"
                FROM selected_contacts AS c
                WHERE u."Id" = c."UserId"
                  AND NULLIF(BTRIM(u."EmergencyContactName"), '') IS NULL
                  AND NULLIF(BTRIM(u."EmergencyContactPhone"), '') IS NULL
                  AND NULLIF(BTRIM(u."EmergencyContactRelationship"), '') IS NULL;
                """);

            migrationBuilder.DropTable(
                name: "IceContacts");

            migrationBuilder.DropColumn(
                name: "AutoSendSmsOnSos",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SmsTemplate",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoSendSmsOnSos",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SmsTemplate",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IceContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IceContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IceContacts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IceContacts_UserId",
                table: "IceContacts",
                column: "UserId");
        }
    }
}
