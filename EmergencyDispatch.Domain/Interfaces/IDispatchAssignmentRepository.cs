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

    /// <summary>
    /// Các lượt của sự cố đọc thẳng từ DB (AsNoTracking, kèm báo cáo nghiệm thu) để xét đóng sự cố sau khi một lượt kết thúc
    /// </summary>
    Task<IReadOnlyList<DispatchAssignment>> GetFreshByIncidentIdAsync(Guid incidentId, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Lịch sử báo cáo nghiệm thu (mới nhất trước), kèm sự cố, tổ, người nộp, ảnh, các xe được hoàn tất.
    /// stationId: báo cáo của tổ / xe trạm đó hoặc của sự cố trạm đó phụ trách.
    /// crewTeamId / crewUserId / crewStationId (nhân viên cứu hộ): báo cáo của tổ mình, do chính mình nộp,
    /// hoặc của xe chưa gán tổ thuộc trạm mình (giống phạm vi my-assignments).
    /// </summary>
    Task<(IReadOnlyList<CompletionReport> Items, int TotalCount)> GetCompletionReportsAsync(
        Guid? stationId,
        Guid? teamId,
        Guid? incidentId,
        Guid? crewTeamId,
        Guid? crewUserId,
        Guid? crewStationId,
        DateTime? from,
        DateTime? to,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Một báo cáo nghiệm thu kèm sự cố, tổ, người nộp, ảnh và các xe được hoàn tất
    /// </summary>
    Task<CompletionReport?> GetCompletionReportByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
