using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Team;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Quản lý tổ làm việc cố định của trạm (Operator thêm nhân viên vào trạm và tổ)
/// </summary>
public class StationTeamService : IStationTeamService
{
    public const int MinTeamSize = 2;
    public const int MaxTeamSize = 6;

    private readonly IStationTeamRepository _teamRepository;
    private readonly IStationRepository _stationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRescueUnitRepository _rescueUnitRepository;
    private readonly ISystemAuditService _auditService;
    private readonly ICurrentUserContext _currentUser;

    public StationTeamService(
        IStationTeamRepository teamRepository,
        IStationRepository stationRepository,
        IUserRepository userRepository,
        IRescueUnitRepository rescueUnitRepository,
        ISystemAuditService auditService,
        ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
        _teamRepository = teamRepository;
        _stationRepository = stationRepository;
        _userRepository = userRepository;
        _rescueUnitRepository = rescueUnitRepository;
        _auditService = auditService;
    }

    private static object Snapshot(StationTeam t) => new
    {
        t.Name,
        t.StationId,
        t.RescueUnitId,
        Members = t.Members.Select(m => new { m.StaffId, m.CrewRole }).ToList()
    };

    public async Task<ApiResponseDto<List<StationTeamDto>>> GetTeamsAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        var scopeError = _currentUser.CheckStation(stationId);
        if (scopeError != null)
        {
            return ApiResponseDto<List<StationTeamDto>>.ForbiddenResult(scopeError);
        }

        if (await _stationRepository.GetByIdAsync(stationId) == null)
        {
            return ApiResponseDto<List<StationTeamDto>>.NotFoundResult("Không tìm thấy trạm cứu hộ.");
        }

