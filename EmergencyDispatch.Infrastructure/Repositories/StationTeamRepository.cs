using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class StationTeamRepository : IStationTeamRepository
{
    private readonly ApplicationDbContext _context;

    public StationTeamRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<StationTeam>> GetByStationAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        return await _context.StationTeams
            .Include(t => t.RescueUnits)
            .Include(t => t.Members).ThenInclude(m => m.Staff)
            .Where(t => t.StationId == stationId)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<StationTeam?> GetByIdAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.StationTeams
            .Include(t => t.RescueUnits)
            .Include(t => t.Members).ThenInclude(m => m.Staff)
            .FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);
    }

    public async Task<IReadOnlyList<TeamDispatchInfo>> GetDispatchInfoAsync(IReadOnlyCollection<Guid> teamIds, CancellationToken cancellationToken = default)
    {
        if (teamIds.Count == 0) return Array.Empty<TeamDispatchInfo>();
        return await _context.StationTeams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .Select(t => new TeamDispatchInfo(
                t.Id,
                t.Name,
                t.Members.Count(m => m.Staff != null && !m.Staff.IsDeleted
                                     && m.Staff.Role == UserRole.RescueStaff && m.Staff.Status == UserStatus.Active)))
            .ToListAsync(cancellationToken);
    }

    public async Task<StationTeamMember?> GetMembershipAsync(Guid staffId, CancellationToken cancellationToken = default)
    {
        return await _context.StationTeamMembers
            .Include(m => m.Team)
            .FirstOrDefaultAsync(m => m.StaffId == staffId, cancellationToken);
    }

    public async Task AddAsync(StationTeam team, CancellationToken cancellationToken = default)
    {
        await _context.StationTeams.AddAsync(team, cancellationToken);
    }

    public void RemoveMember(StationTeamMember member) => _context.StationTeamMembers.Remove(member);

    public void Remove(StationTeam team)
    {
        _context.StationTeamMembers.RemoveRange(team.Members);
        _context.StationTeams.Remove(team);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _context.SaveChangesAsync(cancellationToken);
}
