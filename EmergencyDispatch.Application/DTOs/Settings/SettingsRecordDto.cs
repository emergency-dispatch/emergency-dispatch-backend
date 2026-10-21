namespace EmergencyDispatch.Application.DTOs.Settings;

/// <summary>
/// Giá trị cấu hình kèm thông tin lần chỉnh gần nhất
/// </summary>
public class SettingsRecordDto<T>
{
    public T Values { get; set; } = default!;

    /// <summary>
    /// null = chưa ai chỉnh, đang dùng giá trị mặc định
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }
}

/// <summary>
/// Giá trị mặc định theo SRS (để nút "Khôi phục mặc định" của Admin)
/// </summary>
public class SettingsDefaultsDto
{
    public EscalationSettingsDto Escalation { get; set; } = new();
    public DispatchSettingsDto Dispatch { get; set; } = new();
}
