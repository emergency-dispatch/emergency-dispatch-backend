using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// DTO để Điều phối viên (Operator) thẩm định và xác nhận hoặc bác bỏ tình trạng ùn tắc giao thông
/// </summary>
public class UpdateTrafficJamStatusDto
{
    /// <summary>
    /// Trạng thái xác nhận của Điều phối viên: Confirmed (3) hoặc Dismissed (4)
    /// </summary>
    [Required(ErrorMessage = "Trạng thái thẩm định ùn tắc là bắt buộc")]
    public TrafficJamStatus Status { get; set; }

    /// <summary>
    /// Ghi chú nghiệp vụ hoặc lý do xác nhận / bác bỏ (tùy chọn)
    /// </summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }
}
