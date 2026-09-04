using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Media;

public class MediaUploadResultDto
{
    public string Url { get; set; } = string.Empty;
    public string? PublicId { get; set; }
    public MediaType MediaType { get; set; } = MediaType.Photo;
    public long FileSizeBytes { get; set; }
    public string? MimeType { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
