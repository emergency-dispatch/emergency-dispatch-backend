using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Quy tắc tự động leo thang sự cố khi quá hạn điều phối hoặc không có đội cứu hộ tiếp nhận
/// </summary>
public class EscalationRule : BaseEntity
{
    public SeverityLevel MinSeverityLevel { get; set; } = SeverityLevel.Level4;
    public int TimeoutMinutes { get; set; } = 5;
    public string NotifyRoles { get; set; } = "[\"Admin\",\"Operator\"]";
    public int ExpandRadiusPercent { get; set; } = 50;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    // Navigation properties
    public ICollection<EscalationLog> Logs { get; set; } = new List<EscalationLog>();
}
