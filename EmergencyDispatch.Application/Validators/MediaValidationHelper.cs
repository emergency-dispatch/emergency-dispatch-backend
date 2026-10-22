using EmergencyDispatch.Application.Localization;

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
                return (false, LocalizationHelper.Localize("Kích thước hình ảnh ({0:F1}MB) vượt quá giới hạn cho phép (10MB).", fileLength / (1024 * 1024.0)));
            }
            return (true, null);
        }

        if (AllowedVideoMimeTypes.Contains(contentType))
        {
            if (fileLength > MaxVideoSizeBytes)
            {
                return (false, LocalizationHelper.Localize("Kích thước video ({0:F1}MB) vượt quá giới hạn cho phép (30MB).", fileLength / (1024 * 1024.0)));
            }
            return (true, null);
        }

        return (false, LocalizationHelper.Localize("Định dạng tệp '{0}' không được hỗ trợ. Hệ thống chỉ chấp nhận ảnh (JPEG, PNG, WEBP) hoặc video (MP4, MOV).", contentType));
    }
}
