using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Bản ghi phân công điều phối xe cứu hộ tới hiện trường sự cố (Junction Entity giữa Incident và RescueUnit)
/// </summary>
public class DispatchAssignment : BaseEntity
{
    /// <summary>
    /// ID Sự cố khẩn cấp cần cứu hộ
    /// </summary>
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    /// <summary>
    /// ID Phương tiện / Đội xe cứu hộ được điều động
    /// </summary>
    public Guid RescueUnitId { get; set; }
    public RescueUnit? RescueUnit { get; set; }

    /// <summary>
    /// Thời điểm trung tâm phát lệnh điều phối xe
    /// </summary>
    public DateTime DispatchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm đội xe xác nhận tiếp nhận lệnh điều phối
    /// </summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>
    /// Thời điểm đội xe thực tế tiếp cận hiện trường (On Scene)
    /// </summary>
    public DateTime? ArrivedAt { get; set; }

    /// <summary>
    /// Thời điểm đội xe hoàn tất cứu hộ tại hiện trường
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Trạng thái của lượt phân công
    /// </summary>
    public DispatchAssignmentStatus Status { get; set; } = DispatchAssignmentStatus.Dispatched;

    /// <summary>
    /// Ghi chú chỉ đạo từ điều phối viên hoặc báo cáo nhanh của đội xe
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Đánh dấu phát lệnh điều phối
    /// </summary>
    public void MarkDispatched()
    {
        Status = DispatchAssignmentStatus.Dispatched;
        DispatchedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Đánh dấu đội xe đã tiếp cận hiện trường
    /// </summary>
    public void MarkArrived()
    {
        Status = DispatchAssignmentStatus.OnScene;
        ArrivedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Đánh dấu đội xe đã hoàn tất xử lý hiện trường
    /// </summary>
    public void MarkCompleted()
    {
        Status = DispatchAssignmentStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
