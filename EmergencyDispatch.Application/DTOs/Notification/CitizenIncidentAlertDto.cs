namespace EmergencyDispatch.Application.DTOs.Notification;

/// <summary>Public area-only content. No reporter, victim, medical, media, or dispatch information.</summary>
public class CitizenIncidentAlertDto
{
    public Guid Id { get; set; }
    public string Area { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
