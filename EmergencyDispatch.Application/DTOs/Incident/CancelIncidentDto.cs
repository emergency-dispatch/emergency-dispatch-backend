using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

public class CancelIncidentDto
{
    [MaxLength(2048, ErrorMessage = "Mã hủy tối đa 2048 ký tự")]
    public string? CancellationToken { get; set; }

    [MaxLength(500, ErrorMessage = "Lý do hủy tối đa 500 ký tự")]
    public string? Reason { get; set; }

    /// <summary>
    /// Phân loại lý do (Operator / Admin): FalseReport, Duplicate, TestReport, Other. Citizen tự hủy luôn là CitizenCancelled.
    /// </summary>
    public CancellationReason? ReasonCode { get; set; }
}
