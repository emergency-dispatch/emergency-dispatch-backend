using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.RescueUnit;

public class UpdateRescueUnitStatusDto
{
    [Required(ErrorMessage = "Trạng thái hoạt động mới là bắt buộc.")]
    public RescueUnitStatus Status { get; set; }
}
