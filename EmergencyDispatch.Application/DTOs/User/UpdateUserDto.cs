using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.User;

public class UpdateUserDto
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc")]
    [MaxLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự")]
    public string FullName { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
    public string? PhoneNumber { get; set; }

    public string? AvatarUrl { get; set; }

    public UserRole? Role { get; set; }

    public UserStatus? Status { get; set; }

    public Guid? StationId { get; set; }
 
    /// <summary>
    /// Tổ làm việc / Kíp trực / Đội cứu hộ (e.g. "Tổ phản ứng nhanh số 1", "Kíp cấp cứu 115")
    /// </summary>
    public string? DependentsNote { get; set; }
}

