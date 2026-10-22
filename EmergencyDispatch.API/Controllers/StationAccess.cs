using System.Security.Claims;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Quyền theo trạm (mỗi Operator phụ trách 1 trạm):
/// - Thông tin quản lý trạm (nhân sự, tổ, chi tiết): Admin mọi trạm; Operator / RescueStaff chỉ trạm mình.
/// - Đội xe (vị trí, trạng thái — chỉ đọc): Admin, Operator thấy mọi trạm để điều xe hỗ trợ; RescueStaff chỉ trạm mình.
/// Citizen chỉ thấy xe được phân công cho sự cố của mình qua API tracking.
/// </summary>
internal static class StationAccess
{
    public const string FleetManagerRoles = "Admin,Operator";

    public static bool IsFleetManager(this ClaimsPrincipal user)
        => user.IsInRole("Admin") || user.IsInRole("Operator");

    public static bool CanViewStation(this ClaimsPrincipal user, Guid stationId)
    {
        if (user.IsInRole("Admin"))
        {
            return true;
        }

        return (user.IsInRole("Operator") || user.IsInRole("RescueStaff"))
            && Guid.TryParse(user.FindFirstValue("StationId"), out var ownStation)
            && ownStation == stationId;
    }

    /// <summary>
    /// Xem vị trí / trạng thái xe (chỉ đọc)
    /// </summary>
    public static bool CanViewFleetOf(this ClaimsPrincipal user, Guid stationId)
        => user.IsFleetManager() || user.CanViewStation(stationId);
}
