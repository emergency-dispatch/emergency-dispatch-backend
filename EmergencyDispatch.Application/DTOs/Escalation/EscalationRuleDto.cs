using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Escalation;

public class EscalationRuleDto
{
    public Guid Id { get; set; }
    public SeverityLevel MinSeverityLevel { get; set; }
    public string NotifyRoles { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Ngưỡng đang áp dụng, lấy từ cấu hình chung /api/settings/escalation (chỉ đọc)
    public double EffectiveDayThresholdMinutes { get; set; }
    public double EffectiveNightThresholdMinutes { get; set; }
    public string EffectiveNightStart { get; set; } = string.Empty;
    public string EffectiveNightEnd { get; set; } = string.Empty;
    public double EffectiveRadiusMultiplier { get; set; }
    public double EffectiveMaxRadiusKm { get; set; }
}
