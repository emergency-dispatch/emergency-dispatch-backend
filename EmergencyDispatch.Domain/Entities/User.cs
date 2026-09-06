using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Thực thể người dùng trong hệ thống (Citizen, Operator, RescueStaff, Admin)
/// </summary>
public class User : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }
    public string? AvatarUrl { get; set; }
    public UserRole Role { get; set; } = UserRole.Citizen;
    public UserStatus Status { get; set; } = UserStatus.Active;

    // Thông tin nhân thân mở rộng
    public DateTime? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? CitizenIdNumber { get; set; }
    public string? Address { get; set; }

    // Hồ sơ y tế cứu hộ khẩn cấp (Emergency Medical Profile)
    public BloodType? BloodType { get; set; }
    public string? MedicalNotes { get; set; }

    // Người liên hệ khẩn cấp (Emergency Contact)
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }

    // Thiết bị & Push Notification
    public string? FcmToken { get; set; }

    // Xác thực Email & Khôi phục mật khẩu
    public bool IsEmailVerified { get; set; } = false;
    public string? EmailVerificationToken { get; set; }
    public DateTime? EmailVerificationTokenExpiry { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }

    // Foreign Keys
    public Guid? StationId { get; set; }

    // Navigation Properties
    public Station? Station { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
