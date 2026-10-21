using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IDispatchAssignmentRepository : IGenericRepository<DispatchAssignment>
{
    Task<DispatchAssignment?> GetByIdWithDetailsAsync(Guid id);
    Task<IReadOnlyList<DispatchAssignment>> GetByIncidentIdAsync(Guid incidentId);
    Task<IReadOnlyList<DispatchAssignment>> GetByRescueUnitIdAsync(Guid rescueUnitId);
    Task<DispatchAssignment?> GetActiveAssignmentByUnitIdAsync(Guid rescueUnitId);
    Task<IReadOnlyList<DispatchAssignment>> GetAllActiveAsync();

    /// <summary>
    /// Ghi lịch sử trạng thái lượt phân công (chưa SaveChanges)
    /// </summary>
    Task AddStatusHistoryAsync(AssignmentStatusHistory history);

    /// <summary>
    /// Thêm báo cáo nghiệm thu (chưa SaveChanges)
    /// </summary>
    Task AddCompletionReportAsync(CompletionReport report);
}
