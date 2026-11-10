namespace EmergencyDispatch.Application.DTOs.Settings;

/// <summary>
/// Tham số động cơ gợi ý điều phối (FR-OP-04) — chỉnh được để chạy thí nghiệm RQ2 mà không sửa code
/// </summary>
public class DispatchSettingsDto
{
    /// <summary>
    /// Trọng số Load-Aware (BR-11). Tổng ba trọng số phải bằng 1.
    /// </summary>
    public LoadAwareWeightsDto Weights { get; set; } = new();

    /// <summary>
    /// Các nấc bán kính tìm xe tăng dần (BR-12), mặc định 10 → 15 → 22.5 → 30 km
    /// </summary>
    public List<double> RadiusStepsKm { get; set; } = new() { 10, 15, 22.5, 30 };

    /// <summary>
    /// Thời gian hồi sức (CoolDown) của xe sau khi nghiệm thu nhiệm vụ (BR-27)
    /// </summary>
    public double RecoveryMinutes { get; set; } = 5;

    /// <summary>
    /// Sự cố chưa được xác minh quá số giờ này sẽ tự hủy (CancellationReason.AutoExpired)
    /// </summary>
    public double AutoExpireHours { get; set; } = 24;

    /// <summary>
    /// AI tự động điều phối: TriageVL đủ tin cậy (xác suất token ≥ ngưỡng) với sự cố cháy nổ / tai nạn Level 1–3
    /// → hệ thống tự xác minh và điều xe theo quy tắc loại xe. Mặc định tắt; Admin bật khi đã dùng ai_service.py có độ tin cậy thật
    /// </summary>
    public bool AutoDispatchEnabled { get; set; } = false;

    /// <summary>
    /// Ngưỡng độ tin cậy tối thiểu (0.5–1.0), mặc định 0.90
    /// </summary>
    public double AutoDispatchMinConfidence { get; set; } = 0.90;

    /// <summary>
    /// Trước khi tự điều xe: có sự cố khác đã được điều xe trong bán kính này (mét, không xét loại) → nghi trùng, chuyển Operator
    /// </summary>
    public double AutoDispatchDuplicateRadiusMeters { get; set; } = 500;

    /// <summary>
    /// Khung thời gian (phút) xét nghi trùng trước khi tự điều xe
    /// </summary>
    public double AutoDispatchDuplicateWindowMinutes { get; set; } = 30;
}

public class LoadAwareWeightsDto
{
    public double Distance { get; set; } = 0.5;
    public double StationLoad { get; set; } = 0.3;
    public double ActiveMissions { get; set; } = 0.2;
}
