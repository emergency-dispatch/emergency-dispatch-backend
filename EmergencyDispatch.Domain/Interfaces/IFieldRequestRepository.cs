using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

/// <summary>
/// Repository yêu cầu từ hiện trường (đội cứu hộ gửi về trung tâm điều phối)
/// </summary>
public interface IFieldRequestRepository : IGenericRepository<FieldRequest>
{
    /// <summary>
    /// Chi tiết kèm sự cố, lượt điều phối, xe và người gửi
    /// </summary>
    Task<FieldRequest?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yêu cầu đang mở (Open / Acknowledged); lọc theo trạm phụ trách sự cố nếu có (Operator)
    /// </summary>
    Task<IReadOnlyList<FieldRequest>> GetOpenAsync(Guid? responsibleStationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mọi yêu cầu của một sự cố (mới nhất trước)
    /// </summary>
    Task<IReadOnlyList<FieldRequest>> GetByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yêu cầu còn mở, tạo trước mốc thời gian và chưa từng báo lên Admin
    /// </summary>
    Task<IReadOnlyList<FieldRequest>> GetOverdueAsync(DateTime createdBeforeUtc, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
