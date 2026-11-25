using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations;

public partial class UnifyIncidentDescription : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Widen before merging: legacy note + speech can exceed the new-request limit.
        // Keep the complete historical text; 4000 is validated for new API input only.
        migrationBuilder.AlterColumn<string>(name: "Note", table: "IncidentSupplements", type: "text", nullable: false,
            oldClrType: typeof(string), oldType: "character varying(1000)", oldMaxLength: 1000);

        migrationBuilder.Sql("""
            UPDATE "Incidents" AS incident
            SET "Description" = concat_ws(E'\n\n', NULLIF(btrim(incident."Description"), ''), additions.speech)
            FROM (
                SELECT voices.incident_id, string_agg(voices.speech, E'\n\n' ORDER BY voices.first_at, voices.speech) AS speech
                FROM (
                    SELECT media."IncidentId" AS incident_id, btrim(media."Transcript") AS speech, min(media."CreatedAt") AS first_at
                    FROM "IncidentMedias" media
                    JOIN "Incidents" original ON original."Id" = media."IncidentId"
                    WHERE NOT media."IsDeleted" AND media."MediaType" = 2
                        AND NULLIF(btrim(media."Transcript"), '') IS NOT NULL
                        AND strpos(COALESCE(original."Description", ''), btrim(media."Transcript")) = 0
                    GROUP BY media."IncidentId", btrim(media."Transcript")
                ) voices
                GROUP BY voices.incident_id
            ) additions
            WHERE incident."Id" = additions.incident_id;

            WITH voices AS (
                SELECT supplement."Id" AS supplement_id, item.ordinality,
                    btrim(COALESCE(item.value ->> 'Transcript', item.value ->> 'transcript')) AS speech
                FROM "IncidentSupplements" supplement
                CROSS JOIN LATERAL jsonb_array_elements(
                    CASE WHEN jsonb_typeof(supplement."AudioAttachmentsJson") = 'array'
                        THEN supplement."AudioAttachmentsJson" ELSE '[]'::jsonb END
                ) WITH ORDINALITY AS item(value, ordinality)
                WHERE NOT supplement."IsDeleted"
            ), additions AS (
                SELECT distinct_voices.supplement_id,
                    string_agg(distinct_voices.speech, E'\n\n' ORDER BY distinct_voices.first_at, distinct_voices.speech) AS speech
                FROM (
                    SELECT voices.supplement_id, voices.speech, min(voices.ordinality) AS first_at
                    FROM voices JOIN "IncidentSupplements" original ON original."Id" = voices.supplement_id
                    WHERE NULLIF(voices.speech, '') IS NOT NULL
                        AND strpos(COALESCE(original."Note", ''), voices.speech) = 0
                    GROUP BY voices.supplement_id, voices.speech
                ) distinct_voices
                GROUP BY distinct_voices.supplement_id
            )
            UPDATE "IncidentSupplements" supplement
            SET "Note" = concat_ws(E'\n\n', NULLIF(btrim(supplement."Note"), ''), additions.speech)
            FROM additions WHERE supplement."Id" = additions.supplement_id;

            UPDATE "IncidentSupplements" supplement
            SET "AudioAttachmentsJson" = (
                SELECT COALESCE(jsonb_agg(
                    CASE WHEN jsonb_typeof(item.value) = 'object'
                        THEN item.value - 'Transcript' - 'transcript' - 'TranscriptionStatus' - 'transcriptionStatus'
                        ELSE item.value END ORDER BY item.ordinality), '[]'::jsonb)
                FROM jsonb_array_elements(supplement."AudioAttachmentsJson") WITH ORDINALITY AS item(value, ordinality)
            )
            WHERE jsonb_typeof(supplement."AudioAttachmentsJson") = 'array';

            UPDATE "Incidents"
            SET "AutoDispatchReasonCode" = 'audio-description-unavailable',
                "AutoDispatchNote" = CASE
                    WHEN "AutoDispatchNote" = 'AI không tự điều phối: bản ghi âm chưa có bản chép lời sử dụng được. Cần Điều phối viên nghe và xác nhận.'
                        THEN 'AI không tự điều phối: bản ghi âm chưa có mô tả hiện trường. Cần Điều phối viên nghe và xác nhận.'
                    WHEN "AutoDispatchNote" = 'AI did not dispatch automatically: the recording has no usable transcript. An operator must listen and verify.'
                        THEN 'AI did not dispatch automatically: the recording has no scene description. An operator must listen and verify.'
                    ELSE "AutoDispatchNote" END
            WHERE "AutoDispatchReasonCode" = 'audio-transcript-unavailable';
            """);

        migrationBuilder.DropColumn(name: "Transcript", table: "IncidentMedias");
        migrationBuilder.DropColumn(name: "TranscriptionStatus", table: "IncidentMedias");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restore legacy column shape without deleting the merged description/note.
        // Deliberately keep Note as text: shrinking to 1000 could lose historical content.
        // The original split between typed text and speech cannot be reconstructed automatically.
        migrationBuilder.AddColumn<string>(name: "Transcript", table: "IncidentMedias", type: "character varying(4000)", maxLength: 4000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TranscriptionStatus", table: "IncidentMedias", type: "character varying(32)", maxLength: 32, nullable: true);
    }
}
