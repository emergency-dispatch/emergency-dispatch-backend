using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Medical_ICE_SpecialInfo_To_User : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "Allergies",
                table: "Users",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<bool>(
                name: "AutoSendSmsOnSos",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "ChronicConditions",
                table: "Users",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "CurrentMedications",
                table: "Users",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "DependentsNote",
                table: "Users",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HouseholdMembersCount",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "MobilityLimitations",
                table: "Users",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguage",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

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
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IceContacts");

            migrationBuilder.DropColumn(
                name: "Allergies",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AutoSendSmsOnSos",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ChronicConditions",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CurrentMedications",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DependentsNote",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HouseholdMembersCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MobilityLimitations",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreferredLanguage",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SmsTemplate",
                table: "Users");
        }
    }
}
