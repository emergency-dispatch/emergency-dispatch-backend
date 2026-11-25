using System.Security.Cryptography;
using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Media;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.DataProtection;

namespace EmergencyDispatch.API.Services;

public sealed class AudioUploadTokenService(IDataProtectionProvider provider) : IAudioUploadTokenService
{
    private readonly ITimeLimitedDataProtector _protector = provider
        .CreateProtector("EmergencyDispatch.AudioUpload.v1").ToTimeLimitedDataProtector();

    public string Create(IncidentMediaDto media, Guid? uploadedByUserId)
        => _protector.Protect(JsonSerializer.Serialize(new AudioUploadReceipt(media, uploadedByUserId)), TimeSpan.FromHours(24));

    public bool TryRead(string? token, Guid? userId, out AudioUploadReceipt? receipt)
    {
        receipt = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 32768) return false;
        try
        {
            var value = JsonSerializer.Deserialize<AudioUploadReceipt>(_protector.Unprotect(token));
            if (value?.Media is not { MediaType: MediaType.Audio, DurationSeconds: > 0 and <= 30 }
                || string.IsNullOrWhiteSpace(value.Media.MediaUrl) || string.IsNullOrWhiteSpace(value.Media.PublicId)
                || value.UploadedByUserId.HasValue && value.UploadedByUserId != userId) return false;
            receipt = value;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        { return false; }
    }
}
