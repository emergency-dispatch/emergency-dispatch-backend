using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Traffic;

/// <summary>
/// DTO thông tin điểm nghẽn giao thông đang hoạt động phục vụ hiển thị layer bản đồ
/// </summary>
public class TrafficHotspotResponseDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string LocationAddress { get; set; } = string.Empty;
    public TrafficCongestionLevel CongestionLevel { get; set; }
    public string CongestionLevelName => CongestionLevel.ToString();
    public string? MediaUrl { get; set; }
    public Guid? ReportedByUserId { get; set; }
    public string? ReporterName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Số phút còn lại trước khi tự động hết hạn hiển thị
    /// </summary>
    public double RemainingMinutes
    {
        get
        {
            var remaining = (ExpiresAt - DateTime.UtcNow).TotalMinutes;
            return remaining > 0 ? Math.Round(remaining, 1) : 0;
        }
    }
}
