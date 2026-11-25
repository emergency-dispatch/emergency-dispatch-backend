using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentAudioEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioAttachmentsJson",
                table: "IncidentSupplements",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DurationSeconds",
                table: "IncidentMedias",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Transcript",
                table: "IncidentMedias",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptionStatus",
                table: "IncidentMedias",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioAttachmentsJson",
                table: "IncidentSupplements");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "IncidentMedias");

            migrationBuilder.DropColumn(
                name: "Transcript",
                table: "IncidentMedias");

            migrationBuilder.DropColumn(
                name: "TranscriptionStatus",
                table: "IncidentMedias");
        }
    }
}
