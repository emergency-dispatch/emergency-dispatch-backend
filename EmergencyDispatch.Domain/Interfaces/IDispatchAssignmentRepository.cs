using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IDispatchAssignmentRepository : IGenericRepository<DispatchAssignment>
{
    Task<DispatchAssignment?> GetByIdWithDetailsAsync(Guid id);
    Task<IReadOnlyList<DispatchAssignment>> GetByIncidentIdAsync(Guid incidentId);
    Task<IReadOnlyList<DispatchAssignment>> GetByRescueUnitIdAsync(Guid rescueUnitId);
    Task<DispatchAssignment?> GetActiveAssignmentByUnitIdAsync(Guid rescueUnitId);
}
