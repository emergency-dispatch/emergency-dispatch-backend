using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Phạm vi thao tác theo trạm: mỗi Operator quản lý đúng 1 trạm, Admin quản lý toàn bộ.
/// Tác vụ nền (không có người dùng) không bị giới hạn.
/// </summary>
public static class OperatorScope
{
    public const string NoStationMessage = "Bạn chưa được Admin phân công trạm phụ trách.";
    public const string OtherStationMessage = "Bạn chỉ được thao tác dữ liệu của trạm mình phụ trách.";
    public const string OtherStationIncidentMessage = "Sự cố này thuộc trạm khác phụ trách.";

    /// <summary>
    /// Người dùng hiện tại là Operator → phải lọc theo trạm
    /// </summary>
    public static bool IsStationScoped(this ICurrentUserContext user) => user.Role == UserRole.Operator;

    /// <summary>
    /// Trả về lỗi (thông báo) nếu người dùng hiện tại không được quản lý trạm này; null = được phép
    /// </summary>
    public static string? CheckStation(this ICurrentUserContext user, Guid? stationId)
    {
        if (!user.IsStationScoped())
        {
            return null;
        }
        if (!user.StationId.HasValue)
        {
            return NoStationMessage;
        }
        return stationId == user.StationId ? null : OtherStationMessage;
    }

    /// <summary>
    /// Sự cố chưa xác định trạm (null) thì mọi Operator xử lý được để không bị bỏ sót
    /// </summary>
    public static string? CheckIncident(this ICurrentUserContext user, Guid? responsibleStationId)
    {
        if (!user.IsStationScoped() || !responsibleStationId.HasValue)
        {
            return null;
        }
        if (!user.StationId.HasValue)
        {
            return NoStationMessage;
        }
        return responsibleStationId == user.StationId ? null : OtherStationIncidentMessage;
    }

    /// <summary>
    /// Trung tâm thao tác một lượt điều phối (thu hồi / từ chối thay / đổi tiến độ thay / nghiệm thu thay / xem):
    /// Admin mọi lượt; Operator khi sự cố chưa gán trạm, thuộc trạm mình, hoặc xe của lượt là xe trạm mình (đi hỗ trợ).
    /// null = được phép
    /// </summary>
    public static string? CheckAssignment(UserRole role, Guid? actorStationId, Guid? incidentStationId, Guid? unitStationId)
    {
        if (role != UserRole.Operator)
        {
            return null;
        }
        if (!incidentStationId.HasValue)
        {
            return null;
        }
        if (!actorStationId.HasValue)
        {
            return NoStationMessage;
        }
        return incidentStationId == actorStationId || unitStationId == actorStationId ? null : OtherStationIncidentMessage;
    }

    /// <summary>
    /// Trạm dùng để lọc danh sách: Operator → trạm mình; Admin / tác vụ nền → null (không lọc)
    /// </summary>
    public static Guid? FilterStationId(this ICurrentUserContext user)
        => user.IsStationScoped() ? user.StationId ?? Guid.Empty : null;
}
