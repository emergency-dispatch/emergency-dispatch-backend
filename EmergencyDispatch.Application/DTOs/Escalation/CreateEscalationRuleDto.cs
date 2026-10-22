using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Escalation;

/// <summary>
/// Quy tắc leo thang: quyết định mức độ nào bị quét và vai trò nào nhận cảnh báo.
/// Ngưỡng thời gian (ngày/đêm) và hệ số mở rộng bán kính cấu hình chung tại PUT /api/settings/escalation.
/// </summary>
public class CreateEscalationRuleDto
{
    /// <summary>
    /// Quét các sự cố có mức độ từ mức này trở lên (Level 0 / chưa phân loại luôn được quét)
    /// </summary>
    [Required]
    [Range(1, 3, ErrorMessage = "Mức độ leo thang tối thiểu phải từ 1 đến 3")]
    public SeverityLevel MinSeverityLevel { get; set; } = SeverityLevel.Level3;

    /// <summary>
    /// Mảng JSON các vai trò nhận cảnh báo, chỉ gồm "Admin", "Operator", "RescueStaff". VD: ["Admin","Operator"]
    /// </summary>
    public string NotifyRoles { get; set; } = "[\"Admin\",\"Operator\"]";

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Description { get; set; }
}
