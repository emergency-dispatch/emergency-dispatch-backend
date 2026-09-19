using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.User;

public class UpdateProfileDto
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc")]
    [MaxLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự")]
    public string FullName { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
    public string? PhoneNumber { get; set; }

    public string? AvatarUrl { get; set; }

    // Thông tin nhân thân mở rộng
    public DateTime? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? CitizenIdNumber { get; set; }
    public string? Address { get; set; }

    // Hồ sơ y tế cứu hộ khẩn cấp cơ bản
    public BloodType? BloodType { get; set; }
    public string? MedicalNotes { get; set; }

    // Hồ sơ y tế cấp cứu chuyên sâu (Medical ID)
    public List<string>? ChronicConditions { get; set; }
    public List<string>? Allergies { get; set; }
    public List<string>? CurrentMedications { get; set; }
    public List<string>? MobilityLimitations { get; set; }

    // Thông tin đặc thù cứu nạn tại hiện trường (Special Info)
    [MaxLength(50, ErrorMessage = "Ngôn ngữ ưu tiên tối đa 50 ký tự")]
    public string? PreferredLanguage { get; set; }

    [MaxLength(1000, ErrorMessage = "Ghi chú người phụ thuộc tối đa 1000 ký tự")]
    public string? DependentsNote { get; set; }

    [Range(0, 100, ErrorMessage = "Số lượng thành viên trong gia đình không hợp lệ")]
    public int? HouseholdMembersCount { get; set; }

    // Cấu hình gửi SMS khẩn cấp
    public bool? AutoSendSmsOnSos { get; set; }

    [MaxLength(500, ErrorMessage = "Mẫu tin nhắn SMS tối đa 500 ký tự")]
    public string? SmsTemplate { get; set; }

    // Người liên hệ khẩn cấp chính (Backward compatibility)
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }
}
