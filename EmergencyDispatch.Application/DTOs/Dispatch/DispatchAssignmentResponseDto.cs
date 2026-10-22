using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// DTO phản hồi chi tiết về một phân công điều phối
/// </summary>
public class DispatchAssignmentResponseDto
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }

    /// <summary>
    /// Trạm phụ trách sự cố; khác trạm của xe = xe được điều hỗ trợ
    /// </summary>
    public Guid? ResponsibleStationId { get; set; }
    public string IncidentTitle { get; set; } = string.Empty;
    public string IncidentAddress { get; set; } = string.Empty;
    public double IncidentLat { get; set; }
    public double IncidentLng { get; set; }
    public SeverityLevel IncidentSeverity { get; set; }

    public Guid RescueUnitId { get; set; }
    public RescueUnitResponseDto? RescueUnit { get; set; }

    public DateTime DispatchedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? EnRouteAt { get; set; }
    public DispatchAssignmentStatus Status { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Thuật toán gợi ý đã dùng khi phát lệnh, khoảng cách và ETA tại thời điểm đó (dữ liệu RQ2)
    /// </summary>
    public string? DispatchAlgorithm { get; set; }
    public double? DistanceKm { get; set; }
    public double? EstimatedArrivalMinutes { get; set; }
    public Guid? AssignedByUserId { get; set; }

    /// <summary>
    /// Lý do từ chối nhiệm vụ (BR-22) — chỉ có khi Status = Rejected
    /// </summary>
    public RejectionReason? RejectionReason { get; set; }
    public string? RejectionNotes { get; set; }
    public DateTime? RejectedAt { get; set; }

    /// <summary>
    /// Báo cáo nghiệm thu (khi Status = Completed)
    /// </summary>
    public CompletionReportDto? CompletionReport { get; set; }

    /// <summary>
    /// Thời gian từ khi phát lệnh điều phối đến khi đội xe có mặt tại hiện trường (phút)
    /// </summary>
    public double? ResponseDurationMinutes
    {
        get
        {
            if (ArrivedAt.HasValue)
            {
                return (ArrivedAt.Value - DispatchedAt).TotalMinutes;
            }
            return null;
        }
    }

    /// <summary>
    /// Tổng thời gian xử lý tại hiện trường (phút)
    /// </summary>
    public double? OnSceneDurationMinutes
    {
        get
        {
            if (ArrivedAt.HasValue && CompletedAt.HasValue)
            {
                return (CompletedAt.Value - ArrivedAt.Value).TotalMinutes;
            }
            return null;
        }
    }
}

/// <summary>
/// Báo cáo nghiệm thu hoàn thành nhiệm vụ của một xe (FR-RS-03)
/// </summary>
public class CompletionReportDto
{
    public Guid Id { get; set; }
    public string ActionsSummary { get; set; } = string.Empty;
    public int VictimsAssistedCount { get; set; }
    public List<string> EquipmentUsed { get; set; } = new();
    public List<string> EvidenceMediaUrls { get; set; } = new();
    public string? HandoverEntity { get; set; }
    public string? HandoverContactName { get; set; }
    public string? HandoverContactPhone { get; set; }
    public string? HandoverNotes { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime SubmittedAt { get; set; }
}
