using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.User;

public class UserResponseDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; }

    // Thông tin nhân thân
    public DateTime? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? CitizenIdNumber { get; set; }
    public string? Address { get; set; }

    // Hồ sơ y tế cứu hộ khẩn cấp
    public BloodType? BloodType { get; set; }
    public string? MedicalNotes { get; set; }

    // Người liên hệ khẩn cấp
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }

    // Trạng thái tài khoản & Trạm
    public bool IsEmailVerified { get; set; }
    public string? FcmToken { get; set; }
    public Guid? StationId { get; set; }
    public string? StationName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
