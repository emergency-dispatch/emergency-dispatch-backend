using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Application.DTOs.Incident;

namespace EmergencyDispatch.Application.DTOs.Media;

public class AudioAttachmentInputDto
{
    [Required, MaxLength(32768)]
    public string UploadToken { get; set; } = string.Empty;
    /// <summary>Speech-to-text supplied and reviewed by the client. Missing text requires operator review.</summary>
    [MaxLength(4000)]
    public string? Transcript { get; set; }
}

public sealed record AudioUploadReceipt(IncidentMediaDto Media, Guid? UploadedByUserId);
public sealed record AudioPlaybackUrlDto(string Url, DateTime ExpiresAt);
