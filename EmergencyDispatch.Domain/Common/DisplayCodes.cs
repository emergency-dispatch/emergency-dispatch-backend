using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Common;

/// <summary>
/// Mã ngắn hiển thị cho người dùng thay cho Guid (Guid vẫn là khóa dùng trong API)
/// </summary>
public static class DisplayCodes
{
    /// <summary>
    /// Giờ Việt Nam (UTC+7, không có giờ mùa hè)
    /// </summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// Mã sự cố: SC-yyMMdd-NNNN (ngày báo theo giờ VN + số thứ tự toàn hệ thống), VD SC-261117-0042
    /// </summary>
    public static string IncidentCode(long number, DateTime createdAtUtc)
        => $"SC-{createdAtUtc.Add(VietnamOffset):yyMMdd}-{number:D4}";

    /// <summary>
    /// Mã nhân sự: NV-NNNN (nhân viên cứu hộ, điều phối viên, quản trị). Người dân không có mã
    /// </summary>
    public static string? StaffCode(int? number, UserRole role)
        => number.HasValue && role != UserRole.Citizen ? $"NV-{number.Value:D4}" : null;

    public static bool NeedsStaffNumber(UserRole role) => role != UserRole.Citizen;
}
