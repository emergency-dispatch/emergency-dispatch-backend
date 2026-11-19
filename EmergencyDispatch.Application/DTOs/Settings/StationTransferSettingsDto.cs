namespace EmergencyDispatch.Application.DTOs.Settings;

/// <summary>Automatic ownership handover; independent of rescue assignment acceptance.</summary>
public class StationTransferSettingsDto
{
    // Enable explicitly after deployment; legacy incidents are never enrolled by a worker.
    public bool Enabled { get; set; } = false;
    public double ResponseTimeoutMultiplier { get; set; } = 2;
    public int MaxAutomaticTransfers { get; set; } = 2;
}
