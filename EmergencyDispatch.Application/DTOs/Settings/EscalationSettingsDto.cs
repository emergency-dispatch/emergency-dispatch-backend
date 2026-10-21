namespace EmergencyDispatch.Application.DTOs.Settings;

/// <summary>
/// Cấu hình Escalation Daemon (FR-SYS-02) — áp dụng cho sự cố Level 3 và Level 0 chưa có đội nhận
/// </summary>
public class EscalationSettingsDto
{
    /// <summary>
    /// Ngưỡng chờ ban ngày (phút)
    /// </summary>
    public double DayThresholdMinutes { get; set; } = 5;

    /// <summary>
    /// Ngưỡng chờ ban đêm (phút) — BR-19 giảm 50% so với ban ngày
    /// </summary>
    public double NightThresholdMinutes { get; set; } = 2.5;

    /// <summary>
    /// Giờ bắt đầu khung đêm "HH:mm" (giờ Việt Nam), có thể qua nửa đêm
    /// </summary>
    public string NightStart { get; set; } = "22:00";

    /// <summary>
    /// Giờ kết thúc khung đêm "HH:mm" (giờ Việt Nam)
    /// </summary>
    public string NightEnd { get; set; } = "06:00";

    /// <summary>
    /// Sự cố đã leo thang: bán kính tìm xe nhân hệ số này
    /// </summary>
    public double RadiusMultiplier { get; set; } = 2;

    /// <summary>
    /// Bán kính tìm xe tối đa (km)
    /// </summary>
    public double MaxRadiusKm { get; set; } = 30;
}
