using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EmergencyDispatch.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261125090000_EnableCitizenIncidentAlertsByDefault")]
public class EnableCitizenIncidentAlertsByDefault : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // One-time rollout: enable legacy defaults and establish a durable UTC cutoff.
        // Later Admin opt-outs are preserved because migrations do not rerun at restart.
        migrationBuilder.Sql("""
            INSERT INTO "SystemSettings" ("Id", "Key", "ValueJson", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), 'citizenIncidentAlerts',
                jsonb_build_object('enabled', true, 'enabledFromUtc',
                    to_char(CURRENT_TIMESTAMP AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'))::text,
                CURRENT_TIMESTAMP, FALSE)
            ON CONFLICT ("Key") DO UPDATE
            SET "ValueJson" = ("SystemSettings"."ValueJson"::jsonb ||
                    jsonb_build_object('enabled', true, 'enabledFromUtc',
                    to_char(CURRENT_TIMESTAMP AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')))::text,
                "UpdatedAt" = CURRENT_TIMESTAMP,
                "UpdatedByUserId" = NULL
            WHERE COALESCE(("SystemSettings"."ValueJson"::jsonb ->> 'enabled')::boolean, FALSE) = FALSE
                OR "SystemSettings"."ValueJson"::jsonb ->> 'enabledFromUtc' IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep operational settings and subsequent Admin choices when rolling back code.
    }
}
