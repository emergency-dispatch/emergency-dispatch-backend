using EmergencyDispatch.Application.DTOs.RescueUnit;

namespace EmergencyDispatch.Application.DTOs.Station;

public class StationResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }
    public double CoverageRadiusKm { get; set; }
    public Guid? HeadOperatorId { get; set; }
    public string? HeadOperatorName { get; set; }
    public string? HeadOperatorPhone { get; set; }
    public int ActiveUnitsCount { get; set; }
    public int TotalUnitsCount { get; set; }
    public int StaffCount { get; set; }
    public IReadOnlyList<RescueUnitResponseDto> RescueUnits { get; set; } = new List<RescueUnitResponseDto>();
    public IReadOnlyList<StationStaffMemberDto> StaffMembers { get; set; } = new List<StationStaffMemberDto>();
    public DateTime CreatedAt { get; set; }
}

public class StationStaffMemberDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? TeamName { get; set; } // Tổ làm việc
}
