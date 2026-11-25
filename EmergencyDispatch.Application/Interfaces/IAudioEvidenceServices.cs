using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Media;

namespace EmergencyDispatch.Application.Interfaces;

public interface IAudioUploadTokenService
{
    string Create(IncidentMediaDto media, Guid? uploadedByUserId);
    bool TryRead(string? token, Guid? userId, out AudioUploadReceipt? receipt);
}

public interface IAudioPlaybackService
{
    AudioPlaybackUrlDto? CreatePlaybackUrl(IncidentMediaDto media);
}

