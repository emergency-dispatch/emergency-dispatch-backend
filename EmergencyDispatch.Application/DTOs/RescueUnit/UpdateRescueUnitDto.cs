using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.RescueUnit;

public class UpdateRescueUnitDto
{
    [Required(ErrorMessage = "Biển số / Mã hiệu xe không được để trống.")]
    [MaxLength(50, ErrorMessage = "Biển số xe tối đa 50 ký tự.")]
    public string PlateNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Loại phương tiện cứu hộ là bắt buộc.")]
    public RescueUnitType UnitType { get; set; }

    [Required(ErrorMessage = "Trạng thái xe là bắt buộc.")]
    public RescueUnitStatus Status { get; set; }

    [Required(ErrorMessage = "ID trạm trực thuộc là bắt buộc.")]
    public Guid StationId { get; set; }
}
