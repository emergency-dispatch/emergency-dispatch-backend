namespace EmergencyDispatch.Application.Validators;

public static class MediaValidationHelper
{
    public const long MaxImageSizeBytes = 10 * 1024 * 1024; // 10 MB
    public const long MaxVideoSizeBytes = 30 * 1024 * 1024; // 30 MB

    public static readonly HashSet<string> AllowedImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    public static readonly HashSet<string> AllowedVideoMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4",
        "video/quicktime",
        "video/webm"
    };

    public static (bool IsValid, string? ErrorMessage) ValidateMedia(string contentType, long fileLength)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return (false, "Không xác định được định dạng tệp tin.");
        }

        if (AllowedImageMimeTypes.Contains(contentType))
        {
            if (fileLength > MaxImageSizeBytes)
            {
                return (false, $"Kích thước hình ảnh ({fileLength / (1024 * 1024.0):F1}MB) vượt quá giới hạn cho phép (10MB).");
            }
            return (true, null);
        }

        if (AllowedVideoMimeTypes.Contains(contentType))
        {
            if (fileLength > MaxVideoSizeBytes)
            {
                return (false, $"Kích thước video ({fileLength / (1024 * 1024.0):F1}MB) vượt quá giới hạn cho phép (30MB).");
            }
            return (true, null);
        }

        return (false, $"Định dạng tệp '{contentType}' không được hỗ trợ. Hệ thống chỉ chấp nhận ảnh (JPEG, PNG, WEBP) hoặc video (MP4, MOV).");
    }
}
