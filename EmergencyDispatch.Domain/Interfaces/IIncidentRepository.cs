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
        CancellationToken cancellationToken = default,
        Guid? responsibleStationId = null,
        IReadOnlyCollection<IncidentStatus>? statuses = null);

    /// <summary>
    /// Sự cố chưa được Operator xác minh (Pending / AiProcessing / AiProcessed) tạo trước cutoffUtc — để tự hủy sau 24h
    /// </summary>
    Task<List<Guid>> GetStaleUnverifiedIdsAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// responsibleStationId != null → chỉ sự cố của trạm đó và sự cố chưa xác định trạm
    /// </summary>
    Task<List<Incident>> GetPendingQueueAsync(CancellationToken cancellationToken = default, Guid? responsibleStationId = null);

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
    /// Xóa toàn bộ thực thể đang được theo dõi trong ChangeTracker để tránh Identity Map trả về dữ liệu bộ nhớ cũ khi truy vấn lại
    /// </summary>
    void ClearChangeTracker();

    /// <summary>
    /// Giữ (hoặc gia hạn) khóa mềm một cách nguyên tử: chỉ thành công khi chưa ai giữ, khóa đã hết hạn, hoặc chính Operator này đang giữ
    /// </summary>
    Task<bool> TryAcquireLockAsync(Guid incidentId, Guid operatorId, DateTime expiresAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhả khóa mềm do chính Operator này đang giữ (hoặc mọi khóa nếu force = true)
    /// </summary>
    Task<bool> ReleaseLockAsync(Guid incidentId, Guid operatorId, bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi kết quả AI một cách nguyên tử: chỉ cập nhật khi sự cố vẫn đang AiProcessing (Operator chưa xác minh / hủy).
    /// EventType chỉ ghi khi sự cố chưa có loại. Trả về true nếu đã áp dụng.
    /// </summary>
    Task<bool> TryApplyAiResultAsync(Guid incidentId, SeverityLevel severity, IncidentEventType eventType, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc lại giá trị mới nhất từ DB cho sự cố đang được theo dõi (sau khi cập nhật bằng ExecuteUpdate)
    /// </summary>
    Task ReloadAsync(Incident incident, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mở transaction và khóa dòng sự cố tới khi commit / dispose: các thao tác cùng sự cố (VD nhiều xe nghiệm thu) chạy tuần tự.
    /// Gọi trước khi đọc dữ liệu cần kiểm tra để luôn thấy bản mới nhất.
    /// </summary>
    Task<IIncidentLockScope> BeginLockedTransactionAsync(Guid incidentId, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Lịch sử sự cố đã đóng (Completed / Cancelled) cho Admin: lọc theo thời điểm báo cáo, trạm có xe tham gia,
    /// mức độ, từ khóa; mới nhất trước. pageSize = 0 → lấy tất cả (dùng khi xuất báo cáo, có giới hạn maxRows).
    /// </summary>
    Task<(IReadOnlyList<Incident> Items, int TotalCount)> GetClosedHistoryAsync(
        IReadOnlyCollection<IncidentStatus> statuses,
        DateTime? from,
        DateTime? to,
        Guid? stationId,
        SeverityLevel? severity,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
}
