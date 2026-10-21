namespace EmergencyDispatch.Application.DTOs.Benchmark;

/// <summary>
/// Số liệu thực nghiệm phục vụ RQ2 (thuật toán điều phối) và RQ3 (AI hỗ trợ điều phối)
/// </summary>
public class TelemetrySummaryDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int IncidentCount { get; set; }

    /// <summary>T_AI: từ lúc tạo sự cố đến khi có kết quả AI</summary>
    public LatencyStatsDto TAi { get; set; } = new();

    /// <summary>Độ trễ thuần của mô hình AI (ProcessingDurationMs)</summary>
    public LatencyStatsDto AiModelLatency { get; set; } = new();

    /// <summary>T_Operator: từ lúc có kết quả AI (hoặc lúc tạo nếu không có AI) đến khi Operator xác minh</summary>
    public LatencyStatsDto TOperator { get; set; } = new();
    public LatencyStatsDto TOperatorWithAi { get; set; } = new();
    public LatencyStatsDto TOperatorManual { get; set; } = new();

    /// <summary>Từ lúc xác minh đến khi phát lệnh điều phối đầu tiên</summary>
    public LatencyStatsDto TDispatch { get; set; } = new();

    /// <summary>T_Response: từ lúc phát lệnh đến khi xe có mặt tại hiện trường (mỗi lượt phân công)</summary>
    public LatencyStatsDto TResponse { get; set; } = new();

    /// <summary>T_Response theo thuật toán gợi ý đã dùng khi phát lệnh (RQ2)</summary>
    public Dictionary<string, LatencyStatsDto> TResponseByAlgorithm { get; set; } = new();

    /// <summary>Từ lúc tạo đến khi đóng hồ sơ</summary>
    public LatencyStatsDto TResolution { get; set; } = new();

    /// <summary>Độ đồng thuận giữa mức độ AI đề xuất và mức độ Operator xác nhận (RQ3)</summary>
    public AiAgreementDto AiAgreement { get; set; } = new();
}

public class LatencyStatsDto
{
    public int Count { get; set; }
    public double? AvgMs { get; set; }
    public double? MedianMs { get; set; }
    public double? P95Ms { get; set; }
    public double? MinMs { get; set; }
    public double? MaxMs { get; set; }
}

public class AiAgreementDto
{
    /// <summary>Số sự cố có kết quả AI thành công và đã được Operator xác minh</summary>
    public int SampleCount { get; set; }
    public double? Accuracy { get; set; }
    public double? CohensKappa { get; set; }

    /// <summary>Tỷ lệ Operator ghi đè mức độ AI</summary>
    public double? OverrideRate { get; set; }

    /// <summary>Thứ tự nhãn của ma trận: Unclassified, Level1, Level2, Level3</summary>
    public List<string> Labels { get; set; } = new() { "Unclassified", "Level1", "Level2", "Level3" };

    /// <summary>ConfusionMatrix[ai][operator]</summary>
    public int[][] ConfusionMatrix { get; set; } = Enumerable.Range(0, 4).Select(_ => new int[4]).ToArray();
}
