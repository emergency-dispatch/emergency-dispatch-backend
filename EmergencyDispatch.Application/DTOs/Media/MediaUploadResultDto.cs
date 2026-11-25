using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Media;

public class MediaUploadResultDto
{
    public string Url { get; set; } = string.Empty;
    public string? PublicId { get; set; }
    public MediaType MediaType { get; set; } = MediaType.Photo;
    public long FileSizeBytes { get; set; }
    public string? MimeType { get; set; }
    public double? DurationSeconds { get; set; }
    public string? Transcript { get; set; }
    public string? TranscriptionStatus { get; set; }
    /// <summary>Audio only: opaque, user-bound upload receipt required when attaching to an incident.</summary>
    public string? UploadToken { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
