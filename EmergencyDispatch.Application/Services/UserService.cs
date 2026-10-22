using BCrypt.Net;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.User;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;

namespace EmergencyDispatch.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IStationRepository _stationRepository;
    private readonly IRescueUnitRepository _rescueUnitRepository;
    private readonly IStationTeamService _teamService;

    public UserService(
        IUserRepository userRepository,
        IStationRepository stationRepository,
        IRescueUnitRepository rescueUnitRepository,
        IStationTeamService teamService)
    {
        _userRepository = userRepository;
        _stationRepository = stationRepository;
        _rescueUnitRepository = rescueUnitRepository;
        _teamService = teamService;
    }

    private static string? TeamNameOf(User u) => u.TeamMembership?.Team?.Name;

    public async Task<UserResponseDto> GetByIdAsync(Guid id)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(id)
            ?? throw new KeyNotFoundException(LocalizationHelper.Localize("Không tìm thấy người dùng với Id = {0}", id));

        return MapToUserResponseDto(user);
    }

    public async Task<UserResponseDto> GetProfileAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(userId)
            ?? throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản.");

        return MapToUserResponseDto(user);
    }

    public async Task<UserResponseDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(userId)
            ?? throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản.");

        user.FullName = dto.FullName.Trim();
        user.PhoneNumber = dto.PhoneNumber?.Trim();
        if (!string.IsNullOrEmpty(dto.AvatarUrl))
        {
            user.AvatarUrl = dto.AvatarUrl;
        }

        user.DateOfBirth = dto.DateOfBirth;
        user.Gender = dto.Gender;
        user.CitizenIdNumber = dto.CitizenIdNumber?.Trim();
        user.Address = dto.Address?.Trim();
        user.BloodType = dto.BloodType;
        user.MedicalNotes = dto.MedicalNotes?.Trim();

        // Medical ID chuyên sâu
        if (dto.ChronicConditions != null) user.ChronicConditions = dto.ChronicConditions;
        if (dto.Allergies != null) user.Allergies = dto.Allergies;
        if (dto.CurrentMedications != null) user.CurrentMedications = dto.CurrentMedications;
        if (dto.MobilityLimitations != null) user.MobilityLimitations = dto.MobilityLimitations;

        // Special Info
        if (dto.HasPreferredLanguage) user.PreferredLanguage = dto.PreferredLanguage?.Trim();
        if (dto.HasDependentsNote) user.DependentsNote = dto.DependentsNote?.Trim();
        if (dto.HasHouseholdMembersCount) user.HouseholdMembersCount = dto.HouseholdMembersCount;

        // Cập nhật người liên hệ khẩn cấp duy nhất
        user.EmergencyContactName = dto.EmergencyContactName?.Trim();
        user.EmergencyContactPhone = dto.EmergencyContactPhone?.Trim();
        user.EmergencyContactRelationship = dto.EmergencyContactRelationship?.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);
        return MapToUserResponseDto(user);
    }

    public async Task<PaginatedResultDto<UserResponseDto>> GetAllAsync(
        int pageIndex,
        int pageSize,
        string? search,
        UserRole? role,
        UserStatus? status)
    {
        var (users, totalCount) = await _userRepository.GetUsersPagedAsync(pageIndex, pageSize, search, role, status);
        var dtos = users.Select(MapToUserResponseDto);

        return new PaginatedResultDto<UserResponseDto>(dtos, totalCount, pageIndex, pageSize);
    }

    public async Task<UserResponseDto> CreateUserAsync(CreateUserDto dto, UserRole actorRole)
    {
        EnsureCanManageRole(actorRole, dto.Role);

        if (await _userRepository.EmailExistsAsync(dto.Email.Trim()))
        {
            throw new InvalidOperationException("Email này đã tồn tại trong hệ thống.");
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            PhoneNumber = dto.PhoneNumber?.Trim(),
            PasswordHash = passwordHash,
            Role = dto.Role,
            Status = UserStatus.Active,
            StationId = dto.StationId,
            DependentsNote = dto.DependentsNote?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);
        return MapToUserResponseDto(user);
    }

    public async Task<UserResponseDto> UpdateUserAsync(Guid id, UpdateUserDto dto, UserRole actorRole)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(id)
            ?? throw new KeyNotFoundException(LocalizationHelper.Localize("Không tìm thấy người dùng với Id = {0}", id));

        EnsureCanManageRole(actorRole, user.Role);
        if (dto.Role.HasValue)
        {
            EnsureCanManageRole(actorRole, dto.Role.Value);
        }

        user.FullName = dto.FullName.Trim();
        user.PhoneNumber = dto.PhoneNumber?.Trim();
        if (!string.IsNullOrEmpty(dto.AvatarUrl))
        {
            user.AvatarUrl = dto.AvatarUrl;
        }
        if (dto.Role.HasValue)
        {
            user.Role = dto.Role.Value;
        }
        if (dto.Status.HasValue)
        {
            user.Status = dto.Status.Value;
        }
        user.StationId = dto.StationId;
        if (dto.DependentsNote != null)
        {
            user.DependentsNote = dto.DependentsNote.Trim();
        }
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);
        return MapToUserResponseDto(user);
    }

    public async Task<UserResponseDto> LockUserAsync(Guid id, string reason, Guid actorUserId, UserRole actorRole)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy người dùng với Id = {id}");

        EnsureCanManageRole(actorRole, user.Role);
        if (user.Id == actorUserId)
        {
            throw new InvalidOperationException("Bạn không thể tự khóa tài khoản của chính mình.");
        }
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
        {
            throw new InvalidOperationException("Lý do khóa tài khoản phải có ít nhất 5 ký tự.");
        }

        user.Status = UserStatus.Suspended;
        user.LockReason = reason.Trim();
        user.LockedAt = DateTime.UtcNow;
        user.LockedByUserId = actorUserId;
        user.UpdatedAt = DateTime.UtcNow;

        // Thu hồi mọi phiên đăng nhập đang hoạt động
        foreach (var token in user.RefreshTokens.Where(t => !t.IsRevoked))
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
        }

        await _userRepository.UpdateAsync(user);
        return MapToUserResponseDto(user);
    }

    public async Task<UserResponseDto> UnlockUserAsync(Guid id, UserRole actorRole)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy người dùng với Id = {id}");

        EnsureCanManageRole(actorRole, user.Role);

        user.Status = UserStatus.Active;
        user.LockReason = null;
        user.LockedAt = null;
        user.LockedByUserId = null;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);
        return MapToUserResponseDto(user);
    }

    public async Task<bool> DeleteUserAsync(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
        {
            return false;
        }

        await _userRepository.DeleteAsync(user);
        return true;
    }

    public async Task<bool> UpdateFcmTokenAsync(Guid userId, string fcmToken)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            return false;
        }

        user.FcmToken = fcmToken.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);
        return true;
    }

    public async Task<UserResponseDto> AssignStaffToStationAndTeamAsync(Guid userId, AssignStaffToStationAndTeamDto dto, UserRole actorRole)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(userId)
            ?? throw new KeyNotFoundException(LocalizationHelper.Localize("Không tìm thấy người dùng với Id = {0}", userId));

        EnsureCanManageRole(actorRole, user.Role);

        if (dto.StationId.HasValue)
        {
            var station = await _stationRepository.GetByIdAsync(dto.StationId.Value)
                ?? throw new KeyNotFoundException(LocalizationHelper.Localize("Không tìm thấy trạm cứu hộ với Id = {0}", dto.StationId.Value));
            user.StationId = dto.StationId;
        }
        else
        {
            user.StationId = null;
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        // Tổ làm việc lưu ở bảng StationTeams (trước đây lưu tạm vào DependentsNote)
        if (dto.TeamName != null || !dto.StationId.HasValue)
        {
            await _teamService.AssignByNameAsync(user.Id, user.StationId, dto.TeamName);
        }
        else if (user.TeamMembership?.Team != null && user.TeamMembership.Team.StationId != user.StationId)
        {
            await _teamService.AssignByNameAsync(user.Id, user.StationId, null);
        }

        var updatedUser = await _userRepository.GetByIdWithDetailsAsync(userId);
        return MapToUserResponseDto(updatedUser ?? user);
    }

    public async Task<IReadOnlyList<TeamMemberSummaryDto>> GetStaffByStationAsync(Guid stationId)
    {
        var station = await _stationRepository.GetByIdAsync(stationId)
            ?? throw new KeyNotFoundException(LocalizationHelper.Localize("Không tìm thấy trạm cứu hộ với Id = {0}", stationId));

        var staffList = await _userRepository.GetByStationWithTeamAsync(stationId);

        return staffList
            .OrderBy(u => TeamNameOf(u))
            .ThenBy(u => u.FullName)
            .Select(u => new TeamMemberSummaryDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                AvatarUrl = u.AvatarUrl,
                Role = u.Role.ToString(),
                Status = u.Status.ToString(),
                TeamName = TeamNameOf(u)
            })
            .ToList();
    }

    public async Task<StaffTeamResponseDto> GetMyTeamAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(userId)
            ?? throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản.");

        if (!user.StationId.HasValue)
        {
            return new StaffTeamResponseDto
            {
                MyTeamName = TeamNameOf(user),
                TeamMembers = new List<TeamMemberSummaryDto>
                {
                    new()
                    {
                        Id = user.Id,
                        FullName = user.FullName,
                        Email = user.Email,
                        PhoneNumber = user.PhoneNumber,
                        AvatarUrl = user.AvatarUrl,
                        Role = user.Role.ToString(),
                        Status = user.Status.ToString(),
                        TeamName = TeamNameOf(user)
                    }
                }
            };
        }

        var station = await _stationRepository.GetByIdWithUnitsAndStaffAsync(user.StationId.Value);
        if (station == null)
        {
            return new StaffTeamResponseDto { MyTeamName = TeamNameOf(user) };
        }

        var staffMembers = station.StaffMembers?
            .Where(s => !s.IsDeleted)
            .OrderBy(s => TeamNameOf(s))
            .ThenBy(s => s.FullName)
            .Select(s => new TeamMemberSummaryDto
            {
                Id = s.Id,
                FullName = s.FullName,
                Email = s.Email,
                PhoneNumber = s.PhoneNumber,
                AvatarUrl = s.AvatarUrl,
                Role = s.Role.ToString(),
                Status = s.Status.ToString(),
                TeamName = TeamNameOf(s)
            }).ToList() ?? new List<TeamMemberSummaryDto>();

        var vehicles = station.RescueUnits?
            .Select(v => new StationVehicleSummaryDto
            {
                Id = v.Id,
                PlateNumber = v.PlateNumber,
                UnitType = v.UnitType.ToString(),
                Status = v.Status.ToString(),
                CurrentLat = v.CurrentLat,
                CurrentLng = v.CurrentLng
            }).ToList() ?? new List<StationVehicleSummaryDto>();

        return new StaffTeamResponseDto
        {
            StationId = station.Id,
            StationName = station.Name,
            StationAddress = station.Address,
            StationPhoneNumber = station.PhoneNumber,
            MyTeamName = TeamNameOf(user),
            MyTeamId = user.TeamMembership?.TeamId,
            MyCrewRole = user.TeamMembership?.CrewRole.ToString(),
            MyTeamRescueUnitId = user.TeamMembership?.Team?.RescueUnitId,
            MyTeamRescueUnitPlate = user.TeamMembership?.Team?.RescueUnit?.PlateNumber,
            MyTeamMembers = user.TeamMembership?.Team == null
                ? new List<TeamMemberSummaryDto>()
                : staffMembers.Where(m => m.TeamName == TeamNameOf(user)).ToList(),
            TeamMembers = staffMembers,
            StationVehicles = vehicles
        };
    }

    /// <summary>
    /// Admin quản lý mọi vai trò; Operator chỉ được tạo/sửa/gán trạm cho RescueStaff
    /// </summary>
    private static void EnsureCanManageRole(UserRole actorRole, UserRole targetRole)
    {
        if (actorRole == UserRole.Admin) return;

        if (actorRole == UserRole.Operator && targetRole == UserRole.RescueStaff) return;

        throw new UnauthorizedAccessException("Điều phối viên chỉ được quản lý tài khoản nhân viên cứu hộ (RescueStaff).");
    }

    private static UserResponseDto MapToUserResponseDto(User user) => new()
    {
        LockReason = user.LockReason,
        LockedAt = user.LockedAt,
        LockoutEndUtc = user.LockoutEndUtc > DateTime.UtcNow ? user.LockoutEndUtc : null,
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        AvatarUrl = user.AvatarUrl,
        Role = user.Role,
        Status = user.Status,
        DateOfBirth = user.DateOfBirth,
        Gender = user.Gender,
        CitizenIdNumber = user.CitizenIdNumber,
        Address = user.Address,
        BloodType = user.BloodType,
        MedicalNotes = user.MedicalNotes,

        // Medical ID chuyên sâu
        ChronicConditions = user.ChronicConditions ?? new List<string>(),
        Allergies = user.Allergies ?? new List<string>(),
        CurrentMedications = user.CurrentMedications ?? new List<string>(),
        MobilityLimitations = user.MobilityLimitations ?? new List<string>(),

        // Special Info
        PreferredLanguage = user.PreferredLanguage,
        DependentsNote = user.DependentsNote,
        HouseholdMembersCount = user.HouseholdMembersCount,

        // Người liên hệ khẩn cấp chính
        EmergencyContactName = user.EmergencyContactName,
        EmergencyContactPhone = user.EmergencyContactPhone,
        EmergencyContactRelationship = user.EmergencyContactRelationship,

        IsEmailVerified = user.IsEmailVerified,
        FcmToken = user.FcmToken,
        StationId = user.StationId,
        StationName = user.Station?.Name,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };

}
