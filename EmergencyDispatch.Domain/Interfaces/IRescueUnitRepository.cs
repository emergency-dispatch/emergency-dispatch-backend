using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IRescueUnitRepository : IGenericRepository<RescueUnit>
{
    Task<RescueUnit?> GetByIdWithDetailsAsync(Guid id);
    Task<IReadOnlyList<RescueUnit>> GetByStationIdAsync(Guid stationId);
    Task<IReadOnlyList<RescueUnit>> GetAvailableUnitsAsync(string? unitType = null);

    /// <summary>
    /// Khoảng cách đường chim bay (mét) từ một điểm tới các xe trong bán kính maxMeters, tính bằng PostGIS
    /// (ST_DWithin lọc trên chỉ mục GiST, ST_Distance trên geography). null = không hỗ trợ truy vấn không gian
    /// </summary>
    Task<IReadOnlyDictionary<Guid, double>?> GetDistancesMetersAsync(double latitude, double longitude, double maxMeters, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RescueUnit>> GetAllWithStationAsync();
    Task<bool> PlateNumberExistsAsync(string plateNumber, Guid? excludeId = null);

    /// <summary>
    /// Chuyển xe từ Available sang Dispatched một cách nguyên tử; false nếu xe đã bị Operator khác giao trước (MSG32)
    /// </summary>
    Task<bool> TryReserveForDispatchAsync(Guid unitId, CancellationToken cancellationToken = default);
    Task<bool> TryReserveForStationDispatchAsync(Guid unitId, Guid stationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chỉ ghi các cột vị trí (CurrentLat, CurrentLng, LastLocationUpdateAt, UpdatedAt). Không đụng trạng thái xe:
    /// GPS gửi đúng lúc nghiệm thu không được ghi ngược trạng thái cũ đã đọc trước đó
    /// </summary>
    Task SaveLocationAsync(Guid unitId, double latitude, double longitude, DateTime atUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chỉ ghi các cột trạng thái (Status, CoolDownUntil, UpdatedAt) theo giá trị trên đối tượng; không ghi đè vị trí GPS
    /// </summary>
    Task SaveStatusAsync(RescueUnit unit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trả xe đã giữ chỗ (Dispatched) về Available khi phát lệnh nhiều xe thất bại giữa chừng
    /// </summary>
    Task ReleaseReservationAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trả các xe đã hết thời gian hồi sức (CoolDown) về Available (BR-27); trả về danh sách xe được giải phóng
    /// </summary>
    Task<IReadOnlyList<Guid>> ReleaseExpiredCoolDownsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
