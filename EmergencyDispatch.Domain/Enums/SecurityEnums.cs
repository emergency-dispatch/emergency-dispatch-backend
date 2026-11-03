namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Thời hạn chặn số điện thoại báo khống (FR-OP-03, BR-05)
/// </summary>
public enum BlockDuration
{
    SevenDays = 0,
    ThirtyDays = 1,
    OneYear = 2,
    Permanent = 3
}

/// <summary>
/// Trạng thái hiện tại của một bản ghi chặn số
/// </summary>
public enum BlockedNumberStatus
{
    Active = 0,
    Expired = 1,
    Removed = 2
}

/// <summary>
/// Loại thao tác quản trị được ghi vào nhật ký hệ thống (BR-AD-06)
/// </summary>
public enum SystemAuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    StatusChanged = 3,
    Locked = 4,
    Unlocked = 5,
    StationAssigned = 6,
    SettingsUpdated = 7,
    Unblocked = 8,
    BlockExtended = 9,
    Exported = 10,
    /// <summary>Operator giao xe / đội xử lý điểm nghẽn giao thông</summary>
    TrafficTaskAssigned = 11,
    /// <summary>Operator rút xe / đội khỏi điểm nghẽn</summary>
    TrafficTaskUnassigned = 12,
    /// <summary>Điểm nghẽn được giải tỏa</summary>
    TrafficResolved = 13
}
