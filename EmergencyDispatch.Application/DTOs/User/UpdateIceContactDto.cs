using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.User;

public class UpdateIceContactDto
{
    [Required(ErrorMessage = "Tên người thân liên hệ là bắt buộc")]
    [MaxLength(100, ErrorMessage = "Tên tối đa 100 ký tự")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại là bắt buộc")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
    [MaxLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mối quan hệ là bắt buộc")]
    [MaxLength(50, ErrorMessage = "Mối quan hệ tối đa 50 ký tự")]
    public string Relationship { get; set; } = string.Empty;

    public bool IsPrimary { get; set; } = false;
}
