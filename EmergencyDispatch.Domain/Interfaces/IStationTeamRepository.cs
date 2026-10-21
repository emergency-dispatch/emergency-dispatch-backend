using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IStationTeamRepository
{
    Task<IReadOnlyList<StationTeam>> GetByStationAsync(Guid stationId, CancellationToken cancellationToken = default);
    Task<StationTeam?> GetByIdAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<StationTeamMember?> GetMembershipAsync(Guid staffId, CancellationToken cancellationToken = default);
    Task AddAsync(StationTeam team, CancellationToken cancellationToken = default);
    void RemoveMember(StationTeamMember member);
    void Remove(StationTeam team);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
