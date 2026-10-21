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

    /// <summary>
    /// Giữ (hoặc gia hạn) khóa mềm một cách nguyên tử: chỉ thành công khi chưa ai giữ, khóa đã hết hạn, hoặc chính Operator này đang giữ
    /// </summary>
    Task<bool> TryAcquireLockAsync(Guid incidentId, Guid operatorId, DateTime expiresAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhả khóa mềm do chính Operator này đang giữ (hoặc mọi khóa nếu force = true)
    /// </summary>
    Task<bool> ReleaseLockAsync(Guid incidentId, Guid operatorId, bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-CIT-01: người dùng / số điện thoại đã có sự cố đang hoạt động được tạo sau thời điểm since
    /// </summary>
    Task<bool> HasRecentActiveIncidentAsync(Guid? userId, string? normalizedPhone, DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-CIT-05: sự cố do người dùng báo cáo (mới nhất trước), có thể lọc theo trạng thái
    /// </summary>
    Task<(IReadOnlyList<Incident> Items, int TotalCount)> GetByReporterAsync(Guid userId, IncidentStatus? status, int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhật ký kiểm toán theo bộ lọc (phân trang, mới nhất trước)
    /// </summary>
    Task<(IReadOnlyList<IncidentAuditTrail> Items, int TotalCount)> GetAuditTrailsAsync(
        Guid? incidentId, Guid? actorId, AuditAction? action, DateTime? from, DateTime? to,
        int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sự cố kèm kết quả AI và phân công trong khoảng thời gian (phục vụ số liệu RQ2/RQ3)
    /// </summary>
    Task<IReadOnlyList<Incident>> GetForTelemetryAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
}
