namespace EmergencyDispatch.Application.DTOs.Settings;

/// <summary>
/// Khu vực phục vụ của hệ thống (VD: Khu Công nghệ cao): tâm + bán kính.
/// SOS ngoài vùng bị từ chối khi bật RejectOutside; nếu tắt thì vẫn tiếp nhận nhưng gắn cờ và báo Operator / Admin.
/// Sự cố do Operator tạo (điện thoại, bộ đàm) không bị chặn.
/// </summary>
public class ServiceAreaSettingsDto
{
    /// <summary>
    /// Chưa bật thì không kiểm tra (mặc định tắt cho tới khi Admin cấu hình)
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Từ chối SOS của người dân / khách nằm ngoài khu vực (mặc định bật). Tắt = tiếp nhận nhưng gắn cờ ngoài vùng.
    /// </summary>
    public bool RejectOutside { get; set; } = true;

    public string Name { get; set; } = "Khu vực phục vụ";
    public double CenterLat { get; set; }
    public double CenterLng { get; set; }
    public double RadiusKm { get; set; } = 10;
}
