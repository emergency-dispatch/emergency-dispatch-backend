using BCrypt.Net;
using EmergencyDispatch.Application.DTOs.Audit;
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
    private readonly IStationRepository _stationRepository;
    private readonly IRescueUnitRepository _rescueUnitRepository;
    private readonly IStationTeamService _teamService;
    private readonly ISystemAuditService _auditService;
    private readonly IUserSessionInvalidator _sessionInvalidator;
    private readonly ICurrentUserContext _currentUser;

    public UserService(
        IUserRepository userRepository,
        IIceContactRepository iceContactRepository,
        IStationRepository stationRepository,
        IRescueUnitRepository rescueUnitRepository,
        IStationTeamService teamService,
        ISystemAuditService auditService,
        IUserSessionInvalidator sessionInvalidator,
        ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
        _userRepository = userRepository;
        _iceContactRepository = iceContactRepository;
        _stationRepository = stationRepository;
        _rescueUnitRepository = rescueUnitRepository;
        _teamService = teamService;
        _auditService = auditService;
        _sessionInvalidator = sessionInvalidator;
    }

    /// <summary>
    /// Ảnh chụp thông tin quản trị của tài khoản để ghi nhật ký (không chứa mật khẩu / dữ liệu y tế)
    /// </summary>
    private static object AdminSnapshot(User u) => new
    {
        u.FullName,
        u.Email,
        u.PhoneNumber,
        u.Role,
        u.Status,
        u.StationId,
        u.LockReason
    };

    private static string? TeamNameOf(User u) => u.TeamMembership?.Team?.Name;

    public async Task<UserResponseDto> GetByIdAsync(Guid id)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy người dùng với Id = {id}");

        if (_currentUser.IsStationScoped() && user.Id != _currentUser.UserId)
        {
            EnsureOwnStation(user.StationId);
        }

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

        // SMS Config
        if (dto.AutoSendSmsOnSos.HasValue) user.AutoSendSmsOnSos = dto.AutoSendSmsOnSos.Value;
        if (dto.HasSmsTemplate) user.SmsTemplate = dto.SmsTemplate?.Trim();

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
        var (users, totalCount) = await _userRepository.GetUsersPagedAsync(pageIndex, pageSize, search, role, status, _currentUser.FilterStationId());
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

        // Operator tạo nhân viên → luôn thuộc trạm của Operator
        if (actorRole == UserRole.Operator)
        {
            dto.StationId = RequireOwnStation();
        }
        else if (dto.Role == UserRole.Operator && dto.StationId.HasValue)
        {
            await EnsureStationHasNoOtherOperatorAsync(dto.StationId.Value, null);
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
        if (user.Role == UserRole.Operator && user.StationId.HasValue)
        {
            await SetStationHeadAsync(user.StationId.Value, user.Id);
        }
        await _auditService.LogAsync(SystemAuditAction.Created, AuditEntityTypes.User, user.Id, user.Email,
            $"Tạo tài khoản {user.Role} {user.FullName} ({user.Email})", newValue: AdminSnapshot(user));
        return MapToUserResponseDto(user);
    }

    public async Task<UserResponseDto> UpdateUserAsync(Guid id, UpdateUserDto dto, UserRole actorRole)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy người dùng với Id = {id}");

        EnsureCanManageRole(actorRole, user.Role);
        if (dto.Role.HasValue)
        {
            EnsureCanManageRole(actorRole, dto.Role.Value);
        }

        var before = AdminSnapshot(user);
        var (oldRole, oldStatus, oldStationId) = (user.Role, user.Status, user.StationId);

        // Operator chỉ sửa nhân viên trạm mình và không chuyển họ sang trạm khác
        if (actorRole == UserRole.Operator)
        {
            EnsureOwnStation(user.StationId);
            dto.StationId = user.StationId;
        }
        else if ((dto.Role ?? user.Role) == UserRole.Operator && dto.StationId.HasValue && dto.StationId != oldStationId)
        {
            await EnsureStationHasNoOtherOperatorAsync(dto.StationId.Value, user.Id);
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

        // Đồng bộ Operator phụ trách trạm khi Admin đổi trạm / role của Operator
        if (oldRole == UserRole.Operator && (user.Role != UserRole.Operator || user.StationId != oldStationId) && oldStationId.HasValue)
        {
            await SetStationHeadAsync(oldStationId.Value, null, onlyIfHeadIs: user.Id);
        }
        if (user.Role == UserRole.Operator && user.StationId.HasValue && (user.StationId != oldStationId || oldRole != UserRole.Operator))
        {
            await SetStationHeadAsync(user.StationId.Value, user.Id);
        }

        await _auditService.LogAsync(SystemAuditAction.Updated, AuditEntityTypes.User, user.Id, user.Email,
            $"Cập nhật tài khoản {user.FullName} ({user.Email})", before, AdminSnapshot(user));

        // Role / trạng thái / trạm nằm trong JWT → token cũ phải hết hiệu lực ngay
        if (user.Role != oldRole || user.Status != oldStatus || user.StationId != oldStationId)
        {
            await _sessionInvalidator.InvalidateAsync(user.Id, "Thông tin phân quyền của tài khoản vừa thay đổi. Vui lòng đăng nhập lại.");
        }
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
        await _sessionInvalidator.InvalidateAsync(user.Id, "Tài khoản của bạn đã bị khóa. Lý do: " + user.LockReason);
        await _auditService.LogAsync(SystemAuditAction.Locked, AuditEntityTypes.User, user.Id, user.Email,
            $"Khóa tài khoản {user.FullName} ({user.Email}). Lý do: {user.LockReason}",
            new { status = UserStatus.Active }, AdminSnapshot(user));
        return MapToUserResponseDto(user);
    }

    public async Task<UserResponseDto> UnlockUserAsync(Guid id, UserRole actorRole)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy người dùng với Id = {id}");

        EnsureCanManageRole(actorRole, user.Role);
        var before = AdminSnapshot(user);

        user.Status = UserStatus.Active;
        user.LockReason = null;
        user.LockedAt = null;
        user.LockedByUserId = null;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);
        // Xóa trạng thái "đã khóa" trong bộ nhớ đệm để tài khoản dùng lại được ngay
        await _sessionInvalidator.InvalidateAsync(user.Id, "unlocked");
        await _auditService.LogAsync(SystemAuditAction.Unlocked, AuditEntityTypes.User, user.Id, user.Email,
            $"Mở khóa tài khoản {user.FullName} ({user.Email})", before, AdminSnapshot(user));
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

        // Trạm do người này phụ trách → để trống Operator phụ trách
        foreach (var station in await _stationRepository.GetAllAsync(st => st.HeadOperatorId == user.Id))
        {
            station.HeadOperatorId = null;
            station.UpdatedAt = DateTime.UtcNow;
            await _stationRepository.UpdateAsync(station);
        }

        await _sessionInvalidator.InvalidateAsync(user.Id, "Tài khoản đã bị xóa.");
        await _auditService.LogAsync(SystemAuditAction.Deleted, AuditEntityTypes.User, user.Id, user.Email,
            $"Xóa tài khoản {user.Role} {user.FullName} ({user.Email})", AdminSnapshot(user));
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

    public async Task<UserResponseDto> AssignStaffToStationAndTeamAsync(Guid userId, AssignStaffToStationAndTeamDto dto, UserRole actorRole)
    {
        var user = await _userRepository.GetByIdWithDetailsAsync(userId)
            ?? throw new KeyNotFoundException($"Không tìm thấy người dùng với Id = {userId}");

        EnsureCanManageRole(actorRole, user.Role);
        var oldStationId = user.StationId;
        var oldTeamName = TeamNameOf(user);

        // Operator: chỉ kéo nhân viên chưa có trạm về trạm mình, hoặc sắp xếp nhân viên trong trạm mình
        if (actorRole == UserRole.Operator)
        {
            var ownStation = RequireOwnStation();
            if (user.StationId.HasValue && user.StationId != ownStation)
            {
                throw new UnauthorizedAccessException("Nhân viên này đang thuộc trạm khác. Liên hệ Admin để điều chuyển.");
            }
            if (dto.StationId.HasValue && dto.StationId != ownStation)
            {
                throw new UnauthorizedAccessException(OperatorScope.OtherStationMessage);
            }
        }

        if (dto.StationId.HasValue)
        {
            var station = await _stationRepository.GetByIdAsync(dto.StationId.Value)
                ?? throw new KeyNotFoundException($"Không tìm thấy trạm cứu hộ với Id = {dto.StationId.Value}");
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
        var result = updatedUser ?? user;

        await _auditService.LogAsync(SystemAuditAction.StationAssigned, AuditEntityTypes.User, user.Id, user.Email,
            $"Phân công {user.FullName} vào trạm/tổ",
            new { stationId = oldStationId, teamName = oldTeamName },
            new { stationId = result.StationId, teamName = TeamNameOf(result) });

        // StationId nằm trong JWT (dùng để giới hạn quyền theo trạm) → buộc lấy token mới
        if (oldStationId != result.StationId)
        {
            await _sessionInvalidator.InvalidateAsync(user.Id, "Bạn vừa được chuyển trạm. Vui lòng đăng nhập lại.");
        }
        return MapToUserResponseDto(result);
    }

    public async Task<IReadOnlyList<TeamMemberSummaryDto>> GetStaffByStationAsync(Guid stationId)
    {
        var station = await _stationRepository.GetByIdAsync(stationId)
            ?? throw new KeyNotFoundException($"Không tìm thấy trạm cứu hộ với Id = {stationId}");

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
            StationOperator = station.HeadOperator == null || station.HeadOperator.IsDeleted
                ? null
                : new StationOperatorDto
                {
                    Id = station.HeadOperator.Id,
                    FullName = station.HeadOperator.FullName,
                    PhoneNumber = station.HeadOperator.PhoneNumber,
                    Email = station.HeadOperator.Email,
                    AvatarUrl = station.HeadOperator.AvatarUrl
                },
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
    /// Trạm của Operator đang thao tác; chưa được phân trạm thì không quản lý được nhân sự
    /// </summary>
    private Guid RequireOwnStation()
        => _currentUser.StationId ?? throw new UnauthorizedAccessException(OperatorScope.NoStationMessage);

    private void EnsureOwnStation(Guid? stationId)
    {
        var error = _currentUser.CheckStation(stationId);
        if (error != null)
        {
            throw new UnauthorizedAccessException(error);
        }
    }

    /// <summary>
    /// Mỗi trạm chỉ 1 Operator phụ trách
    /// </summary>
    private async Task EnsureStationHasNoOtherOperatorAsync(Guid stationId, Guid? userId)
    {
        var station = await _stationRepository.GetByIdAsync(stationId)
            ?? throw new KeyNotFoundException($"Không tìm thấy trạm cứu hộ với Id = {stationId}");
        if (station.HeadOperatorId.HasValue && station.HeadOperatorId != userId)
        {
            throw new InvalidOperationException($"Trạm \"{station.Name}\" đã có Operator phụ trách. Mỗi trạm chỉ 1 Operator.");
        }
    }

    private async Task SetStationHeadAsync(Guid stationId, Guid? headId, Guid? onlyIfHeadIs = null)
    {
        var station = await _stationRepository.GetByIdAsync(stationId);
        if (station == null || (onlyIfHeadIs.HasValue && station.HeadOperatorId != onlyIfHeadIs))
        {
            return;
        }
        station.HeadOperatorId = headId;
        station.UpdatedAt = DateTime.UtcNow;
        await _stationRepository.UpdateAsync(station);
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
