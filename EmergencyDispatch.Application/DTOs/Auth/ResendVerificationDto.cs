using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.Auth;

public class ResendVerificationDto
{
    [Required(ErrorMessage = "Email là bắt buộc")]
    [EmailAddress(ErrorMessage = "Định dạng email không hợp lệ")]
    public string Email { get; set; } = string.Empty;
}
