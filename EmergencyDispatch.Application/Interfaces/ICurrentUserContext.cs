using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Người dùng của request hiện tại (lấy từ JWT). Null khi chạy nền hoặc ẩn danh.
/// </summary>
public interface ICurrentUserContext
{
    Guid? UserId { get; }
    string? FullName { get; }
    UserRole? Role { get; }
    string? IpAddress { get; }

    /// <summary>
    /// Trạm của người dùng (claim "StationId"): Operator = trạm phụ trách, RescueStaff = trạm công tác
    /// </summary>
    Guid? StationId { get; }
}
