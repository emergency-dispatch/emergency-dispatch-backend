namespace EmergencyDispatch.Application.DTOs.User;

public class TeamMemberSummaryDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? TeamName { get; set; }
}

public class StationVehicleSummaryDto
{
    public Guid Id { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string UnitType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double CurrentLat { get; set; }
    public double CurrentLng { get; set; }
}

/// <summary>
/// DTO phản hồi thông tin Tổ làm việc (Working Group/Squad) và trạm phân công cho Staff
/// </summary>
public class StaffTeamResponseDto
{
    public Guid? StationId { get; set; }
    public string? StationName { get; set; }
    public string? StationAddress { get; set; }
    public string? StationPhoneNumber { get; set; }
    public string? MyTeamName { get; set; } // Tổ làm việc của cán bộ hiện tại
    public Guid? MyTeamId { get; set; }
    public string? MyCrewRole { get; set; }
    public Guid? MyTeamRescueUnitId { get; set; }
    public string? MyTeamRescueUnitPlate { get; set; }

    /// <summary>
    /// Đồng đội cùng tổ (TeamMembers là toàn bộ nhân sự của trạm)
    /// </summary>
    public List<TeamMemberSummaryDto> MyTeamMembers { get; set; } = new();
    public List<TeamMemberSummaryDto> TeamMembers { get; set; } = new();
    public List<StationVehicleSummaryDto> StationVehicles { get; set; } = new();
}

/// <summary>
/// DTO phân công nhân viên vào trạm và tổ làm việc (Dành cho Điều phối viên &amp; Admin)
/// </summary>
public class AssignStaffToStationAndTeamDto
{
    public Guid? StationId { get; set; }
    public string? TeamName { get; set; } // Tổ làm việc (e.g. "Tổ phản ứng nhanh số 1", "Kíp cấp cứu 115")
}
