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

    // Hồ sơ y tế cứu hộ khẩn cấp (Emergency Medical Profile)
    public BloodType? BloodType { get; set; }
    public string? MedicalNotes { get; set; }

    // Người liên hệ khẩn cấp (Emergency Contact)
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }
}
