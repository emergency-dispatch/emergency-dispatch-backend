using System.Net;
using System.Text.Json;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmergencyDispatch.Tests.Infrastructure;

public class AiClassificationServiceTests
{
    private const string Img = "https://res.cloudinary.com/demo/image/upload/a.jpg";

    private static AiClassificationService Svc(FakeHttpHandler? local, FakeHttpHandler? openRouter, string? apiKey = "or-key", bool useLocal = true)
    {
        var factory = new FakeHttpClientFactory();
        if (local != null) factory.With(string.Empty, local);
        if (openRouter != null) factory.With("OpenRouterClient", openRouter);
        var cfg = TestHelpers.Config(
            ("AiService:UseLocalModel", useLocal.ToString()),
            ("AiService:LocalServiceUrl", "https://triage.test/api/analyze-incident"),
            ("AiService:ApiKey", "svc-secret"),
            ("OpenRouter:ApiKey", apiKey),
            ("OpenRouter:BaseUrl", "https://openrouter.test/api/v1"),
            ("OpenRouter:Model", "qwen/qwen3-vl-32b-instruct"));
        return new AiClassificationService(factory, cfg, NullLogger<AiClassificationService>.Instance);
    }

    private static string OpenRouterReply(string content, string model = "qwen/qwen3-vl-32b-instruct")
        => JsonSerializer.Serialize(new { model, choices = new[] { new { message = new { content } } } });

    private const string LocalJson = "{\"incident_type\":\"fire\",\"severity_level\":2,\"traffic_jam\":false,\"description\":\"Cháy xe máy\",\"description_en\":\"A motorbike is on fire\",\"confidence\":0.87,\"tags\":[\"fire\",\"smoke\"]}";

    // -------- local TriageVL service --------
    [Fact]
    public async Task Local_Success_IsUsed_AndOpenRouterNotCalled()
    {
        var local = FakeHttpHandler.Json(LocalJson);
        var or = FakeHttpHandler.Json("{}");

        var r = await Svc(local, or).AnalyzeAsync(Img, "Có người kẹt");

        Assert.True(r.IsSuccess);
        Assert.Equal(IncidentEventType.FireExplosion, r.EventType);
        Assert.Equal(SeverityLevel.Level2, r.SeverityLevel);
        Assert.Equal(0.87, r.ConfidenceScore);
        Assert.Equal(new[] { "fire", "smoke" }, r.HazardTags);
        Assert.Equal(TrafficJamStatus.NoEvidence, r.TrafficJamStatus);
        Assert.Equal("Cháy xe máy", r.SummaryVi);
        Assert.Equal("A motorbike is on fire", r.SummaryEn);
        Assert.Empty(or.Requests);
    }

    [Fact]
    public async Task Local_SendsImageUrlNoteServiceKeyAndNgrokHeader()
    {
        var local = FakeHttpHandler.Json(LocalJson);

        await Svc(local, null).AnalyzeAsync(Img, "Có người kẹt");

        var req = local.Requests.Single();
        Assert.Equal("https://triage.test/api/analyze-incident", req.RequestUri!.ToString());
        Assert.Equal("svc-secret", req.Headers.GetValues("X-AI-Service-Key").Single());
        Assert.True(req.Headers.Contains("ngrok-skip-browser-warning"));
        Assert.Contains(Img, local.Bodies[0]);
        Assert.Contains("Có người kẹt", local.Bodies[0]);
    }

