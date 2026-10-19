namespace EmergencyDispatch.Application.DTOs.Dashboard;

public class AdminDashboardStatsDto
{
    // Thống kê sự cố
    public int TotalIncidents { get; set; }
    public int ActiveIncidents { get; set; }
    public int PendingIncidents { get; set; }
    public int DispatchedIncidents { get; set; }
    public int ResolvedIncidents { get; set; }
    public int CancelledIncidents { get; set; }
    public int EscalatedIncidents { get; set; }

    // Thống kê theo cấp độ nghiêm trọng
    public int CriticalLevel3Incidents { get; set; }
    public int ModerateLevel2Incidents { get; set; }
    public int MinorLevel1Incidents { get; set; }

    // Thống kê phương tiện cứu hộ
    public int TotalRescueUnits { get; set; }
    public int AvailableUnits { get; set; }
    public int OnMissionUnits { get; set; }
    public int MaintenanceUnits { get; set; }

    // Thống kê hạ tầng trạm
    public int TotalStations { get; set; }
    public int ActiveStations { get; set; }

    // Thống kê người dùng
    public int TotalUsers { get; set; }
    public int TotalCitizens { get; set; }
    public int TotalOperators { get; set; }
    public int TotalRescueStaff { get; set; }
    public int TotalAdmins { get; set; }

    // Chỉ số hiệu năng (KPIs)
    public double AverageResponseTimeMinutes { get; set; }
    public double IncidentResolutionRatePercent { get; set; }

    // Danh sách sự cố khẩn cấp gần đây
    public IReadOnlyList<RecentIncidentSummaryDto> RecentIncidents { get; set; } = new List<RecentIncidentSummaryDto>();
}

public class RecentIncidentSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? LocationAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}
