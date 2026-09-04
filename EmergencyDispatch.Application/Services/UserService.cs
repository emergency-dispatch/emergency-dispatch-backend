using BCrypt.Net;
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

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<UserResponseDto> GetByIdAsync(Guid id)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy người dùng với Id = {id}");

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

    public async Task<UserResponseDto> CreateUserAsync(CreateUserDto dto)
    {
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
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);
        return MapToUserResponseDto(user);
    }

    public async Task<UserResponseDto> UpdateUserAsync(Guid id, UpdateUserDto dto)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy người dùng với Id = {id}");

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

    private static UserResponseDto MapToUserResponseDto(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        AvatarUrl = user.AvatarUrl,
        Role = user.Role,
        Status = user.Status,
        StationId = user.StationId,
        StationName = user.Station?.Name,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };
}
