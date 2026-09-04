namespace EmergencyDispatch.Application.DTOs.Ai;

/// <summary>
/// Yêu cầu phân tích hình ảnh/video hiện trường
/// </summary>
public class AiAnalyzeRequestDto
{
    /// <summary>
    /// Đường dẫn ảnh hoặc video cần AI phân tích
    /// </summary>
    public string MediaUrl { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả sơ bộ từ người dân hoặc bối cảnh bổ sung (optional)
    /// </summary>
    public string? AdditionalContext { get; set; }
}
