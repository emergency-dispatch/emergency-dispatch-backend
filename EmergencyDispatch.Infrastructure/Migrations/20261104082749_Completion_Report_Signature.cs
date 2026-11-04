using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Completion_Report_Signature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureUrl",
                table: "CompletionReports",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignedAt",
                table: "CompletionReports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignerName",
                table: "CompletionReports",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureUrl",
                table: "CompletionReports");

            migrationBuilder.DropColumn(
                name: "SignedAt",
                table: "CompletionReports");

            migrationBuilder.DropColumn(
                name: "SignerName",
                table: "CompletionReports");
        }
    }
}
