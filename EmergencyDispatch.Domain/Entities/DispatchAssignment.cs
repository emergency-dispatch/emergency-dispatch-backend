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
    /// Phiên bản dòng dữ liệu (cột hệ thống xmin của PostgreSQL) — chống nghiệm thu / đổi trạng thái hai lần cùng lúc
    /// </summary>
    public uint Version { get; set; }

    /// <summary>
    /// Thời điểm đội xe bắt đầu di chuyển (EnRoute)
    /// </summary>
    public DateTime? EnRouteAt { get; set; }

    /// <summary>
    /// Điều phối viên phát lệnh
    /// </summary>
    public Guid? AssignedByUserId { get; set; }
    public User? AssignedByUser { get; set; }

    /// <summary>
    /// Thuật toán gợi ý đã dùng khi phát lệnh (NearestAvailable / LoadAware) — dữ liệu thực nghiệm RQ2
    /// </summary>
    public string? DispatchAlgorithm { get; set; }

    /// <summary>
    /// Khoảng cách (km) và ETA (phút) tại thời điểm phát lệnh
    /// </summary>
    public double? DistanceKm { get; set; }
    public double? EstimatedArrivalMinutes { get; set; }

    /// <summary>
    /// Lý do và ghi chú khi đội xe từ chối nhận lệnh (BR-22)
    /// </summary>
    public RejectionReason? RejectionReason { get; set; }
    public string? RejectionNotes { get; set; }
    public DateTime? RejectedAt { get; set; }

    public CompletionReport? CompletionReport { get; set; }
    public ICollection<AssignmentStatusHistory> StatusHistories { get; set; } = new List<AssignmentStatusHistory>();

    /// <summary>
    /// Vòng đời tuần tự của lượt phân công (BR-24): Dispatched → Accepted → EnRoute → OnScene → Completed.
    /// Completed chỉ đạt được qua nghiệm thu (resolve); Cancelled chỉ dành cho trung tâm điều phối.
    /// </summary>
    private static readonly IReadOnlyDictionary<DispatchAssignmentStatus, DispatchAssignmentStatus[]> AllowedTransitions =
        new Dictionary<DispatchAssignmentStatus, DispatchAssignmentStatus[]>
        {
            [DispatchAssignmentStatus.Dispatched] = new[] { DispatchAssignmentStatus.Accepted, DispatchAssignmentStatus.Rejected, DispatchAssignmentStatus.Cancelled },
            // Hỏng xe giữa đường vẫn được phép từ chối để trung tâm điều xe thay thế (FR-RS-02, EC-R01)
            [DispatchAssignmentStatus.Accepted] = new[] { DispatchAssignmentStatus.EnRoute, DispatchAssignmentStatus.Rejected, DispatchAssignmentStatus.Cancelled },
            [DispatchAssignmentStatus.EnRoute] = new[] { DispatchAssignmentStatus.OnScene, DispatchAssignmentStatus.Rejected, DispatchAssignmentStatus.Cancelled },
            [DispatchAssignmentStatus.OnScene] = new[] { DispatchAssignmentStatus.Completed, DispatchAssignmentStatus.Cancelled }
        };

    /// <summary>
    /// Kiểm tra có được chuyển từ trạng thái hiện tại sang trạng thái mới hay không
    /// </summary>
    public bool CanTransitionTo(DispatchAssignmentStatus next)
        => AllowedTransitions.TryGetValue(Status, out var allowed) && allowed.Contains(next);

    /// <summary>
    /// Lượt phân công còn đang hoạt động (chưa hoàn tất / chưa hủy)
    /// </summary>
    public bool IsActive => Status is DispatchAssignmentStatus.Dispatched
        or DispatchAssignmentStatus.Accepted
        or DispatchAssignmentStatus.EnRoute
        or DispatchAssignmentStatus.OnScene;

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
