using EmergencyDispatch.Application.DTOs.Ai;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Hai model CNN phân 7 mức (ResNet-50, YOLO11n-cls) chạy trong YoloService, dùng cho chế độ "3 AI bỏ phiếu"
/// </summary>
public interface ICnnTriageService
{
    /// <summary>
    /// Trả phiếu của từng model (luôn đủ 2 phần tử; model lỗi / service không gọi được → IsAvailable = false)
    /// </summary>
    Task<IReadOnlyList<AiModelVoteDto>> ClassifyAsync(string mediaUrl, CancellationToken cancellationToken = default);
}
