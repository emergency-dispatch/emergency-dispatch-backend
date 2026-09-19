using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IIceContactRepository : IGenericRepository<IceContact>
{
    Task<IReadOnlyList<IceContact>> GetByUserIdAsync(Guid userId);
}
