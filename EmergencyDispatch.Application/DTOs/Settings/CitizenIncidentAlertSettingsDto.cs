namespace EmergencyDispatch.Application.DTOs.Settings;

public class CitizenIncidentAlertSettingsDto
{
    public bool Enabled { get; set; } = true;
    // Set by the server when enabling; clients cannot backdate publication.
    public DateTime? EnabledFromUtc { get; set; }
}
