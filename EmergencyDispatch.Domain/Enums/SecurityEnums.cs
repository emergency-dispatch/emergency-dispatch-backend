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
