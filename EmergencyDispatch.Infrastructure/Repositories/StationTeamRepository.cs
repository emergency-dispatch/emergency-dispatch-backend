using EmergencyDispatch.Domain.Entities;
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
            .Include(t => t.RescueUnit)
            .Include(t => t.Members).ThenInclude(m => m.Staff)
            .Where(t => t.StationId == stationId)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<StationTeam?> GetByIdAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.StationTeams
            .Include(t => t.RescueUnit)
            .Include(t => t.Members).ThenInclude(m => m.Staff)
            .FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);
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
