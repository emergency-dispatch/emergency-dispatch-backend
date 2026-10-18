using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

public interface ITrafficHotspotRepository : IGenericRepository<TrafficHotspot>
{
    /// <summary>
    /// Lấy danh sách tất cả các điểm nghẽn đang còn hiệu lực (IsActive == true và ExpiresAt > UtcNow)
    /// </summary>
    Task<IReadOnlyList<TrafficHotspot>> GetActiveHotspotsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm kiếm và đánh dấu vô hiệu hóa các điểm nghẽn đã quá hạn tồn tại (TTL)
    /// </summary>
    Task<int> DeactivateExpiredHotspotsAsync(CancellationToken cancellationToken = default);
}
