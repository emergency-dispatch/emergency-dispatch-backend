using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.Incident;

public class CancelIncidentDto
{
    [MaxLength(500, ErrorMessage = "Lý do hủy tối đa 500 ký tự")]
    public string? Reason { get; set; } = "Người dân/Khách báo hủy sự cố";
}
