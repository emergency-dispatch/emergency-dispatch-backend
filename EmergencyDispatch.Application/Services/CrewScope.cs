using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Phạm vi của nhân viên cứu hộ với một xe (SR-02):
/// xe có tổ phụ trách → chỉ thành viên tổ đó; xe chưa gán tổ → mọi nhân viên cứu hộ của trạm có xe.
/// Nhân viên chưa vào tổ chỉ làm được xe chưa gán tổ, nên không thấy nhiệm vụ của các tổ khác trong trạm.
/// </summary>
public static class CrewScope
{
    public static bool CanWorkOn(Guid? staffStationId, Guid? staffTeamId, RescueUnit? unit)
        => unit != null
           && staffStationId.HasValue
           && staffStationId == unit.StationId
           && (unit.StationTeamId == null || unit.StationTeamId == staffTeamId);

    /// <summary>
    /// Người nhận lệnh của một xe trong danh sách nhân sự trạm (đã kèm TeamMembership)
    /// </summary>
    public static IEnumerable<User> CrewOf(IEnumerable<User> stationUsers, Guid? unitTeamId)
        => stationUsers.Where(u => u.Role == UserRole.RescueStaff
                                   && u.Status == UserStatus.Active
                                   && !u.IsDeleted
                                   && (unitTeamId == null || u.TeamMembership?.TeamId == unitTeamId));
}
