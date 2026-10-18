using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// Yêu cầu điều phối xe cứu hộ tới sự cố
/// </summary>
public class AssignDispatchDto
{
    [Required(ErrorMessage = "Mã sự cố (IncidentId) là bắt buộc.")]
    public Guid IncidentId { get; set; }

    [Required(ErrorMessage = "Mã xe cứu hộ (RescueUnitId) là bắt buộc.")]
    public Guid RescueUnitId { get; set; }

    /// <summary>
    /// Chỉ thị điều phối từ Operator gửi tới đội xe
    /// </summary>
    [MaxLength(1000, ErrorMessage = "Ghi chú điều phối không được vượt quá 1000 ký tự.")]
    public string? Notes { get; set; }
}
