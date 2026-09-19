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

    // Hồ sơ y tế cấp cứu chuyên sâu (Medical ID)
    public List<string> ChronicConditions { get; set; } = new();
    public List<string> Allergies { get; set; } = new();
    public List<string> CurrentMedications { get; set; } = new();
    public List<string> MobilityLimitations { get; set; } = new();

    // Thông tin đặc thù cứu nạn tại hiện trường (Special Info)
    public string? PreferredLanguage { get; set; }
    public string? DependentsNote { get; set; }
    public int? HouseholdMembersCount { get; set; }

    // Cấu hình gửi SMS khẩn cấp
    public bool AutoSendSmsOnSos { get; set; }
    public string? SmsTemplate { get; set; }

    // Người liên hệ khẩn cấp chính
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }

    // Danh bạ người thân khẩn cấp (ICE)
    public List<IceContactDto> IceContacts { get; set; } = new();

    // Trạng thái tài khoản & Trạm
    public bool IsEmailVerified { get; set; }
    public string? FcmToken { get; set; }
    public Guid? StationId { get; set; }
    public string? StationName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
