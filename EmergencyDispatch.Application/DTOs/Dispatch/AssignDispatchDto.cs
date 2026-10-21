using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// Yêu cầu điều phối một hoặc nhiều xe cứu hộ tới sự cố (FR-OP-04, BR-14)
/// </summary>
public class AssignDispatchDto
{
    [Required(ErrorMessage = "Mã sự cố (IncidentId) là bắt buộc.")]
    public Guid IncidentId { get; set; }

    /// <summary>
    /// Danh sách xe điều động. Level 3 cho phép tối đa 5 xe; Level 1–2 tối đa 1 xe (trừ khi AllowExtraUnits)
    /// </summary>
    public List<Guid>? RescueUnitIds { get; set; }

    /// <summary>
    /// [Tương thích ngược] Điều phối đúng 1 xe. Có thể dùng thay cho RescueUnitIds.
    /// </summary>
    public Guid? RescueUnitId { get; set; }

    /// <summary>
    /// Thuật toán gợi ý Operator đã dùng khi chọn xe (lưu làm dữ liệu thực nghiệm RQ2)
    /// </summary>
    public DispatchAlgorithm? Algorithm { get; set; }

    /// <summary>
    /// Cho phép điều thêm xe vượt giới hạn của Level 1–2 (bắt buộc ghi lý do vào Notes)
    /// </summary>
    public bool AllowExtraUnits { get; set; }

    /// <summary>
    /// Chỉ thị điều phối từ Operator gửi tới đội xe
    /// </summary>
    [MaxLength(1000, ErrorMessage = "Ghi chú điều phối không được vượt quá 1000 ký tự.")]
    public string? Notes { get; set; }

    /// <summary>
    /// Gộp RescueUnitIds và RescueUnitId thành danh sách không trùng lặp
    /// </summary>
    public List<Guid> GetUnitIds()
    {
        var ids = new List<Guid>();
        if (RescueUnitIds != null) ids.AddRange(RescueUnitIds);
        if (RescueUnitId.HasValue) ids.Add(RescueUnitId.Value);
        return ids.Where(id => id != Guid.Empty).Distinct().ToList();
    }
}
