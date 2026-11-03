using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

/// <summary>
/// Repository quản lý điểm nghẽn giao thông và chướng ngại vật nội khu
/// </summary>
public interface ITrafficHotspotRepository : IGenericRepository<TrafficHotspot>
{
    /// <summary>
    /// Điểm nghẽn còn trên bản đồ: Active chưa hết hạn hoặc Assigned (kèm trạm và xe được giao)
    /// </summary>
    Task<IReadOnlyList<TrafficHotspot>> GetActiveHotspotsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Chi tiết điểm nghẽn kèm người báo, trạm phụ trách và xe được giao
    /// </summary>
    Task<TrafficHotspot?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xe đang được giao xử lý một điểm nghẽn khác (trạng thái Assigned)
    /// </summary>
    Task<bool> IsUnitOnHotspotAsync(Guid rescueUnitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuyển Active → Assigned có điều kiện (atomic). false nếu điểm nghẽn vừa được người khác giao / giải tỏa / hết hạn
    /// </summary>
    Task<bool> TryMarkAssignedAsync(Guid id, Guid rescueUnitId, Guid assignedByUserId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuyển Expired các điểm nghẽn Active đã quá hạn TTL; điểm nghẽn đang có đội xử lý (Assigned) không hết hạn
    /// </summary>
    Task<int> DeactivateExpiredHotspotsAsync(CancellationToken cancellationToken = default);
}
