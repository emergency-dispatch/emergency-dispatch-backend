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
    public DispatchAssignmentStatus Status { get; set; }
    public string? Notes { get; set; }

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
