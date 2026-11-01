using System.Text.Json;
using System.Text.Json.Nodes;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Localization;

/// <summary>Explicit language rendering also works in background jobs and shared SignalR payloads.</summary>
public static class AiTextLocalization
{
    private const string MetadataKey = "_localizedAiText";

    public static string T(string language, string text, params object[] args) =>
        LocalizationHelper.LocalizeForLanguage(text, language, args);

    public static AiClassificationResultDto Ensure(AiClassificationResultDto result)
    {
        result.SummaryVi ??= result.GetSummary("vi");
        if (string.IsNullOrWhiteSpace(result.SummaryEn) && !string.IsNullOrWhiteSpace(result.SummaryVi))
        {
            var translated = T("en", result.SummaryVi);
            result.SummaryEnIsFallback = translated == result.SummaryVi;
            result.SummaryEn = result.SummaryEnIsFallback
                ? $"A detailed English description is unavailable. Incident: {result.IncidentType ?? "unverified"}; severity: {result.SeverityScore}. Human review may be required."
                : translated;
        }

        result.TrafficJamEvidenceVi ??= result.GetTrafficJamEvidence("vi");
        result.TrafficJamEvidenceEn ??= TranslateEvidence(result.GetTrafficJamEvidence("vi"), result.TrafficJamStatus);
        result.ErrorMessageVi ??= result.GetErrorMessage("vi");
        if (result.ErrorMessageEn == null && result.ErrorMessageVi != null)
        {
            var translated = T("en", result.ErrorMessageVi);
            result.ErrorMessageEn = translated != result.ErrorMessageVi
                ? translated : "AI analysis failed. Technical details are retained in the original error metadata.";
        }
        return result;
    }

    private static string? TranslateEvidence(string? evidence, TrafficJamStatus status)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return evidence;
        var translated = T("en", evidence);
        if (translated != evidence) return translated;

        // Notes entered by people remain verbatim; translate the system-generated prefix only.
        var marker = "Ghi chú:";
        var noteAt = evidence.IndexOf(marker, StringComparison.Ordinal);
        var note = noteAt >= 0 ? " Notes: " + evidence[(noteAt + marker.Length)..].Trim() : "";
        return status switch
        {
            TrafficJamStatus.Confirmed => "[Operator confirmed] Traffic congestion has been confirmed." + note,
            TrafficJamStatus.Dismissed => "[Operator dismissed] Traffic congestion was dismissed; traffic is flowing normally." + note,
            TrafficJamStatus.Suspected => "Traffic congestion is suspected and requires operator review.",
            TrafficJamStatus.Disputed => "The AI engines disagree about traffic congestion; operator review is required.",
            TrafficJamStatus.NoEvidence => "No signs of traffic congestion were identified in the image.",
            _ => "Traffic congestion could not be assessed; operator review is required."
        };
    }

    /// <summary>Persist translations in the existing JSON diagnostic column; no schema migration is required.</summary>
    public static string Capture(AiClassificationResultDto result)
    {
        Ensure(result);
        JsonObject root;
        try { root = JsonNode.Parse(result.RawResponse ?? "{}") as JsonObject ?? new JsonObject { ["rawResponse"] = result.RawResponse }; }
        catch (JsonException) { root = new JsonObject { ["rawResponse"] = result.RawResponse }; }
        root[MetadataKey] = JsonSerializer.SerializeToNode(new
        {
            result.SummaryVi, result.SummaryEn, result.SummaryEnIsFallback,
            result.TrafficJamEvidenceVi, result.TrafficJamEvidenceEn,
            result.ErrorMessageVi, result.ErrorMessageEn
        });
        return root.ToJsonString();
    }

    public static void Restore(AiClassificationResultDto result)
    {
        try
        {
            using var doc = JsonDocument.Parse(result.RawResponse ?? "{}");
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(MetadataKey, out var text) && text.ValueKind == JsonValueKind.Object)
            {
                string? Read(string name) => text.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                var vi = Read(nameof(result.SummaryVi));
                var en = Read(nameof(result.SummaryEn));
                if (result.GetSummary("vi") == vi || result.GetSummary("vi") == en)
                {
                    result.SummaryVi = vi;
                    result.SummaryEn = en;
                    result.SummaryEnIsFallback = text.TryGetProperty(nameof(result.SummaryEnIsFallback), out var fallback) && fallback.ValueKind == JsonValueKind.True;
                }
                vi = Read(nameof(result.TrafficJamEvidenceVi));
                en = Read(nameof(result.TrafficJamEvidenceEn));
                // Never restore obsolete AI evidence over a newer human decision.
                if (result.TrafficJamVerifiedByUserId == null && (result.GetTrafficJamEvidence("vi") == vi || result.GetTrafficJamEvidence("vi") == en))
                {
                    result.TrafficJamEvidenceVi = vi;
                    result.TrafficJamEvidenceEn = en;
                }
                result.ErrorMessageVi = Read(nameof(result.ErrorMessageVi));
                result.ErrorMessageEn = Read(nameof(result.ErrorMessageEn));
            }
        }
        catch (JsonException) { /* Legacy raw responses may be non-JSON. */ }
        Ensure(result);
    }
}
