using System.Diagnostics;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

/// <summary>
/// Dual-Engine AI Orchestrator: Chạy song song YOLO (nhanh, local) và Qwen-VL (sâu, cloud)
/// rồi hợp nhất kết quả theo ma trận đồng thuận (Consensus Decision Matrix).
///
/// Fault Tolerance:
/// - Nếu YOLO fail → dùng kết quả Qwen-VL
/// - Nếu Qwen-VL fail → dùng kết quả YOLO
/// - Nếu cả hai fail → fallback Severity = 0 (NeedsHumanReview)
/// - Nếu cả hai thành công → consensus logic chọn kết quả tốt nhất
/// </summary>
public class DualEngineAiService : IAiClassificationService
{
    private readonly AiClassificationService _qwenService;
    private readonly YoloClassificationService _yoloService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DualEngineAiService> _logger;

    public DualEngineAiService(
        AiClassificationService qwenService,
        YoloClassificationService yoloService,
        IConfiguration configuration,
        ILogger<DualEngineAiService> logger)
    {
        _qwenService = qwenService;
        _yoloService = yoloService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AiClassificationResultDto> AnalyzeAsync(
        string mediaUrl,
        string? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var overallStopwatch = Stopwatch.StartNew();
        var dualEngineEnabled = _configuration.GetValue("YoloService:Enabled", false);

        // Nếu YOLO bị tắt → chỉ dùng Qwen-VL (legacy mode)
        if (!dualEngineEnabled)
        {
            _logger.LogDebug("Dual-Engine disabled → falling back to Qwen-VL only mode.");
            return await _qwenService.AnalyzeAsync(mediaUrl, additionalContext, cancellationToken);
        }

        _logger.LogInformation("🚀 Dual-Engine AI bắt đầu: chạy song song YOLO + Qwen-VL cho media: {Url}", mediaUrl);

        // ── Chạy song song hai engine ──────────────────────────────────
        Task<AiClassificationResultDto?> yoloTask;
        Task<AiClassificationResultDto> qwenTask;

        try
        {
            yoloTask = SafeYoloAnalyzeAsync(mediaUrl, cancellationToken);
            qwenTask = SafeQwenAnalyzeAsync(mediaUrl, additionalContext, cancellationToken);

            await Task.WhenAll((Task)yoloTask, qwenTask);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Dual-Engine WhenAll encountered an unexpected error");
            overallStopwatch.Stop();
            return AiClassificationResultDto.CreateFallback(
                $"Dual-Engine error: {ex.Message}",
                durationMs: overallStopwatch.ElapsedMilliseconds);
        }

        var yoloResult = await yoloTask;
        var qwenResult = await qwenTask;

        overallStopwatch.Stop();

        // ── Consensus Decision Matrix ──────────────────────────────────
        var finalResult = ApplyConsensus(yoloResult, qwenResult, overallStopwatch.ElapsedMilliseconds);

        _logger.LogInformation(
            "✅ Dual-Engine AI hoàn tất: FinalType={Type}, FinalSeverity={Sev}, Engine={Engine} ({TotalMs}ms)",
            finalResult.IncidentType,
            finalResult.SeverityScore,
            finalResult.ModelName,
            finalResult.ProcessingDurationMs);

        return finalResult;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Safe wrappers: catch mọi exception, không để fail propagate
    // ─────────────────────────────────────────────────────────────────────

    private async Task<AiClassificationResultDto?> SafeYoloAnalyzeAsync(
        string mediaUrl, CancellationToken ct)
    {
        try
        {
            return await _yoloService.ClassifyAsync(mediaUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YOLO engine failed (safe-wrapped). Continuing with Qwen-VL.");
            return null;
        }
    }

    private async Task<AiClassificationResultDto> SafeQwenAnalyzeAsync(
        string mediaUrl, string? context, CancellationToken ct)
    {
        try
        {
            return await _qwenService.AnalyzeAsync(mediaUrl, context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qwen-VL engine failed (safe-wrapped). Will rely on YOLO if available.");
            return AiClassificationResultDto.CreateFallback(
                $"Qwen-VL failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Consensus Logic
    // ─────────────────────────────────────────────────────────────────────

    private AiClassificationResultDto ApplyConsensus(
        AiClassificationResultDto? yolo,
        AiClassificationResultDto qwen,
        long totalMs)
    {
        var yoloOk = yolo is { IsSuccess: true };
        var qwenOk = qwen.IsSuccess;

        // ─── Case 1: Cả hai đều fail ───
        if (!yoloOk && !qwenOk)
        {
            _logger.LogWarning("⚠️ Cả YOLO và Qwen-VL đều thất bại → Fallback NeedsHumanReview");
            var fallback = AiClassificationResultDto.CreateFallback(
                "Cả hai engine AI đều không khả dụng.",
                durationMs: totalMs);
            fallback.ModelName = "Dual-Engine (Both Failed)";
            return fallback;
        }

        // ─── Case 2: Chỉ YOLO thành công (Qwen-VL fail/unavailable) ───
        if (yoloOk && !qwenOk)
        {
            _logger.LogInformation("Qwen-VL unavailable → Chỉ có YOLO. Không tự tiện gán Level 1; đặt Severity=0 (Unclassified) và yêu cầu Operator thẩm định.");

            var (singleJamStatus, singleJamEv, _) = ResolveTrafficJamConsensus(yolo, null);
            yolo!.SeverityScore = 0;
            yolo.IsTrafficJam = false; // AI chỉ dừng ở mức Suspected/Unverified, không tự ý gán Confirmed
            yolo.TrafficJamStatus = singleJamStatus;
            yolo.TrafficJamEvidence = singleJamEv;

            if (!yolo.HazardTags.Contains("NeedsHumanReview"))
                yolo.HazardTags.Add("NeedsHumanReview");
            if (!yolo.HazardTags.Contains("PreliminaryYoloDetection"))
                yolo.HazardTags.Add("PreliminaryYoloDetection");

            if (singleJamStatus == TrafficJamStatus.Suspected)
                yolo.HazardTags.Add("SuspectedTrafficJam");
            else if (singleJamStatus == TrafficJamStatus.Unverified)
                yolo.HazardTags.Add("TrafficJamUnverified");

            yolo.ModelName = $"YOLO Only (Qwen-VL unavailable) | {yolo.ModelName}";
            yolo.ProcessingDurationMs = totalMs;
            return yolo;
        }

        // ─── Case 3: Chỉ Qwen-VL thành công ───
        if (!yoloOk && qwenOk)
        {
            _logger.LogInformation("YOLO unavailable → Sử dụng kết quả Qwen-VL duy nhất");
            var (singleJamStatus, singleJamEv, _) = ResolveTrafficJamConsensus(null, qwen);
            qwen.IsTrafficJam = false; // AI không tự ý gán Confirmed
            qwen.TrafficJamStatus = singleJamStatus;
            qwen.TrafficJamEvidence = singleJamEv;

            if (singleJamStatus == TrafficJamStatus.Suspected && !qwen.HazardTags.Contains("SuspectedTrafficJam"))
                qwen.HazardTags.Add("SuspectedTrafficJam");
            else if (singleJamStatus == TrafficJamStatus.Unverified && !qwen.HazardTags.Contains("TrafficJamUnverified"))
                qwen.HazardTags.Add("TrafficJamUnverified");

            qwen.ModelName = $"Qwen-VL Only (YOLO unavailable) | {qwen.ModelName}";
            qwen.ProcessingDurationMs = totalMs;
            return qwen;
        }

        // ─── Case 4: CẢ HAI thành công → Consensus ───
        return MergeResults(yolo!, qwen, totalMs);
    }

    /// <summary>
    /// Ma trận đồng thuận khi cả hai engine đều trả kết quả.
    /// Nguyên tắc:
    /// - Khi YOLO chỉ chạy được Jam (Accident-fire unverified): Tin Qwen-VL cho sự cố, chỉ lấy Jam từ YOLO.
    /// - Mức độ nghiêm trọng (Severity): Qwen-VL đánh giá. Không gán Level 1 tự động khi bất đồng hoặc thiếu Qwen.
    /// - Bất đồng giữa 2 engine: Giữ Severity=0 (Unclassified) hoặc gán cờ NeedsHumanReview rõ ràng.
    /// </summary>
    private AiClassificationResultDto MergeResults(
        AiClassificationResultDto yolo,
        AiClassificationResultDto qwen,
        long totalMs)
    {
        var yoloType = (yolo.IncidentType ?? "none").ToLowerInvariant().Trim();
        var qwenType = (qwen.IncidentType ?? "none").ToLowerInvariant().Trim();
        var yoloConf = yolo.ConfidenceScore ?? 0.0;
        var qwenConf = qwen.ConfidenceScore ?? 0.0;

        // Phân định kẹt xe theo ma trận 9 trường hợp (không dùng phép OR thô sơ)
        var (jamStatus, jamEvidence, jamNeedsReview) = ResolveTrafficJamConsensus(yolo, qwen);

        // ── Kiểm tra nếu YOLO bị lỗi phần Tai nạn/Hỏa hoạn (chỉ chạy được Jam) ──
        var yoloAfEvaluated = yoloType != "unverified" && !yolo.HazardTags.Contains("YoloAccidentFireUnverified");
        if (!yoloAfEvaluated)
        {
            _logger.LogInformation("YOLO chỉ đánh giá được Jam (Accident-Fire unverified) → Dùng loại sự cố & severity từ Qwen-VL, ghép Jam consensus.");
            var jamTags = new HashSet<string>(qwen.HazardTags.Concat(yolo.HazardTags));
            if (jamNeedsReview)
            {
                jamTags.Add("NeedsHumanReview");
                if (jamStatus == TrafficJamStatus.Suspected) jamTags.Add("SuspectedTrafficJam");
                else if (jamStatus == TrafficJamStatus.Disputed) jamTags.Add("TrafficJamDisputed");
                else if (jamStatus == TrafficJamStatus.Unverified) jamTags.Add("TrafficJamUnverified");
            }

            var mergedJamResult = new AiClassificationResultDto
            {
                IncidentType = qwen.IncidentType,
                SeverityScore = qwen.SeverityScore,
                ConfidenceScore = qwen.ConfidenceScore,
                IsTrafficJam = false, // AI chỉ dừng ở mức Suspected/Disputed, chỉ True khi con người Confirmed
                TrafficJamStatus = jamStatus,
                TrafficJamEvidence = jamEvidence,
                HazardTags = jamTags.ToList(),
                Summary = $"[Dual-Engine | YOLO Jam + Qwen-VL Full]\n• Qwen-VL: {qwen.Summary}\n• YOLO: {yolo.Summary}\n• Tình trạng giao thông: {jamEvidence}",
                IsSuccess = qwen.IsSuccess,
                ModelName = $"Dual-Engine (Qwen-VL + YOLO Jam only) | {qwen.ModelName}",
                RawResponse = BuildMergedRawResponse(yolo, qwen),
                ProcessingDurationMs = totalMs
            };
            return mergedJamResult;
        }

        string finalType;
        int finalSeverity;
        double finalConfidence;
        string consensusNote;
        var needsHumanReview = false;

        // ── 4a: Cả hai đồng ý về loại sự cố ──
        if (yoloType == qwenType)
        {
            finalType = qwenType;
            // Severity do Qwen-VL đánh giá (YOLO không đánh giá severity)
            finalSeverity = qwen.SeverityScore;
            // Weighted average confidence
            finalConfidence = (qwenConf * 0.6) + (yoloConf * 0.4);
            consensusNote = "ĐỒNG THUẬN (Severity do Qwen-VL đánh giá)";
        }
        // ── 4b: Một engine phát hiện nguy hiểm, engine kia nói "none" ──
        else if (yoloType != "none" && qwenType == "none")
        {
            // BẤT ĐỒNG: YOLO phát hiện sự cố, nhưng Qwen-VL kết luận không có sự cố
            // Giữ cảnh báo sự cố từ YOLO nhưng KHÔNG tự tiện gán Level 1.
            // Đặt Severity = 0 (Unclassified) và bắt buộc gắn cờ NeedsHumanReview để Operator thẩm định.
            finalType = yoloType;
            finalSeverity = 0; // Unclassified vì chưa được Qwen xác nhận
            finalConfidence = yoloConf * 0.7; // Giảm confidence vì bất đồng
            consensusNote = "BẤT ĐỒNG (YOLO phát hiện sự cố, Qwen-VL không) → Cần Điều phối viên thẩm định (Severity=0/Unclassified)";
            needsHumanReview = true;
        }
        else if (yoloType == "none" && qwenType != "none")
        {
            finalType = qwenType;
            finalSeverity = qwen.SeverityScore;
            finalConfidence = qwenConf * 0.85; // Qwen reasoning đáng tin hơn
            consensusNote = "Qwen-VL phát hiện sự cố, YOLO reject → Tin Qwen-VL reasoning (Severity do Qwen-VL đánh giá)";
        }
        // ── 4c: Hai engine phát hiện LOẠI SỰ CỐ KHÁC NHAU ──
        // (vd: YOLO nói "fire", Qwen nói "accident")
        else
        {
            // Ưu tiên Qwen-VL (reasoning sâu hơn về ngữ cảnh), severity từ Qwen
            finalType = qwenType;
            finalSeverity = qwen.SeverityScore;
            finalConfidence = Math.Max(qwenConf, yoloConf) * 0.75; // Giảm vì bất đồng
            consensusNote = $"BẤT ĐỒNG LOẠI SỰ CỐ (YOLO={yoloType}, Qwen={qwenType}) → Ưu tiên Qwen-VL reasoning, cần Điều phối viên thẩm định";
            needsHumanReview = true;
        }

        // ── Merge hazard tags ──
        var mergedTags = new HashSet<string>(qwen.HazardTags ?? new List<string>());
        if (yolo.HazardTags != null)
        {
            foreach (var tag in yolo.HazardTags)
                mergedTags.Add(tag);
        }

        if (jamNeedsReview)
        {
            needsHumanReview = true;
            if (jamStatus == TrafficJamStatus.Suspected) mergedTags.Add("SuspectedTrafficJam");
            else if (jamStatus == TrafficJamStatus.Disputed) mergedTags.Add("TrafficJamDisputed");
            else if (jamStatus == TrafficJamStatus.Unverified) mergedTags.Add("TrafficJamUnverified");
        }

        if (needsHumanReview)
        {
            mergedTags.Add("NeedsHumanReview");
            mergedTags.Add("EngineDisagreement");
        }

        // ── Build summary ──
        var yoloSummary = yolo.Summary ?? "";
        var qwenSummary = qwen.Summary ?? "";
        var mergedSummary =
            $"[Dual-Engine | {consensusNote}]\n" +
            $"• YOLO ({yolo.ModelName}): {yoloSummary}\n" +
            $"• Qwen-VL ({qwen.ModelName}): {qwenSummary}\n" +
            $"• Tình trạng giao thông: {jamEvidence}";

        return new AiClassificationResultDto
        {
            IncidentType = finalType,
            IsTrafficJam = false, // AI không tự ý gán Confirmed
            TrafficJamStatus = jamStatus,
            TrafficJamEvidence = jamEvidence,
            SeverityScore = Math.Clamp(finalSeverity, 0, 3),
            ConfidenceScore = Math.Round(finalConfidence, 4),
            HazardTags = mergedTags.ToList(),
            Summary = mergedSummary,
            IsSuccess = true,
            ModelName = $"Dual-Engine (YOLO + Qwen-VL) | Consensus: {consensusNote}",
            RawResponse = BuildMergedRawResponse(yolo, qwen),
            ProcessingDurationMs = totalMs
        };
    }

    /// <summary>
    /// Ma trận đồng thuận 9 trường hợp phân định trạng thái kẹt xe (YOLO x Qwen-VL)
    /// </summary>
    private static (TrafficJamStatus Status, string Evidence, bool NeedsReview) ResolveTrafficJamConsensus(
        AiClassificationResultDto? yolo,
        AiClassificationResultDto? qwen)
    {
        var yoloStatus = yolo?.TrafficJamStatus ?? TrafficJamStatus.Unverified;
        var qwenStatus = qwen?.TrafficJamStatus ?? TrafficJamStatus.Unverified;

        var yoloHasJam = yoloStatus == TrafficJamStatus.Suspected;
        var qwenHasJam = qwenStatus == TrafficJamStatus.Suspected;

        var yoloNoJam = yoloStatus == TrafficJamStatus.NoEvidence;
        var qwenNoJam = qwenStatus == TrafficJamStatus.NoEvidence;

        var yoloError = yoloStatus == TrafficJamStatus.Unverified || yolo == null || !yolo.IsSuccess;
        var qwenError = qwenStatus == TrafficJamStatus.Unverified || qwen == null || !qwen.IsSuccess;

        var yoloEv = !string.IsNullOrWhiteSpace(yolo?.TrafficJamEvidence) ? yolo.TrafficJamEvidence : "YOLO không có chi tiết đếm xe";
        var qwenEv = !string.IsNullOrWhiteSpace(qwen?.TrafficJamEvidence) ? qwen.TrafficJamEvidence : "Qwen-VL không có chi tiết ngữ cảnh";

        // Case 1: Cả hai AI cùng có dấu hiệu
        if (yoloHasJam && qwenHasJam)
        {
            return (
                TrafficJamStatus.Suspected,
                $"Nghi ngờ — Cả hai AI đồng thuận ghi nhận dấu hiệu ùn tắc. (YOLO: {yoloEv} | Qwen: {qwenEv})",
                true
            );
        }

        // Case 2: YOLO báo có dấu hiệu, Qwen không thấy
        if (yoloHasJam && qwenNoJam)
        {
            return (
                TrafficJamStatus.Disputed,
                $"Bất đồng — YOLO ghi nhận đông xe nhưng Qwen-VL không phát hiện ùn tắc. (YOLO: {yoloEv} | Qwen: {qwenEv})",
                true
            );
        }

        // Case 3: YOLO không thấy, Qwen báo có dấu hiệu
        if (yoloNoJam && qwenHasJam)
        {
            return (
                TrafficJamStatus.Disputed,
                $"Bất đồng — Qwen-VL nhận định ùn tắc nhưng YOLO đếm dưới ngưỡng. (Qwen: {qwenEv} | YOLO: {yoloEv})",
                true
            );
        }

        // Case 4: Cả hai đều không thấy dấu hiệu
        if (yoloNoJam && qwenNoJam)
        {
            return (
                TrafficJamStatus.NoEvidence,
                "Cả hai mô hình AI đều chưa phát hiện dấu hiệu ùn tắc trên ảnh.",
                false
            );
        }

        // Case 5: YOLO báo có dấu hiệu, Qwen lỗi/thiếu
        if (yoloHasJam && qwenError)
        {
            return (
                TrafficJamStatus.Suspected,
                $"Nghi ngờ — Chỉ YOLO ghi nhận đông xe ({yoloEv}); Qwen-VL gặp sự cố, thiếu nguồn đối chiếu.",
                true
            );
        }

        // Case 6: Qwen báo có dấu hiệu, YOLO lỗi/thiếu
        if (qwenHasJam && yoloError)
        {
            return (
                TrafficJamStatus.Suspected,
                $"Nghi ngờ — Chỉ Qwen-VL ghi nhận ùn tắc ({qwenEv}); YOLO gặp sự cố, thiếu nguồn đối chiếu.",
                true
            );
        }

        // Case 7: YOLO không thấy dấu hiệu nhưng Qwen lỗi (chưa đủ đối chiếu -> Unverified)
        if (yoloNoJam && qwenError)
        {
            return (
                TrafficJamStatus.Unverified,
                $"Chưa xác minh — YOLO không thấy dấu hiệu ({yoloEv}) nhưng Qwen-VL gặp sự cố, thiếu nguồn độc lập đối chiếu.",
                true
            );
        }

        // Case 8: Qwen không thấy dấu hiệu nhưng YOLO lỗi (chưa đủ đối chiếu -> Unverified)
        if (qwenNoJam && yoloError)
        {
            return (
                TrafficJamStatus.Unverified,
                $"Chưa xác minh — Qwen-VL không thấy dấu hiệu ({qwenEv}) nhưng YOLO gặp sự cố, thiếu nguồn độc lập đối chiếu.",
                true
            );
        }

        // Case 9: Cả hai đều lỗi
        return (
            TrafficJamStatus.Unverified,
            "Chưa thể đánh giá ùn tắc: Cả hai mô hình AI đều không khả dụng hoặc gặp lỗi.",
            true
        );
    }

    private static string BuildMergedRawResponse(
        AiClassificationResultDto yolo,
        AiClassificationResultDto qwen)
    {
        var merged = new
        {
            yolo = new
            {
                model = yolo.ModelName,
                incidentType = yolo.IncidentType,
                severity = yolo.SeverityScore,
                confidence = yolo.ConfidenceScore,
                isTrafficJam = yolo.IsTrafficJam,
                durationMs = yolo.ProcessingDurationMs,
                rawResponse = yolo.RawResponse
            },
            qwen = new
            {
                model = qwen.ModelName,
                incidentType = qwen.IncidentType,
                severity = qwen.SeverityScore,
                confidence = qwen.ConfidenceScore,
                isTrafficJam = qwen.IsTrafficJam,
                durationMs = qwen.ProcessingDurationMs
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(merged, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
