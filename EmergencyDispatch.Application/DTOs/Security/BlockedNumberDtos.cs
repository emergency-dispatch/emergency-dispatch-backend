using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Security;

/// <summary>
/// Operator chặn số điện thoại báo khống của một sự cố (FR-OP-03, BR-05)
/// </summary>
public class BlockReporterDto
{
    [Required(ErrorMessage = "Thời hạn chặn là bắt buộc.")]
    public BlockDuration Duration { get; set; } = BlockDuration.SevenDays;

    [Required(ErrorMessage = "Lý do chặn là bắt buộc.")]
    [MinLength(5, ErrorMessage = "Lý do chặn phải có ít nhất 5 ký tự.")]
    [MaxLength(500, ErrorMessage = "Lý do chặn tối đa 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Admin gỡ chặn số điện thoại (bắt buộc lý do)
/// </summary>
public class UnblockNumberDto
{
    [Required(ErrorMessage = "Lý do gỡ chặn là bắt buộc.")]
    [MinLength(5, ErrorMessage = "Lý do gỡ chặn phải có ít nhất 5 ký tự.")]
    [MaxLength(500, ErrorMessage = "Lý do gỡ chặn tối đa 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Admin gia hạn thời gian chặn (tính lại từ thời điểm hiện tại)
/// </summary>
public class ExtendBlockDto
{
    [Required(ErrorMessage = "Thời hạn chặn là bắt buộc.")]
    public BlockDuration Duration { get; set; }

    [Required(ErrorMessage = "Lý do gia hạn là bắt buộc.")]
    [MinLength(5, ErrorMessage = "Lý do gia hạn phải có ít nhất 5 ký tự.")]
    [MaxLength(500, ErrorMessage = "Lý do gia hạn tối đa 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Một bản ghi trong danh sách số bị chặn (số điện thoại luôn được che)
/// </summary>
public class BlockedNumberDto
{
    public Guid Id { get; set; }
    public string MaskedPhone { get; set; } = string.Empty;
    public BlockDuration Duration { get; set; }
    public BlockedNumberStatus Status { get; set; }
    public DateTime BlockedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? IncidentId { get; set; }
    public string? BlockedByName { get; set; }
    public DateTime? LastExtendedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
    public string? RemovedByName { get; set; }
    public string? RemoveReason { get; set; }
}

/// <summary>
/// Số điện thoại đầy đủ của người báo (chỉ trả khi bấm "Hiện số", có ghi vết)
/// </summary>
public class RevealPhoneDto
{
    public Guid IncidentId { get; set; }
    public string? PhoneNumber { get; set; }
}

/// <summary>
/// Admin khóa / mở khóa tài khoản thủ công
/// </summary>
public class UserLockDto
{
    [MaxLength(500, ErrorMessage = "Lý do tối đa 500 ký tự.")]
    public string? Reason { get; set; }
}
