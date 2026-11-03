using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Traffic;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Dịch vụ quản lý điểm nghẽn giao thông và chướng ngại vật nội khu (Traffic Hotspots).
/// Luồng phụ, không chia cấp độ: báo cáo → (tùy chọn) Operator giao đội phân luồng → giải tỏa / hết hạn.
/// </summary>
public interface ITrafficService
{
    /// <summary>
    /// Báo cáo điểm nghẽn giao thông mới (TTL 10–180 phút, gán trạm gần nhất, broadcast SignalR lên Live Map)
    /// </summary>
    Task<ApiResponseDto<TrafficHotspotResponseDto>> ReportHotspotAsync(
        ReportTrafficHotspotDto dto,
        Guid? currentUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các điểm nghẽn còn trên bản đồ (Active chưa hết hạn hoặc Assigned)
    /// </summary>
    Task<ApiResponseDto<List<TrafficHotspotResponseDto>>> GetActiveHotspotsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chi tiết một điểm nghẽn (kể cả đã giải tỏa / hết hạn)
    /// </summary>
    Task<ApiResponseDto<TrafficHotspotResponseDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator / Admin giao một xe đang sẵn sàng của trạm xuống phân luồng, giải tỏa điểm nghẽn
    /// </summary>
    Task<ApiResponseDto<TrafficHotspotResponseDto>> AssignUnitAsync(
        Guid id,
        AssignTrafficHotspotDto dto,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator / Admin rút xe khỏi điểm nghẽn (xe về Sẵn sàng, điểm nghẽn quay lại Active)
    /// </summary>
    Task<ApiResponseDto<TrafficHotspotResponseDto>> UnassignUnitAsync(
        Guid id,
        UnassignTrafficHotspotDto dto,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Giải tỏa điểm nghẽn. Điểm nghẽn đã giao đội: chỉ Operator / Admin hoặc nhân viên trạm của xe được giao
    /// </summary>
    Task<ApiResponseDto<TrafficHotspotResponseDto>> ResolveHotspotAsync(
        Guid id,
        Guid resolverUserId,
        string? note = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quét và chuyển Expired các điểm nghẽn Active đã quá hạn TTL (bỏ qua điểm đang có đội xử lý)
    /// </summary>
    Task<int> CleanupExpiredHotspotsAsync(CancellationToken cancellationToken = default);
}
