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
}

public class LoadAwareWeightsDto
{
    public double Distance { get; set; } = 0.5;
    public double StationLoad { get; set; } = 0.3;
    public double ActiveMissions { get; set; } = 0.2;
}
