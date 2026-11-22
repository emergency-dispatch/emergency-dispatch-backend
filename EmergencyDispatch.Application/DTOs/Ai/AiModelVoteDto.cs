namespace EmergencyDispatch.Application.DTOs.Ai;

/// <summary>
/// Phiếu của một model trong chế độ "3 AI bỏ phiếu" (TriageVL-3B + ResNet-50 + YOLO11n-cls) của AI tự điều phối
/// </summary>
public class AiModelVoteDto
{
    public string Model { get; set; } = string.Empty;

    /// <summary>fire / accident / none; null khi model không trả lời được</summary>
    public string? IncidentType { get; set; }

    /// <summary>0 = none, 1–3 = Level</summary>
    public int Severity { get; set; }

    /// <summary>Độ tin cậy đo từ model (TriageVL: xác suất token; CNN: xác suất softmax)</summary>
    public double? Confidence { get; set; }

    public string? ConfidenceSource { get; set; }

    /// <summary>false = model lỗi / chưa nạp, không tính phiếu</summary>
    public bool IsAvailable { get; set; }

    /// <summary>Phiếu nằm trong nhóm đa số (cùng loại sự cố với TriageVL và độ tin cậy ≥ ngưỡng)</summary>
    public bool InMajority { get; set; }

    public string? Error { get; set; }
}
