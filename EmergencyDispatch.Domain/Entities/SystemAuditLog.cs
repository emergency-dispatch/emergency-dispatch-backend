using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Nhật ký thao tác quản trị: ai sửa gì (trạm, xe, tài khoản, cấu hình...), lúc nào, giá trị trước/sau (BR-AD-06).
/// Chỉ ghi thêm, không sửa/xóa.
/// </summary>
public class SystemAuditLog : BaseEntity
{
    public Guid? ActorId { get; set; }
    public string? ActorName { get; set; }
    public string? ActorRole { get; set; }

    public SystemAuditAction Action { get; set; }

    /// <summary>
    /// Loại đối tượng: Station, RescueUnit, User, StationTeam, Settings, EscalationRule, BlockedNumber, Report
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? EntityName { get; set; }

    public string Description { get; set; } = string.Empty;
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }

    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
