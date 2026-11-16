using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

/// <summary>
/// Thông tin tổ dùng khi điều phối: số nhân viên cứu hộ đang hoạt động (tổ không có staff không được phân công)
/// </summary>
public record TeamDispatchInfo(Guid TeamId, string Name, int ActiveStaffCount);

public interface IStationTeamRepository
{
    Task<IReadOnlyList<StationTeam>> GetByStationAsync(Guid stationId, CancellationToken cancellationToken = default);
    Task<StationTeam?> GetByIdAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamDispatchInfo>> GetDispatchInfoAsync(IReadOnlyCollection<Guid> teamIds, CancellationToken cancellationToken = default);
    Task<StationTeamMember?> GetMembershipAsync(Guid staffId, CancellationToken cancellationToken = default);
    Task AddAsync(StationTeam team, CancellationToken cancellationToken = default);
    void RemoveMember(StationTeamMember member);
    void Remove(StationTeam team);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
