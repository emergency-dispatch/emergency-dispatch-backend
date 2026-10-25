using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.User;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Interfaces;

public interface IUserService
{
    Task<UserResponseDto> GetByIdAsync(Guid id);
    Task<UserResponseDto> GetProfileAsync(Guid userId);
    Task<UserResponseDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto);
    Task<PaginatedResultDto<UserResponseDto>> GetAllAsync(
        int pageIndex,
        int pageSize,
        string? search,
        UserRole? role,
        UserStatus? status);
    Task<UserResponseDto> CreateUserAsync(CreateUserDto dto, UserRole actorRole);
    Task<UserResponseDto> UpdateUserAsync(Guid id, UpdateUserDto dto, UserRole actorRole);
    Task<bool> DeleteUserAsync(Guid id);
    Task<UserResponseDto> LockUserAsync(Guid id, string reason, Guid actorUserId, UserRole actorRole);
    Task<UserResponseDto> UnlockUserAsync(Guid id, UserRole actorRole);
    Task<bool> UpdateFcmTokenAsync(Guid userId, string fcmToken);

    // Quản lý điều phối nhân sự, gán trạm & Tổ làm việc (Staff & Operator & Admin)
    Task<UserResponseDto> AssignStaffToStationAndTeamAsync(Guid userId, AssignStaffToStationAndTeamDto dto, UserRole actorRole);
    Task<IReadOnlyList<TeamMemberSummaryDto>> GetStaffByStationAsync(Guid stationId);
    Task<StaffTeamResponseDto> GetMyTeamAsync(Guid userId);
}
