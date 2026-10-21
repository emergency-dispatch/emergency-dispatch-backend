namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Tham số vận hành hệ thống lưu dạng JSON theo khóa (VD "escalation", "dispatch") — Admin chỉnh được không cần sửa code
/// </summary>
public class SystemSetting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "{}";

    public Guid? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}
