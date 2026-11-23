using System.Text.Json;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmergencyDispatch.Tests.Infrastructure;

/// <summary>
/// Ca SC-261123-0390 (23/11/2026): TriageVL trả 2 object JSON liền nhau (object thứ 2 chỉ để chứa description_en),
/// ai_service.py bản cũ không parse được → none / 0 / "unparseable" nhưng vẫn gửi độ tin cậy 0.99
/// </summary>
public class LocalAiOutputParserTests
{
    private const string TwoObjects =
        "{\"incident_type\": \"accident\", \"severity_level\": 3, \"traffic_jam\": true, \"tags\": [\"multi_vehicle_pileup\", \"crushed_cabin\", \"lane_blocked\"], \"description\": \"Tai nan giao thong lien hoan voi nhieu xe o to va xe tai.\"}\n"
        + "{\"incident_type\": \"accident\", \"severity_level\": 3, \"traffic_jam\": true, \"tags\": [\"multi_vehicle_pileup\"], \"description_en\": \"Accident involving multiple cars and trucks. Lane blocked.\"}";

    [Fact]
    public void TwoObjects_AreMerged_FirstObjectDecides()
    {
        var r = LocalAiOutputParser.TryParse(TwoObjects)!;

        Assert.Equal("merged", r.ParseStatus);
        Assert.Equal("accident", r.IncidentType);
        Assert.Equal(3, r.SeverityLevel);
        Assert.True(r.TrafficJam);
        Assert.Equal(new[] { "multi_vehicle_pileup", "crushed_cabin", "lane_blocked" }, r.Tags);
        Assert.StartsWith("Tai nan giao thong", r.Description);
        Assert.StartsWith("Accident involving", r.DescriptionEn);
    }

    [Fact]
    public void SecondObject_CannotOverrideTypeOrLevel()
    {
        var r = LocalAiOutputParser.TryParse(
            "{\"incident_type\": \"fire\", \"severity_level\": 1, \"description\": \"Khói\"} {\"incident_type\": \"accident\", \"severity_level\": 3, \"description_en\": \"Smoke\"}")!;

        Assert.Equal(("fire", 1, "Smoke"), (r.IncidentType, r.SeverityLevel, r.DescriptionEn));
    }

    [Theory]
    [InlineData("```json\n{\"incident_type\": \"fire\", \"severity_level\": 2, \"description\": \"Cháy\"}\n```", "ok", "fire", 2)]
    [InlineData("{\"incident_type\": \"accident\", \"severity_level\": \"2\", \"traffic_jam\": \"false\"}", "ok", "accident", 2)]
    [InlineData("{\"incident_type\": \"accident\", \"severity_level\": 2, \"tags\": [\"crash\"], \"description\": \"Hai xe va chạm\", \"description_en\": \"Two bikes coll", "salvaged", "accident", 2)]
    [InlineData("{\"incident_type\": \"explosion\", \"severity_level\": 9}", "ok", "none", 3)]
    public void OtherShapes(string raw, string status, string type, int level)
    {
        var r = LocalAiOutputParser.TryParse(raw)!;

        Assert.Equal((status, type, level), (r.ParseStatus, r.IncidentType, r.SeverityLevel));
    }

    [Fact]
    public void Truncated_KeepsCompletedDescription()
    {
        var r = LocalAiOutputParser.TryParse("{\"incident_type\": \"accident\", \"severity_level\": 2, \"tags\": [\"crash\"], \"description\": \"Hai xe va chạm\", \"description_en\": \"Two bikes coll")!;

        Assert.Equal("Hai xe va chạm", r.Description);
        Assert.Null(r.DescriptionEn);
        Assert.Equal(new[] { "crash" }, r.Tags);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I cannot analyze this image.")]
    [InlineData("{\"incident_type\": \"accident\", \"sever")]
    [InlineData("{\"foo\": 1}")]
    public void Unreadable_ReturnsNull(string? raw)
    {
        Assert.Null(LocalAiOutputParser.TryParse(raw));
    }

    // -------- AiClassificationService với phản hồi thật của ai_service.py bản cũ --------
    private static string OldServiceReply(string raw) => JsonSerializer.Serialize(new
    {
        is_emergency = false,
        incident_type = "none",
        severity_level = 0,
        traffic_jam = false,
        tags = new[] { "unparseable" },
        description = raw.Length > 200 ? raw[..200] : raw,
        description_vi = raw.Length > 200 ? raw[..200] : raw,
        description_en = "A detailed English description is unavailable. Incident: none; severity: 0. Operator review may be required.",
        description_en_is_fallback = true,
        confidence = 0.9947,
        confidence_source = "logprob",
        raw_response = raw,
        inference_time_ms = 35119
    });

    private static AiClassificationService Svc(FakeHttpHandler local)
        => new(new FakeHttpClientFactory().With(string.Empty, local).With("OpenRouterClient", FakeHttpHandler.Json("{}")),
            TestHelpers.Config(("AiService:UseLocalModel", "true"), ("AiService:LocalServiceUrl", "https://triage.test/api/analyze-incident")),
            NullLogger<AiClassificationService>.Instance);

    [Fact]
    public async Task OldService_Unparseable_IsRecoveredFromRawResponse()
    {
        var r = await Svc(FakeHttpHandler.Json(OldServiceReply(TwoObjects))).AnalyzeAsync("https://res.cloudinary.com/demo/image/upload/a.jpg");

        Assert.True(r.IsSuccess);
        Assert.Equal(IncidentEventType.TrafficAccident, r.EventType);
        Assert.Equal(SeverityLevel.Level3, r.SeverityLevel);
        Assert.Equal(0.9947, r.ConfidenceScore);
        Assert.Equal("logprob", r.ConfidenceSource);
        Assert.DoesNotContain("unparseable", r.HazardTags);
        Assert.StartsWith("Tai nan giao thong", r.SummaryVi);
        Assert.StartsWith("Accident involving", r.SummaryEn);
        Assert.False(r.SummaryEnIsFallback);
        Assert.Equal(TrafficJamStatus.Suspected, r.TrafficJamStatus);
    }

    [Fact]
    public async Task OldService_Unparseable_Garbage_DropsConfidence()
    {
        var r = await Svc(FakeHttpHandler.Json(OldServiceReply("I cannot analyze this image."))).AnalyzeAsync("https://res.cloudinary.com/demo/image/upload/a.jpg");

        Assert.Equal(SeverityLevel.Unclassified, r.SeverityLevel);
        Assert.Equal(0.0, r.ConfidenceScore);
        Assert.Equal("unparseable", r.ConfidenceSource);
    }
}
