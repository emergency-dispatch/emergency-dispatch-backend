using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Danh sách số điện thoại bị chặn gửi SOS do báo khống / phá rối (FR-OP-03, BR-05)
/// </summary>
public class BlockedPhoneNumber : BaseEntity
{
    /// <summary>
    /// Số điện thoại đã chuẩn hóa (chỉ chữ số, đầu 0)
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public BlockDuration Duration { get; set; } = BlockDuration.SevenDays;
    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm hết hạn chặn; null = chặn vĩnh viễn
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Lý do chặn bắt buộc (lưu vết kiểm toán)
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    public Guid? IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public Guid? BlockedByUserId { get; set; }
    public User? BlockedByUser { get; set; }

    public DateTime? LastExtendedAt { get; set; }

    // Gỡ chặn thủ công
    public DateTime? RemovedAt { get; set; }
    public Guid? RemovedByUserId { get; set; }
    public User? RemovedByUser { get; set; }
    public string? RemoveReason { get; set; }

    public BlockedNumberStatus GetStatus(DateTime utcNow)
        => RemovedAt.HasValue ? BlockedNumberStatus.Removed
            : ExpiresAt.HasValue && ExpiresAt.Value <= utcNow ? BlockedNumberStatus.Expired
            : BlockedNumberStatus.Active;

    public static DateTime? CalculateExpiry(BlockDuration duration, DateTime fromUtc) => duration switch
    {
        BlockDuration.SevenDays => fromUtc.AddDays(7),
        BlockDuration.ThirtyDays => fromUtc.AddDays(30),
        BlockDuration.OneYear => fromUtc.AddYears(1),
        _ => null
    };
}
