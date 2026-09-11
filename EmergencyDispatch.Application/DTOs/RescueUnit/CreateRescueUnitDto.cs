using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.RescueUnit;

public class CreateRescueUnitDto
{
    [Required(ErrorMessage = "Biển số / Mã hiệu xe không được để trống.")]
    [MaxLength(50, ErrorMessage = "Biển số xe tối đa 50 ký tự.")]
    public string PlateNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Loại phương tiện cứu hộ là bắt buộc.")]
    public RescueUnitType UnitType { get; set; } = RescueUnitType.Ambulance;

    [Required(ErrorMessage = "ID trạm trực thuộc là bắt buộc.")]
    public Guid StationId { get; set; }

    public double? InitialLat { get; set; }
    public double? InitialLng { get; set; }
}
