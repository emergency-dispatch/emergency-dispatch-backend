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
}