    [Fact]
    public async Task Local_TrafficJamTrue_IsOnlySuspected()
    {
        var r = await Svc(FakeHttpHandler.Json("{\"incident_type\":\"accident\",\"severity_level\":1,\"traffic_jam\":true,\"description\":\"x\"}"), null).AnalyzeAsync(Img);

        Assert.Equal(TrafficJamStatus.Suspected, r.TrafficJamStatus);
        Assert.False(r.IsTrafficJam);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"detail\":\"bad key\"}")]
    [InlineData(HttpStatusCode.NotFound, "ERR_NGROK_3200 endpoint offline")]
    [InlineData(HttpStatusCode.OK, "{\"unexpected\":true}")]
    public async Task Local_FailureOrUnknownShape_FallsBackToOpenRouter(HttpStatusCode code, string body)
    {
        var or = FakeHttpHandler.Json(OpenRouterReply("{\"incidentType\":\"accident\",\"severityScore\":3,\"summary\":\"Tai nạn liên hoàn\",\"confidenceScore\":0.8}"));

        var r = await Svc(FakeHttpHandler.Json(body, code), or).AnalyzeAsync(Img);

        Assert.Single(or.Requests);
        Assert.True(r.IsSuccess);
        Assert.Equal(SeverityLevel.Level3, r.SeverityLevel);
        Assert.Equal("qwen/qwen3-vl-32b-instruct", r.ModelName);
    }

    [Fact]
    public async Task Local_NetworkError_FallsBackToOpenRouter()
    {
        var or = FakeHttpHandler.Json(OpenRouterReply("{\"incidentType\":\"fire\",\"severityScore\":1,\"summary\":\"Cháy rác\"}"));

        var r = await Svc(FakeHttpHandler.Throws(new HttpRequestException("refused")), or).AnalyzeAsync(Img);

        Assert.Equal(SeverityLevel.Level1, r.SeverityLevel);
    }

    [Fact]
    public async Task LocalDisabled_GoesStraightToOpenRouter()
    {
        var local = FakeHttpHandler.Json(LocalJson);
        var or = FakeHttpHandler.Json(OpenRouterReply("{\"incidentType\":\"fire\",\"severityScore\":2,\"summary\":\"x\"}"));

        await Svc(local, or, useLocal: false).AnalyzeAsync(Img);

        Assert.Empty(local.Requests);
        Assert.Single(or.Requests);
    }

    // -------- OpenRouter parsing --------
    [Fact]
    public async Task OpenRouter_SendsBearerKeyModelAndImage()
    {
        var or = FakeHttpHandler.Json(OpenRouterReply("{\"incidentType\":\"fire\",\"severityScore\":2,\"summary\":\"x\"}"));

        await Svc(null, or, useLocal: false).AnalyzeAsync(Img, "ghi chú");

        var req = or.Requests.Single();
        Assert.Equal("https://openrouter.test/api/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer or-key", req.Headers.Authorization!.ToString());
        Assert.Contains("qwen/qwen3-vl-32b-instruct", or.Bodies[0]);
        Assert.Contains(Img, or.Bodies[0]);
    }

    [Fact]
    public async Task OpenRouter_AcceptsMarkdownFencedJsonAndSnakeCaseFields()
    {
        var content = "```json\n{\"incident_type\":\"fire\",\"severity_score\":3,\"description\":\"Cháy kho\",\"hazard_tags\":[\"fire\"],\"is_traffic_jam\":true,\"confidence\":0.9}\n```";

        var r = await Svc(null, FakeHttpHandler.Json(OpenRouterReply(content)), useLocal: false).AnalyzeAsync(Img);

        Assert.True(r.IsSuccess);
        Assert.Equal(SeverityLevel.Level3, r.SeverityLevel);
        Assert.Equal(TrafficJamStatus.Suspected, r.TrafficJamStatus);
        Assert.Equal(new[] { "fire" }, r.HazardTags);
    }

    [Theory]
    [InlineData("Đây là một đám cháy lớn, mức 3.")]                                   // prose only
    [InlineData("{\"incidentType\":\"flood\",\"severityScore\":2,\"summary\":\"x\"}")]  // unknown type
    [InlineData("{\"incidentType\":\"fire\",\"severityScore\":5,\"summary\":\"x\"}")]   // severity out of range
    [InlineData("{\"incidentType\":\"fire\",\"severityScore\":2.5,\"summary\":\"x\"}")] // non-integer severity
    [InlineData("{\"incidentType\":\"fire\",\"severityScore\":2}")]                     // missing summary
    [InlineData("{\"incidentType\":\"fire\",\"severityScore\":2,\"summary\":\"x\",\"confidenceScore\":1.5}")]
    [InlineData("{\"incidentType\":\"fire\",\"severityScore\":2,\"summary\":\"x\",\"hazardTags\":\"fire\"}")]
    [InlineData("")]
    public async Task OpenRouter_InvalidModelOutput_FallsBackToUnclassified(string content)
    {
        var r = await Svc(null, FakeHttpHandler.Json(OpenRouterReply(content)), useLocal: false).AnalyzeAsync(Img);

        Assert.False(r.IsSuccess);
        Assert.Equal(SeverityLevel.Unclassified, r.SeverityLevel);
        Assert.Contains("NeedsHumanReview", r.HazardTags);
        Assert.Equal(TrafficJamStatus.Unverified, r.TrafficJamStatus);
    }

    [Fact]
    public async Task OpenRouter_HttpError_FallsBackToUnclassified()
    {
        var r = await Svc(null, FakeHttpHandler.Json("{\"error\":\"rate limited\"}", HttpStatusCode.TooManyRequests), useLocal: false).AnalyzeAsync(Img);

        Assert.False(r.IsSuccess);
        Assert.Equal(SeverityLevel.Unclassified, r.SeverityLevel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task NoOpenRouterKey_ReturnsFallbackWithoutCalling(string? key)
    {
        var or = FakeHttpHandler.Json("{}");

        var r = await Svc(null, or, apiKey: key, useLocal: false).AnalyzeAsync(Img);

        Assert.False(r.IsSuccess);
        Assert.Equal(SeverityLevel.Unclassified, r.SeverityLevel);
        Assert.Empty(or.Requests);
    }

    [Fact]
    public async Task OpenRouter_MissingTrafficJamField_IsUnverified()
    {
        var r = await Svc(null, FakeHttpHandler.Json(OpenRouterReply("{\"incidentType\":\"none\",\"severityScore\":0,\"summary\":\"Nấu ăn\"}")), useLocal: false).AnalyzeAsync(Img);

        Assert.True(r.IsSuccess);
        Assert.Equal(IncidentEventType.None, r.EventType);
        Assert.Equal(TrafficJamStatus.Unverified, r.TrafficJamStatus);
    }
}
