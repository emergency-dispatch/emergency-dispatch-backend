using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.RescueUnit;

/// <summary>
/// Người gửi mẫu GPS (SignalR truyền từ Context.User, REST lấy từ JWT của request)
/// </summary>
public sealed record GpsSender(Guid? UserId, UserRole? Role);
