using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// SOS nằm ngoài khu vực phục vụ (sự kiện ReceiveOutOfAreaAlert)
/// </summary>
public class OutOfAreaAlertDto
{
    public Guid IncidentId { get; set; }
    public string IncidentTitle { get; set; } = string.Empty;
    public string LocationAddress { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double DistanceOutsideAreaKm { get; set; }
    public string? ServiceAreaName { get; set; }
    public Guid? ResponsibleStationId { get; set; }
    public string? ResponsibleStationName { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Sự cố được chuyển sang trạm khác phụ trách (sự kiện ReceiveIncidentTransferred)
/// </summary>
public class IncidentTransferredDto
{
    public Guid IncidentId { get; set; }
    public string IncidentTitle { get; set; } = string.Empty;
    public Guid? FromStationId { get; set; }
    public string? FromStationName { get; set; }
    public Guid ToStationId { get; set; }
    public string? ToStationName { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? TransferredByName { get; set; }
    public DateTime TransferredAt { get; set; }
}

/// <summary>
/// Xe đổi trạng thái (sự kiện ReceiveRescueUnitStatus) — cập nhật live map / danh sách xe không cần tải lại
/// </summary>
public class RescueUnitStatusEventDto
{
    public Guid RescueUnitId { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public Guid StationId { get; set; }
    public RescueUnitStatus Status { get; set; }
    public RescueUnitStatus? PreviousStatus { get; set; }
    public DateTime? CoolDownUntil { get; set; }
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Khóa mềm sự cố thay đổi (sự kiện ReceiveIncidentLock). LockedByOperatorId = null → đã nhả khóa
/// </summary>
public class IncidentLockEventDto
{
    public Guid IncidentId { get; set; }
    public Guid? LockedByOperatorId { get; set; }
    public string? LockedByOperatorName { get; set; }
    public DateTime? LockExpiresAt { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Yêu cầu chuyển trạm phụ trách
/// </summary>
public class TransferIncidentDto
{
    [System.ComponentModel.DataAnnotations.Required]
    public Guid StationId { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MinLength(5, ErrorMessage = "Lý do chuyển trạm tối thiểu 5 ký tự.")]
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
