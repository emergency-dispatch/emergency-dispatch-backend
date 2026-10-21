using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Tổ làm việc cố định của trạm cứu hộ (Operator thêm nhân viên vào trạm và tổ). Mỗi xe có tối đa 1 tổ phụ trách.
/// </summary>
public class StationTeam : BaseEntity
{
    public Guid StationId { get; set; }
    public Station? Station { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Xe tổ phụ trách; null = tổ trực tại trạm
    /// </summary>
    public Guid? RescueUnitId { get; set; }
    public RescueUnit? RescueUnit { get; set; }

    public ICollection<StationTeamMember> Members { get; set; } = new List<StationTeamMember>();
}

/// <summary>
/// Thành viên tổ làm việc. Mỗi nhân viên cứu hộ thuộc tối đa 1 tổ.
/// </summary>
public class StationTeamMember : BaseEntity
{
    public Guid TeamId { get; set; }
    public StationTeam? Team { get; set; }

    public Guid StaffId { get; set; }
    public User? Staff { get; set; }

    public CrewRole CrewRole { get; set; } = CrewRole.Rescuer;
}
