using System.Net;

namespace EmergencyDispatch.API.Services;

/// <summary>
/// Quyết định origin nào được gọi API kèm credentials (cookie / JWT qua SignalR).
/// - Có cấu hình "Cors:AllowedOrigins" → chỉ chấp nhận đúng danh sách đó.
/// - Chưa cấu hình + môi trường Development → chấp nhận localhost và IP mạng LAN (test web trên điện thoại cùng Wi-Fi).
/// - Chưa cấu hình + môi trường khác → chặn mọi origin.
/// App mobile native không gửi header Origin nên không bị ảnh hưởng.
/// </summary>
public sealed class CorsOriginPolicy
{
    private readonly HashSet<string> _allowedOrigins;
    private readonly bool _allowLocalNetwork;

    public CorsOriginPolicy(IConfiguration configuration, IHostEnvironment environment)
    {
        _allowedOrigins = (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim().TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _allowLocalNetwork = _allowedOrigins.Count == 0 && environment.IsDevelopment();
    }

    public bool HasExplicitOrigins => _allowedOrigins.Count > 0;

    public bool AllowsLocalNetwork => _allowLocalNetwork;

    public bool IsAllowed(string origin)
    {
        if (_allowedOrigins.Contains(origin.TrimEnd('/')))
        {
            return true;
        }

        if (!_allowLocalNetwork || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.IsLoopback)
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var ip) && IsPrivateIPv4(ip);
    }

    private static bool IsPrivateIPv4(IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var b = ip.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168);
    }
}
