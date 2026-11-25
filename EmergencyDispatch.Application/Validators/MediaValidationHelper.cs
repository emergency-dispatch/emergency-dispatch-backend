using EmergencyDispatch.Application.Localization;

namespace EmergencyDispatch.Application.Validators;

public static class MediaValidationHelper
{
    public const long MaxImageSizeBytes = 10 * 1024 * 1024; // 10 MB
    public const long MaxVideoSizeBytes = 30 * 1024 * 1024; // 30 MB
    public const long MaxAudioSizeBytes = 5 * 1024 * 1024;
    public const double MaxAudioDurationSeconds = 30;
    public static readonly HashSet<string> AllowedAudioMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mp4", "audio/m4a", "audio/x-m4a", "audio/mpeg", "audio/mp3", "audio/aac",
        "audio/webm", "audio/ogg", "audio/wav", "audio/x-wav", "audio/wave"
    };
    public static string NormalizeMimeType(string? contentType) => (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
    public static bool IsAudio(string? contentType) => AllowedAudioMimeTypes.Contains(NormalizeMimeType(contentType));

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

        contentType = NormalizeMimeType(contentType);
        if (fileLength <= 0) return (false, "Vui lòng chọn tệp tin cần tải lên.");
        if (AllowedAudioMimeTypes.Contains(contentType))
        {
            return fileLength <= MaxAudioSizeBytes ? (true, null)
                : (false, LocalizationHelper.Localize("Bản ghi âm không được vượt quá 5MB."));
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

        return (false, LocalizationHelper.Localize("Định dạng tệp '{0}' không được hỗ trợ. Hệ thống chỉ chấp nhận ảnh, video hoặc âm thanh được hỗ trợ.", contentType));
    }
}
