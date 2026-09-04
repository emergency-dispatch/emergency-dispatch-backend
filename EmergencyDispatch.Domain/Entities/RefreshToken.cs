namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Thực thể lưu trữ Refresh Token để cấp phát lại Access Token
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }

    // Navigation Property
    public User User { get; set; } = null!;

    // Helper
    public bool IsActive => !IsRevoked && !IsDeleted && DateTime.UtcNow < ExpiresAt;
}
