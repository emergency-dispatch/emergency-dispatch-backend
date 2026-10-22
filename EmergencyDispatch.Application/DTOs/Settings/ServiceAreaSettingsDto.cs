namespace EmergencyDispatch.Application.DTOs.Settings;

/// <summary>
/// Khu vực phục vụ của hệ thống (VD: Khu Công nghệ cao): tâm + bán kính.
/// SOS ngoài vùng vẫn được tiếp nhận nhưng gắn cờ và báo Operator / Admin.
/// </summary>
public class ServiceAreaSettingsDto
{
    /// <summary>
    /// Chưa bật thì không kiểm tra (mặc định tắt cho tới khi Admin cấu hình)
    /// </summary>
    public bool Enabled { get; set; }

    public string Name { get; set; } = "Khu vực phục vụ";
    public double CenterLat { get; set; }
    public double CenterLng { get; set; }
    public double RadiusKm { get; set; } = 10;
}
