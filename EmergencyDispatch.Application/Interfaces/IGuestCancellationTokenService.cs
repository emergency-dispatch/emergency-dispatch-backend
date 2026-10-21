namespace EmergencyDispatch.Application.Interfaces;

public interface IGuestCancellationTokenService
{
    string Create(Guid incidentId);
    bool Validate(Guid incidentId, string? token);
}
