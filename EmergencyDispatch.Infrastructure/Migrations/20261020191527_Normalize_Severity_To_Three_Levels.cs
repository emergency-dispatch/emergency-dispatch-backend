using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Normalize_Severity_To_Three_Levels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chuẩn hóa dữ liệu cũ theo thang 3 cấp: Level4/Level5 (legacy) -> Level3, giá trị âm -> 0 (Reject)
            migrationBuilder.Sql("UPDATE \"Incidents\" SET \"Severity\" = 3 WHERE \"Severity\" > 3;");
            migrationBuilder.Sql("UPDATE \"Incidents\" SET \"Severity\" = 0 WHERE \"Severity\" < 0;");
            migrationBuilder.Sql("UPDATE \"AiClassifications\" SET \"SeverityScore\" = 3 WHERE \"SeverityScore\" > 3;");
            migrationBuilder.Sql("UPDATE \"AiClassifications\" SET \"SeverityScore\" = 0 WHERE \"SeverityScore\" < 0;");
            migrationBuilder.Sql("UPDATE \"EscalationRules\" SET \"MinSeverityLevel\" = 3 WHERE \"MinSeverityLevel\" > 3 OR \"MinSeverityLevel\" < 1;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Incidents_Severity",
                table: "Incidents",
                sql: "\"Severity\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EscalationRules_MinSeverityLevel",
                table: "EscalationRules",
                sql: "\"MinSeverityLevel\" BETWEEN 1 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiClassifications_SeverityScore",
                table: "AiClassifications",
                sql: "\"SeverityScore\" BETWEEN 0 AND 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Incidents_Severity",
                table: "Incidents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EscalationRules_MinSeverityLevel",
                table: "EscalationRules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiClassifications_SeverityScore",
                table: "AiClassifications");
        }
    }
}
