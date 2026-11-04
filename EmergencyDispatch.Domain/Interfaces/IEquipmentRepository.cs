using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

/// <summary>
/// Repository thiết bị cứu hộ của trạm
/// </summary>
public interface IEquipmentRepository : IGenericRepository<Equipment>
{
    /// <summary>
    /// Chi tiết kèm trạm và xe đang trang bị
    /// </summary>
    Task<Equipment?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Danh sách theo bộ lọc (null = không lọc), kèm trạm và xe; sắp xếp theo trạm rồi tên
    /// </summary>
    Task<IReadOnlyList<Equipment>> GetListAsync(
        Guid? stationId,
        Guid? rescueUnitId,
        EquipmentStatus? status,
        EquipmentCategory? category,
        CancellationToken cancellationToken = default);
}
