using EmergencyDispatch.Application.DTOs.Dashboard;

namespace EmergencyDispatch.Application.Interfaces;

public interface IDashboardService
{
    Task<AdminDashboardStatsDto> GetAdminStatsAsync(CancellationToken cancellationToken = default);
}
