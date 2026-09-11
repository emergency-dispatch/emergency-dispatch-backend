using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.Station;

public class CreateStationDto
{
    [Required(ErrorMessage = "Tên trạm cứu hộ không được để trống.")]
    [MaxLength(200, ErrorMessage = "Tên trạm tối đa 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ trạm không được để trống.")]
    [MaxLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vĩ độ (Latitude) là bắt buộc.")]
    [Range(-90.0, 90.0, ErrorMessage = "Vĩ độ phải nằm trong khoảng -90 đến 90.")]
    public double Latitude { get; set; }

    [Required(ErrorMessage = "Kinh độ (Longitude) là bắt buộc.")]
    [Range(-180.0, 180.0, ErrorMessage = "Kinh độ phải nằm trong khoảng -180 đến 180.")]
    public double Longitude { get; set; }

    [Phone(ErrorMessage = "Số điện thoại không đúng định dạng.")]
    [MaxLength(20, ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;
}
