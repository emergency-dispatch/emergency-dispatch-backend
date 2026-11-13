using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PostGIS_Locations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.AddColumn<Point>(
                name: "Location",
                table: "TrafficHotspots",
                type: "geography(Point,4326)",
                nullable: true,
                computedColumnSql: "ST_SetSRID(ST_MakePoint(\"Longitude\", \"Latitude\"), 4326)::geography",
                stored: true);

            migrationBuilder.AddColumn<Point>(
                name: "Location",
                table: "Stations",
                type: "geography(Point,4326)",
                nullable: true,
                computedColumnSql: "ST_SetSRID(ST_MakePoint(\"Longitude\", \"Latitude\"), 4326)::geography",
                stored: true);

            migrationBuilder.AddColumn<Point>(
                name: "Location",
                table: "RescueUnits",
                type: "geography(Point,4326)",
                nullable: true,
                computedColumnSql: "ST_SetSRID(ST_MakePoint(\"CurrentLng\", \"CurrentLat\"), 4326)::geography",
                stored: true);

            migrationBuilder.AddColumn<Point>(
                name: "Location",
                table: "Incidents",
                type: "geography(Point,4326)",
                nullable: true,
                computedColumnSql: "ST_SetSRID(ST_MakePoint(\"Longitude\", \"Latitude\"), 4326)::geography",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrafficHotspots_Location",
                table: "TrafficHotspots",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_Location",
                table: "Stations",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_RescueUnits_Location",
                table: "RescueUnits",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Location",
                table: "Incidents",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrafficHotspots_Location",
                table: "TrafficHotspots");

            migrationBuilder.DropIndex(
                name: "IX_Stations_Location",
                table: "Stations");

            migrationBuilder.DropIndex(
                name: "IX_RescueUnits_Location",
                table: "RescueUnits");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_Location",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "TrafficHotspots");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "RescueUnits");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Incidents");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
