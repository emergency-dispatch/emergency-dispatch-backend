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
    Task<UserResponseDto> CreateUserAsync(CreateUserDto dto);
    Task<UserResponseDto> UpdateUserAsync(Guid id, UpdateUserDto dto);
    Task<bool> DeleteUserAsync(Guid id);
    Task<bool> UpdateFcmTokenAsync(Guid userId, string fcmToken);

    // Quản lý danh bạ người thân khẩn cấp (ICE - In Case of Emergency)
    Task<IReadOnlyList<IceContactDto>> GetIceContactsAsync(Guid userId);
    Task<IceContactDto> CreateIceContactAsync(Guid userId, CreateIceContactDto dto);
    Task<IceContactDto> UpdateIceContactAsync(Guid userId, Guid contactId, UpdateIceContactDto dto);
    Task<bool> DeleteIceContactAsync(Guid userId, Guid contactId);
}
