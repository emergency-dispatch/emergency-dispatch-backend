using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Application.DTOs.Incident;

namespace EmergencyDispatch.Application.DTOs.Media;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public class AudioAttachmentInputDto
{
    [Required, MaxLength(32768)]
    public string UploadToken { get; set; } = string.Empty;
}

public sealed record AudioUploadReceipt(IncidentMediaDto Media, Guid? UploadedByUserId);
public sealed record AudioPlaybackUrlDto(string Url, DateTime ExpiresAt);
