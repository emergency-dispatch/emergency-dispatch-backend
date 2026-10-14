using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Traffic;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Dịch vụ quản lý điểm nghẽn giao thông và chướng ngại vật nội khu (Traffic Hotspots)
/// </summary>
public interface ITrafficService
{
    /// <summary>
    /// Báo cáo điểm nghẽn giao thông mới (TTL 30-45 phút, broadcast SignalR lên Live Map)
    /// </summary>
    Task<ApiResponseDto<TrafficHotspotResponseDto>> ReportHotspotAsync(
        ReportTrafficHotspotDto dto,
        Guid? currentUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách toàn bộ các điểm nghẽn đang còn hiệu lực trên bản đồ
    /// </summary>
    Task<ApiResponseDto<List<TrafficHotspotResponseDto>>> GetActiveHotspotsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Giải tỏa thủ công một điểm nghẽn (dành cho Operator hoặc tuần tra viên)
    /// </summary>
    Task<ApiResponseDto<TrafficHotspotResponseDto>> ResolveHotspotAsync(
        Guid id,
        Guid resolverUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quét và vô hiệu hóa các điểm nghẽn đã quá hạn TTL (dành cho Background Worker)
    /// </summary>
    Task<int> CleanupExpiredHotspotsAsync(CancellationToken cancellationToken = default);
}
