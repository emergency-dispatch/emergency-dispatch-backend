using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.Auth;

public class VerifyEmailDto
{
    [Required(ErrorMessage = "Email là bắt buộc")]
    [EmailAddress(ErrorMessage = "Định dạng email không hợp lệ")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã xác thực (OTP) là bắt buộc")]
    public string Token { get; set; } = string.Empty;
}
