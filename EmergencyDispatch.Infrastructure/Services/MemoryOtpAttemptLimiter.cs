using EmergencyDispatch.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace EmergencyDispatch.Infrastructure.Services;

/// <summary>
/// Bộ đếm nhập sai OTP trong bộ nhớ (một instance API). Mã OTP sống tối đa 15 phút nên bộ đếm cũng hết hạn sau 15 phút.
/// Khi chạm giới hạn, AuthService xóa mã trong DB nên mã vẫn chết dù API khởi động lại.
/// </summary>
public class MemoryOtpAttemptLimiter : IOtpAttemptLimiter
{
    public const int MaxFailedAttempts = 5;
    private static readonly TimeSpan CounterLifetime = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;
    private readonly object _sync = new();

    public MemoryOtpAttemptLimiter(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool RegisterFailure(string purpose, string email)
    {
        var key = Key(purpose, email);
        lock (_sync)
        {
            var count = _cache.TryGetValue(key, out int current) ? current + 1 : 1;
            _cache.Set(key, count, CounterLifetime);
            return count >= MaxFailedAttempts;
        }
    }

    public void Reset(string purpose, string email) => _cache.Remove(Key(purpose, email));

    private static string Key(string purpose, string email) => $"otp-fail:{purpose}:{email.Trim().ToLowerInvariant()}";
}
