using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IDispatchAssignmentRepository : IGenericRepository<DispatchAssignment>
{
    Task<DispatchAssignment?> GetByIdWithDetailsAsync(Guid id);

    /// <summary>
    /// Chỉ lấy IncidentId của lượt phân công (không theo dõi entity) — dùng để khóa sự cố trước khi đọc chi tiết
    /// </summary>
    Task<Guid?> GetIncidentIdAsync(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DispatchAssignment>> GetByIncidentIdAsync(Guid incidentId);
    Task<IReadOnlyList<DispatchAssignment>> GetByRescueUnitIdAsync(Guid rescueUnitId);
    Task<DispatchAssignment?> GetActiveAssignmentByUnitIdAsync(Guid rescueUnitId);
    Task<IReadOnlyList<DispatchAssignment>> GetAllActiveAsync();

    /// <summary>
    /// Lượt phân công đang chạy kèm sự cố + xe + trạm. stationId != null → lượt của sự cố trạm đó phụ trách hoặc xe trạm đó
    /// </summary>
    Task<IReadOnlyList<DispatchAssignment>> GetActiveWithDetailsAsync(Guid? stationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi lịch sử trạng thái lượt phân công (chưa SaveChanges)
    /// </summary>
    Task AddStatusHistoryAsync(AssignmentStatusHistory history);

    /// <summary>
    /// Thêm báo cáo nghiệm thu (chưa SaveChanges)
    /// </summary>
    Task AddCompletionReportAsync(CompletionReport report);
}
