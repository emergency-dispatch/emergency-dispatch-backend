namespace EmergencyDispatch.Application.DTOs.Ai;

/// <summary>
/// Công việc phân tích AI được đẩy vào hàng đợi nền (System.Threading.Channels)
/// sau khi sự cố SOS đã được lưu và phản hồi ngay cho người báo.
/// </summary>
/// <param name="IncidentId">ID sự cố cần phân tích</param>
/// <param name="MediaUrl">URL ảnh/video hiện trường gửi tới mô hình Vision-Language</param>
/// <param name="AdditionalContext">Mô tả người dân cung cấp (bối cảnh bổ sung cho AI)</param>
/// <param name="EnqueuedAtUtc">Thời điểm đưa vào hàng đợi (đo thời gian chờ hàng đợi - Telemetry)</param>
public sealed record AiAnalysisJob(
    Guid IncidentId,
    string MediaUrl,
    string? AdditionalContext,
    DateTime EnqueuedAtUtc);
