using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IStationRepository : IGenericRepository<Station>
{
    Task<Station?> GetByIdWithUnitsAndStaffAsync(Guid id);
    Task<IReadOnlyList<Station>> GetAllActiveAsync();
}
