using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IRescueUnitRepository : IGenericRepository<RescueUnit>
{
    Task<RescueUnit?> GetByIdWithDetailsAsync(Guid id);
    Task<IReadOnlyList<RescueUnit>> GetByStationIdAsync(Guid stationId);
    Task<IReadOnlyList<RescueUnit>> GetAvailableUnitsAsync(RescueUnitType? unitType = null);
    Task<IReadOnlyList<RescueUnit>> GetAllWithStationAsync();
    Task<bool> PlateNumberExistsAsync(string plateNumber, Guid? excludeId = null);

    /// <summary>
    /// Chuyển xe từ Available sang Dispatched một cách nguyên tử; false nếu xe đã bị Operator khác giao trước (MSG32)
    /// </summary>
    Task<bool> TryReserveForDispatchAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trả xe đã giữ chỗ (Dispatched) về Available khi phát lệnh nhiều xe thất bại giữa chừng
    /// </summary>
    Task ReleaseReservationAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trả các xe đã hết thời gian hồi sức (CoolDown) về Available (BR-27); trả về danh sách xe được giải phóng
    /// </summary>
    Task<IReadOnlyList<Guid>> ReleaseExpiredCoolDownsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
