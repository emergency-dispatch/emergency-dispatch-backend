using Microsoft.Extensions.Configuration;

namespace EmergencyDispatch.Application.Validators;

/// <summary>
/// URL ảnh/video chỉ được nhận nếu do chính hệ thống upload (POST /api/media/upload).
/// AI service tự tải URL được truyền vào, nên URL tùy ý cho phép dò mạng nội bộ (SSRF) hoặc tốn tiền gọi AI.
/// </summary>
public static class MediaUrlPolicy
{
    // URL mock CloudinaryMediaService trả về khi chưa cấu hình Cloudinary
    private const string MockHost = "images.unsplash.com";

    public static bool IsTrusted(string? url, IConfiguration configuration)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var cloudName = configuration["Cloudinary:CloudName"];
        if (string.IsNullOrWhiteSpace(cloudName) || cloudName.StartsWith("YOUR_"))
        {
            return uri.Host.Equals(MockHost, StringComparison.OrdinalIgnoreCase);
        }

        return uri.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase)
               && uri.AbsolutePath.StartsWith($"/{cloudName}/", StringComparison.Ordinal);
    }
}
