using EmergencyDispatch.Application.DTOs.Dashboard;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;

namespace EmergencyDispatch.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IIncidentRepository _incidentRepository;
    private readonly IRescueUnitRepository _rescueUnitRepository;
    private readonly IStationRepository _stationRepository;
    private readonly IUserRepository _userRepository;

    public DashboardService(
        IIncidentRepository incidentRepository,
        IRescueUnitRepository rescueUnitRepository,
        IStationRepository stationRepository,
        IUserRepository userRepository)
    {
        _incidentRepository = incidentRepository;
        _rescueUnitRepository = rescueUnitRepository;
        _stationRepository = stationRepository;
        _userRepository = userRepository;
    }

    public async Task<AdminDashboardStatsDto> GetAdminStatsAsync(CancellationToken cancellationToken = default)
    {
        // 1. Thống kê Sự cố
        var allIncidents = (await _incidentRepository.GetAllAsync(i => !i.IsDeleted)).ToList();

        var totalIncidents = allIncidents.Count;
        var activeIncidents = allIncidents.Count(i =>
            i.Status == IncidentStatus.Pending ||
            i.Status == IncidentStatus.AiProcessing ||
            i.Status == IncidentStatus.AiProcessed ||
            i.Status == IncidentStatus.Verified ||
            i.Status == IncidentStatus.Dispatched ||
            i.Status == IncidentStatus.InProgress ||
            i.Status == IncidentStatus.Escalated);

        var pendingIncidents = allIncidents.Count(i =>
            i.Status == IncidentStatus.Pending ||
            i.Status == IncidentStatus.AiProcessing ||
            i.Status == IncidentStatus.AiProcessed);

        var dispatchedIncidents = allIncidents.Count(i =>
            i.Status == IncidentStatus.Dispatched ||
            i.Status == IncidentStatus.InProgress);

        var resolvedIncidents = allIncidents.Count(i => i.Status == IncidentStatus.Completed);
        var cancelledIncidents = allIncidents.Count(i => i.Status == IncidentStatus.Cancelled);
        var escalatedIncidents = allIncidents.Count(i => i.Status == IncidentStatus.Escalated);

        var criticalLevel3 = allIncidents.Count(i => i.Severity == SeverityLevel.Level3);
        var moderateLevel2 = allIncidents.Count(i => i.Severity == SeverityLevel.Level2);
        var minorLevel1 = allIncidents.Count(i => i.Severity == SeverityLevel.Level1);

        // 2. Thống kê Phương tiện cứu hộ
        var allUnits = (await _rescueUnitRepository.GetAllAsync(u => !u.IsDeleted)).ToList();
        var totalRescueUnits = allUnits.Count;
        var availableUnits = allUnits.Count(u => u.Status == RescueUnitStatus.Available);
        var onMissionUnits = allUnits.Count(u =>
            u.Status == RescueUnitStatus.Dispatched ||
            u.Status == RescueUnitStatus.OnScene ||
            u.Status == RescueUnitStatus.Returning);
        var maintenanceUnits = allUnits.Count(u => u.Status == RescueUnitStatus.Maintenance);

        // 3. Thống kê Trạm
        var allStations = (await _stationRepository.GetAllAsync(s => !s.IsDeleted)).ToList();
        var totalStations = allStations.Count;
        var activeStations = allStations.Count(s => s.IsActive);

        // 4. Thống kê Người dùng
        var allUsers = (await _userRepository.GetAllAsync(u => !u.IsDeleted)).ToList();
        var totalUsers = allUsers.Count;
        var totalCitizens = allUsers.Count(u => u.Role == UserRole.Citizen);
        var totalOperators = allUsers.Count(u => u.Role == UserRole.Operator);
        var totalRescueStaff = allUsers.Count(u => u.Role == UserRole.RescueStaff);
        var totalAdmins = allUsers.Count(u => u.Role == UserRole.Admin);

        // 5. KPIs
        var incidentsWithResponseTime = allIncidents
            .Where(i => i.ResponseTimeMs.HasValue && i.ResponseTimeMs.Value > 0)
            .ToList();

        var averageResponseMinutes = incidentsWithResponseTime.Any()
            ? Math.Round(incidentsWithResponseTime.Average(i => i.ResponseTimeMs!.Value) / 60000.0, 1)
            : 0;

        var resolutionRate = totalIncidents > 0
            ? Math.Round((double)resolvedIncidents / totalIncidents * 100.0, 1)
            : 0;

        // 6. Sự cố khẩn cấp gần nhất
        var recentIncidents = allIncidents
            .OrderByDescending(i => i.CreatedAt)
            .Take(8)
            .Select(i => new RecentIncidentSummaryDto
            {
                Id = i.Id,
                Title = i.Title,
                Severity = i.Severity.ToString(),
                Status = i.Status.ToString(),
                LocationAddress = i.LocationAddress,
                CreatedAt = i.CreatedAt
            })
            .ToList();

        return new AdminDashboardStatsDto
        {
            TotalIncidents = totalIncidents,
            ActiveIncidents = activeIncidents,
            PendingIncidents = pendingIncidents,
            DispatchedIncidents = dispatchedIncidents,
            ResolvedIncidents = resolvedIncidents,
            CancelledIncidents = cancelledIncidents,
            EscalatedIncidents = escalatedIncidents,
            CriticalLevel3Incidents = criticalLevel3,
            ModerateLevel2Incidents = moderateLevel2,
            MinorLevel1Incidents = minorLevel1,
            TotalRescueUnits = totalRescueUnits,
            AvailableUnits = availableUnits,
            OnMissionUnits = onMissionUnits,
            MaintenanceUnits = maintenanceUnits,
            TotalStations = totalStations,
            ActiveStations = activeStations,
            TotalUsers = totalUsers,
            TotalCitizens = totalCitizens,
            TotalOperators = totalOperators,
            TotalRescueStaff = totalRescueStaff,
            TotalAdmins = totalAdmins,
            AverageResponseTimeMinutes = averageResponseMinutes,
            IncidentResolutionRatePercent = resolutionRate,
            RecentIncidents = recentIncidents
        };
    }
}
