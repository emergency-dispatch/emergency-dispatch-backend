using EmergencyDispatch.Application.DTOs.Ai;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Dịch vụ phân tích rủi ro và nhận dạng sự cố bằng AI Vision-Language (Qwen2.5-VL)
/// </summary>
public interface IAiClassificationService
{
    /// <summary>
    /// Gửi media (ảnh/video) đến mô hình Vision-Language để phát hiện nguy cơ và chấm điểm Severity (1-5)
    /// Đảm bảo luôn trả về kết quả an toàn (Fallback Severity = 0 nếu có lỗi).
    /// </summary>
    /// <param name="mediaUrl">Đường dẫn ảnh hoặc video</param>
    /// <param name="additionalContext">Bối cảnh thêm hoặc mô tả từ người dân</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    /// <returns>AiClassificationResultDto chứa hazard tags, severity và metadata</returns>
    Task<AiClassificationResultDto> AnalyzeAsync(string mediaUrl, string? additionalContext = null, CancellationToken cancellationToken = default);
}
