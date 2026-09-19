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
    private readonly IIceContactRepository _iceContactRepository;

    public UserService(IUserRepository userRepository, IIceContactRepository iceContactRepository)
    {
        _userRepository = userRepository;
        _iceContactRepository = iceContactRepository;
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
        user.PreferredLanguage = dto.PreferredLanguage?.Trim();
        user.DependentsNote = dto.DependentsNote?.Trim();
        user.HouseholdMembersCount = dto.HouseholdMembersCount;

        // SMS Config
        if (dto.AutoSendSmsOnSos.HasValue) user.AutoSendSmsOnSos = dto.AutoSendSmsOnSos.Value;
        user.SmsTemplate = dto.SmsTemplate?.Trim();

        // Đồng bộ người liên hệ khẩn cấp chính
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

    // ==========================================
    // ICE CONTACTS (DANH BẠ NGƯỜI THÂN KHẨN CẤP)
    // ==========================================
    public async Task<IReadOnlyList<IceContactDto>> GetIceContactsAsync(Guid userId)
    {
        var contacts = await _iceContactRepository.GetByUserIdAsync(userId);
        return contacts.Select(MapToIceContactDto).ToList();
    }

    public async Task<IceContactDto> CreateIceContactAsync(Guid userId, CreateIceContactDto dto)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản.");

        var existingContacts = await _iceContactRepository.GetByUserIdAsync(userId);

        // Nếu là liên hệ đầu tiên hoặc được đánh dấu IsPrimary, gán IsPrimary = true
        var isPrimary = dto.IsPrimary || existingContacts.Count == 0;

        if (isPrimary && existingContacts.Count > 0)
        {
            foreach (var p in existingContacts.Where(c => c.IsPrimary))
            {
                p.IsPrimary = false;
                p.UpdatedAt = DateTime.UtcNow;
                await _iceContactRepository.UpdateAsync(p);
            }
        }

        var contact = new IceContact
        {
            UserId = userId,
            Name = dto.Name.Trim(),
            PhoneNumber = dto.PhoneNumber.Trim(),
            Relationship = dto.Relationship.Trim(),
            IsPrimary = isPrimary,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _iceContactRepository.AddAsync(contact);

        // Đồng bộ liên hệ chính vào bảng User
        if (isPrimary)
        {
            user.EmergencyContactName = contact.Name;
            user.EmergencyContactPhone = contact.PhoneNumber;
            user.EmergencyContactRelationship = contact.Relationship;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateAsync(user);
        }

        return MapToIceContactDto(created);
    }

    public async Task<IceContactDto> UpdateIceContactAsync(Guid userId, Guid contactId, UpdateIceContactDto dto)
    {
        var contact = await _iceContactRepository.GetByIdAsync(contactId);
        if (contact == null || contact.UserId != userId)
        {
            throw new KeyNotFoundException($"Không tìm thấy người thân liên hệ với Id = {contactId}");
        }

        if (dto.IsPrimary && !contact.IsPrimary)
        {
            var existingContacts = await _iceContactRepository.GetByUserIdAsync(userId);
            foreach (var p in existingContacts.Where(c => c.IsPrimary && c.Id != contactId))
            {
                p.IsPrimary = false;
                p.UpdatedAt = DateTime.UtcNow;
                await _iceContactRepository.UpdateAsync(p);
            }
        }

        contact.Name = dto.Name.Trim();
        contact.PhoneNumber = dto.PhoneNumber.Trim();
        contact.Relationship = dto.Relationship.Trim();
        contact.IsPrimary = dto.IsPrimary;
        contact.UpdatedAt = DateTime.UtcNow;

        await _iceContactRepository.UpdateAsync(contact);

        if (dto.IsPrimary)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user != null)
            {
                user.EmergencyContactName = contact.Name;
                user.EmergencyContactPhone = contact.PhoneNumber;
                user.EmergencyContactRelationship = contact.Relationship;
                user.UpdatedAt = DateTime.UtcNow;
                await _userRepository.UpdateAsync(user);
            }
        }

        return MapToIceContactDto(contact);
    }

    public async Task<bool> DeleteIceContactAsync(Guid userId, Guid contactId)
    {
        var contact = await _iceContactRepository.GetByIdAsync(contactId);
        if (contact == null || contact.UserId != userId)
        {
            return false;
        }

        var wasPrimary = contact.IsPrimary;
        await _iceContactRepository.DeleteAsync(contact);

        // Nếu xóa người liên hệ chính, tự động chuyển người liên hệ tiếp theo làm chính
        if (wasPrimary)
        {
            var remainingContacts = await _iceContactRepository.GetByUserIdAsync(userId);
            var nextPrimary = remainingContacts.FirstOrDefault(c => c.Id != contactId);

            var user = await _userRepository.GetByIdAsync(userId);
            if (nextPrimary != null)
            {
                nextPrimary.IsPrimary = true;
                nextPrimary.UpdatedAt = DateTime.UtcNow;
                await _iceContactRepository.UpdateAsync(nextPrimary);

                if (user != null)
                {
                    user.EmergencyContactName = nextPrimary.Name;
                    user.EmergencyContactPhone = nextPrimary.PhoneNumber;
                    user.EmergencyContactRelationship = nextPrimary.Relationship;
                    user.UpdatedAt = DateTime.UtcNow;
                    await _userRepository.UpdateAsync(user);
                }
            }
            else if (user != null)
            {
                user.EmergencyContactName = null;
                user.EmergencyContactPhone = null;
                user.EmergencyContactRelationship = null;
                user.UpdatedAt = DateTime.UtcNow;
                await _userRepository.UpdateAsync(user);
            }
        }

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

        // SMS config
        AutoSendSmsOnSos = user.AutoSendSmsOnSos,
        SmsTemplate = user.SmsTemplate,

        // Người liên hệ khẩn cấp chính
        EmergencyContactName = user.EmergencyContactName,
        EmergencyContactPhone = user.EmergencyContactPhone,
        EmergencyContactRelationship = user.EmergencyContactRelationship,

        // Danh bạ ICE
        IceContacts = user.IceContacts?.Select(MapToIceContactDto).ToList() ?? new List<IceContactDto>(),

        IsEmailVerified = user.IsEmailVerified,
        FcmToken = user.FcmToken,
        StationId = user.StationId,
        StationName = user.Station?.Name,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };

    private static IceContactDto MapToIceContactDto(IceContact c) => new()
    {
        Id = c.Id,
        UserId = c.UserId,
        Name = c.Name,
        PhoneNumber = c.PhoneNumber,
        Relationship = c.Relationship,
        IsPrimary = c.IsPrimary,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };
}
