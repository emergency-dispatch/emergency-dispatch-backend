using Microsoft.Extensions.Configuration;

namespace EmergencyDispatch.Application.Validators;

/// <summary>
/// URL ảnh/video chỉ được nhận nếu do chính hệ thống upload (POST /api/media/upload).
/// AI service tự tải URL được truyền vào, nên URL tùy ý cho phép dò mạng nội bộ (SSRF) hoặc tốn tiền gọi AI.
/// </summary>
public static class MediaUrlPolicy
{
    // URL mock CloudinaryMediaService trả về khi chưa cấu hình Cloudinary
    public const string MockHost = "images.unsplash.com";

    /// <summary>
    /// Đủ CloudName + ApiKey + ApiSecret thật → upload lên Cloudinary; thiếu một trong ba → upload trả URL mock.
    /// CloudinaryMediaService và IsTrusted dùng chung điều kiện này để không nhận / từ chối lệch nhau.
    /// </summary>
    public static bool IsCloudinaryConfigured(IConfiguration configuration)
        => IsReal(configuration["Cloudinary:CloudName"])
           && IsReal(configuration["Cloudinary:ApiKey"])
           && IsReal(configuration["Cloudinary:ApiSecret"]);

    public static bool IsTrusted(string? url, IConfiguration configuration)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // Upload đang chạy chế độ mock (thiếu khóa Cloudinary) → URL mock do chính hệ thống sinh ra là hợp lệ
        if (!IsCloudinaryConfigured(configuration) && uri.Host.Equals(MockHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var cloudName = configuration["Cloudinary:CloudName"]?.Trim();
        if (!IsReal(cloudName))
        {
            return false;
        }

        return uri.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase)
               && uri.AbsolutePath.StartsWith($"/{cloudName}/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReal(string? value)
        => !string.IsNullOrWhiteSpace(value) && !value.Trim().StartsWith("YOUR_", StringComparison.Ordinal);
}
