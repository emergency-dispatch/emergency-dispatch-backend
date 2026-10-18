using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IIncidentRepository : IGenericRepository<Incident>
{
    Task<Incident?> GetIncidentWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(List<Incident> Items, int TotalCount)> GetFilteredIncidentsAsync(
        IncidentStatus? status,
        SeverityLevel? severity,
        string? searchTerm,
        DateTime? fromDate,
        DateTime? toDate,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<List<Incident>> GetPendingQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy các sự cố kẹt ở trạng thái AiProcessing mà chưa có kết quả AI (do server restart làm mất hàng đợi in-memory)
    /// </summary>
    Task<List<Incident>> GetStuckAiProcessingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Đánh dấu thêm kết quả AI (chưa lưu, gọi SaveChangesAsync để commit)
    /// </summary>
    Task AddAiClassificationAsync(AiClassification classification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đánh dấu thêm lịch sử chuyển trạng thái sự cố (chưa lưu)
    /// </summary>
    Task AddStatusHistoryAsync(IncidentStatusHistory history, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đánh dấu thêm nhật ký kiểm toán / Telemetry (chưa lưu)
    /// </summary>
    Task AddAuditTrailAsync(IncidentAuditTrail auditTrail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commit toàn bộ thay đổi đang được theo dõi
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
