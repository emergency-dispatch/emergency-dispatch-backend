namespace EmergencyDispatch.Application.DTOs.Settings;

/// <summary>
/// Tự gom báo cáo trùng (BR-07): SOS mới gần một sự cố đang hoạt động, trong khung thời gian, cùng loại (nếu biết)
/// được gom vào sự cố đó thay vì tạo dòng mới trong hàng đợi. Admin chỉnh tại Settings.
/// </summary>
public class DuplicateDetectionSettingsDto
{
    /// <summary>
    /// Bật / tắt tự gom
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Khoảng cách tối đa tới sự cố gốc (mét)
    /// </summary>
    public double RadiusMeters { get; set; } = 200;

    /// <summary>
    /// Khung thời gian (phút) tính từ lần báo gần nhất tại sự cố gốc
    /// </summary>
    public double WindowMinutes { get; set; } = 15;
}
