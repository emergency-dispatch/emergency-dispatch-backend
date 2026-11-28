namespace EmergencyDispatch.Application.DTOs.Settings;

public class CitizenIncidentAlertSettingsDto
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Thời gian chờ giữa các SOS đang hoạt động của cùng tài khoản/số điện thoại (0–60 phút).
    /// 0 = tắt bước kiểm tra cooldown; JSON cũ không có trường này dùng mặc định 3 phút.
    /// Độc lập với Enabled (công tắc thông báo khu vực Citizen).
    /// </summary>
    public int ReportCooldownMinutes { get; set; } = 3;
    // Set by the server when enabling; clients cannot backdate publication.
    public DateTime? EnabledFromUtc { get; set; }
}