        var teams = await _teamRepository.GetByStationAsync(stationId, cancellationToken);
        return ApiResponseDto<List<StationTeamDto>>.SuccessResult(teams.Select(Map).ToList());
    }

    public async Task<ApiResponseDto<StationTeamDto>> CreateAsync(Guid stationId, StationTeamInputDto dto, CancellationToken cancellationToken = default)
    {
        var scopeError = _currentUser.CheckStation(stationId);
        if (scopeError != null)
        {
            return ApiResponseDto<StationTeamDto>.ForbiddenResult(scopeError);
        }

        if (await _stationRepository.GetByIdAsync(stationId) == null)
        {
            return ApiResponseDto<StationTeamDto>.NotFoundResult("Không tìm thấy trạm cứu hộ.");
        }

        var error = await ValidateAsync(stationId, null, dto, cancellationToken);
        if (error != null) return error;

        var team = new StationTeam
        {
            StationId = stationId,
            Name = dto.Name.Trim(),
            RescueUnitId = dto.RescueUnitId
        };
        foreach (var m in dto.Members)
        {
            team.Members.Add(new StationTeamMember { StaffId = m.StaffId, CrewRole = m.CrewRole });
        }

        await _teamRepository.AddAsync(team, cancellationToken);
        await _teamRepository.SaveChangesAsync(cancellationToken);

        var saved = await _teamRepository.GetByIdAsync(team.Id, cancellationToken);
        await _auditService.LogAsync(SystemAuditAction.Created, AuditEntityTypes.StationTeam, team.Id, team.Name,
            $"Tạo tổ \"{team.Name}\" ({team.Members.Count} người)", newValue: Snapshot(team), cancellationToken: cancellationToken);
        return ApiResponseDto<StationTeamDto>.SuccessResult(Map(saved ?? team), $"Đã tạo tổ \"{team.Name}\".");
    }

    public async Task<ApiResponseDto<StationTeamDto>> UpdateAsync(Guid stationId, Guid teamId, StationTeamInputDto dto, CancellationToken cancellationToken = default)
    {
        var scopeError = _currentUser.CheckStation(stationId);
        if (scopeError != null)
        {
            return ApiResponseDto<StationTeamDto>.ForbiddenResult(scopeError);
        }

        var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
        if (team == null || team.StationId != stationId)
        {
            return ApiResponseDto<StationTeamDto>.NotFoundResult("Không tìm thấy tổ.");
        }

        var error = await ValidateAsync(stationId, teamId, dto, cancellationToken);
        if (error != null) return error;

        var before = Snapshot(team);
        team.Name = dto.Name.Trim();
        team.RescueUnitId = dto.RescueUnitId;
        team.UpdatedAt = DateTime.UtcNow;

        var desired = dto.Members.ToDictionary(m => m.StaffId, m => m.CrewRole);
        foreach (var existing in team.Members.ToList())
        {
            if (desired.TryGetValue(existing.StaffId, out var role))
            {
                existing.CrewRole = role;
                desired.Remove(existing.StaffId);
            }
            else
            {
                team.Members.Remove(existing);
                _teamRepository.RemoveMember(existing);
            }
        }
        foreach (var (staffId, role) in desired)
        {
            team.Members.Add(new StationTeamMember { TeamId = team.Id, StaffId = staffId, CrewRole = role });
        }

        await _teamRepository.SaveChangesAsync(cancellationToken);

        var saved = await _teamRepository.GetByIdAsync(team.Id, cancellationToken);
        await _auditService.LogAsync(SystemAuditAction.Updated, AuditEntityTypes.StationTeam, team.Id, team.Name,
            $"Cập nhật tổ \"{team.Name}\"", before, Snapshot(saved ?? team), cancellationToken);
        return ApiResponseDto<StationTeamDto>.SuccessResult(Map(saved ?? team), $"Đã cập nhật tổ \"{team.Name}\".");
    }

    public async Task<ApiResponseDto<bool>> DeleteAsync(Guid stationId, Guid teamId, CancellationToken cancellationToken = default)
    {
        var scopeError = _currentUser.CheckStation(stationId);
        if (scopeError != null)
        {
            return ApiResponseDto<bool>.ForbiddenResult(scopeError);
        }

        var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
        if (team == null || team.StationId != stationId)
        {
            return ApiResponseDto<bool>.NotFoundResult("Không tìm thấy tổ.");
        }

        var before = Snapshot(team);
        _teamRepository.Remove(team);
        await _teamRepository.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(SystemAuditAction.Deleted, AuditEntityTypes.StationTeam, team.Id, team.Name,
            $"Xóa tổ \"{team.Name}\"", before, cancellationToken: cancellationToken);
        return ApiResponseDto<bool>.SuccessResult(true, $"Đã xóa tổ \"{team.Name}\".");
    }

    public async Task AssignByNameAsync(Guid staffId, Guid? stationId, string? teamName, CancellationToken cancellationToken = default)
    {
        var membership = await _teamRepository.GetMembershipAsync(staffId, cancellationToken);
        var name = teamName?.Trim();

        // Rời tổ khi đổi trạm, bỏ trạm, hoặc truyền tên rỗng
        if (membership != null && (stationId == null || membership.Team?.StationId != stationId || string.IsNullOrEmpty(name) || membership.Team?.Name != name))
        {
            _teamRepository.RemoveMember(membership);
            await _teamRepository.SaveChangesAsync(cancellationToken);
            membership = null;
        }

        if (stationId == null || string.IsNullOrEmpty(name) || membership != null)
        {
            return;
        }

        var teams = await _teamRepository.GetByStationAsync(stationId.Value, cancellationToken);
        var team = teams.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (team == null)
        {
            team = new StationTeam { StationId = stationId.Value, Name = name };
            await _teamRepository.AddAsync(team, cancellationToken);
        }
        else if (team.Members.Count >= MaxTeamSize)
        {
            throw new InvalidOperationException($"Tổ \"{team.Name}\" đã đủ {MaxTeamSize} người.");
        }

        team.Members.Add(new StationTeamMember { StaffId = staffId, CrewRole = CrewRole.Rescuer });
        await _teamRepository.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Quy tắc giống màn hình quản lý tổ của Web (stationTeamService.ts)
    /// </summary>
    private async Task<ApiResponseDto<StationTeamDto>?> ValidateAsync(Guid stationId, Guid? teamId, StationTeamInputDto dto, CancellationToken cancellationToken)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return ApiResponseDto<StationTeamDto>.FailureResult("Vui lòng nhập tên tổ.");
        }

        var teams = await _teamRepository.GetByStationAsync(stationId, cancellationToken);
        if (teams.Any(t => t.Id != teamId && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return ApiResponseDto<StationTeamDto>.ConflictResult($"Trạm đã có tổ tên \"{name}\".");
        }

        var members = dto.Members ?? new List<StationTeamMemberInputDto>();
        if (members.Select(m => m.StaffId).Distinct().Count() != members.Count)
        {
            return ApiResponseDto<StationTeamDto>.FailureResult("Một nhân viên chỉ được thêm vào tổ một lần.");
        }
        if (members.Count > MaxTeamSize)
        {
            return ApiResponseDto<StationTeamDto>.FailureResult($"Mỗi tổ tối đa {MaxTeamSize} người.");
        }
        if (members.Count(m => m.CrewRole == CrewRole.Commander) > 1)
        {
            return ApiResponseDto<StationTeamDto>.FailureResult("Mỗi tổ chỉ có 1 tổ trưởng (Chỉ huy kíp).");
        }

        foreach (var m in members)
        {
            var staff = await _userRepository.GetByIdAsync(m.StaffId);
            if (staff == null || staff.Role != UserRole.RescueStaff || staff.StationId != stationId)
            {
                return ApiResponseDto<StationTeamDto>.FailureResult("Thành viên phải là nhân viên cứu hộ của trạm này.");
            }
            if (staff.Status != UserStatus.Active)
            {
                return ApiResponseDto<StationTeamDto>.FailureResult($"Tài khoản {staff.FullName} đang bị khóa.");
            }

            var otherTeam = teams.FirstOrDefault(t => t.Id != teamId && t.Members.Any(x => x.StaffId == m.StaffId));
            if (otherTeam != null)
            {
                return ApiResponseDto<StationTeamDto>.ConflictResult($"{staff.FullName} đã thuộc {otherTeam.Name}.");
            }
        }

        if (dto.RescueUnitId.HasValue)
        {
            var unit = await _rescueUnitRepository.GetByIdAsync(dto.RescueUnitId.Value);
            if (unit == null || unit.StationId != stationId)
            {
                return ApiResponseDto<StationTeamDto>.FailureResult("Xe không thuộc trạm này.");
            }

            var otherTeam = teams.FirstOrDefault(t => t.Id != teamId && t.RescueUnitId == dto.RescueUnitId);
            if (otherTeam != null)
            {
                return ApiResponseDto<StationTeamDto>.ConflictResult($"Xe đã có {otherTeam.Name} phụ trách.");
            }
        }

        return null;
    }

    public static StationTeamDto Map(StationTeam team) => new()
    {
        Id = team.Id,
        StationId = team.StationId,
        Name = team.Name,
        RescueUnitId = team.RescueUnitId,
        RescueUnitPlate = team.RescueUnit?.PlateNumber,
        IsUnderstaffed = team.Members.Count < MinTeamSize,
        Members = team.Members
            .OrderBy(m => m.CrewRole)
            .ThenBy(m => m.Staff?.FullName)
            .Select(m => new StationTeamMemberDto
            {
                StaffId = m.StaffId,
                FullName = m.Staff?.FullName ?? string.Empty,
                PhoneNumber = m.Staff?.PhoneNumber,
                CrewRole = m.CrewRole
            }).ToList()
    };
}
