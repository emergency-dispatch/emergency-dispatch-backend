using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.RescueUnit;

public class UpdateRescueUnitLocationDto
{
    [Required(ErrorMessage = "Vĩ độ (Latitude) là bắt buộc.")]
    [Range(-90.0, 90.0, ErrorMessage = "Vĩ độ phải nằm trong khoảng -90 đến 90.")]
    public double Latitude { get; set; }

    [Required(ErrorMessage = "Kinh độ (Longitude) là bắt buộc.")]
    [Range(-180.0, 180.0, ErrorMessage = "Kinh độ phải nằm trong khoảng -180 đến 180.")]
    public double Longitude { get; set; }
}
