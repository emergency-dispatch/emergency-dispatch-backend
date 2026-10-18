using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Escalation;

/// <summary>
/// DTO cảnh báo leo thang khẩn cấp phát qua SignalR tới Operators và Admins
/// </summary>
public class EscalationAlertDto
{
    public Guid IncidentId { get; set; }
    public string IncidentTitle { get; set; } = string.Empty;
    public SeverityLevel Severity { get; set; }
    public IncidentStatus Status { get; set; }
    public string LocationAddress { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public Guid RuleId { get; set; }
    public int TimeoutMinutes { get; set; }
    public double ElapsedMinutes { get; set; }
    public string ActionTaken { get; set; } = string.Empty;
    public int ExpandRadiusPercent { get; set; }
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
}
