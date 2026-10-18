using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Traffic;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

public class TrafficService : ITrafficService
{
    private readonly ITrafficHotspotRepository _trafficRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<TrafficService> _logger;

    public TrafficService(
        ITrafficHotspotRepository trafficRepository,
        IUserRepository userRepository,
        IRealtimeNotifier realtimeNotifier,
        ILogger<TrafficService> logger)
    {
        _trafficRepository = trafficRepository;
        _userRepository = userRepository;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<ApiResponseDto<TrafficHotspotResponseDto>> ReportHotspotAsync(
        ReportTrafficHotspotDto dto,
        Guid? currentUserId = null,
        CancellationToken cancellationToken = default)
    {
        User? user = null;
        if (currentUserId.HasValue)
        {
            user = await _userRepository.GetByIdAsync(currentUserId.Value);
        }

        var reporterName = dto.ReporterName?.Trim() ?? user?.FullName ?? "Người dân / Công nhân";
        var reporterPhone = dto.ReporterPhone?.Trim() ?? user?.PhoneNumber;

        var ttl = Math.Clamp(dto.TtlMinutes, 10, 180);
        var now = DateTime.UtcNow;

        var hotspot = new TrafficHotspot
        {
            Title = dto.Title.Trim(),
            Description = dto.Description?.Trim(),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            LocationAddress = dto.LocationAddress.Trim(),
            CongestionLevel = dto.CongestionLevel,
            MediaUrl = dto.MediaUrl?.Trim(),
            ReportedByUserId = currentUserId,
            ReportedByUser = user,
            ReporterName = reporterName,
            ReporterPhone = reporterPhone,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(ttl),
            IsActive = true
        };

        await _trafficRepository.AddAsync(hotspot);

        _logger.LogInformation("Điểm nghẽn mới '{Title}' tại {Address} đã được báo cáo (TTL={TTL} phút)",
            hotspot.Title, hotspot.LocationAddress, ttl);

        var responseDto = MapToResponseDto(hotspot);

        // Bắn SignalR lên Live Map của Operator và Rescuer
        await _realtimeNotifier.TrafficHotspotReportedAsync(responseDto, cancellationToken);

        return ApiResponseDto<TrafficHotspotResponseDto>.SuccessResult(
            responseDto,
            "Báo cáo điểm nghẽn giao thông thành công. Thông tin đã được cập nhật lên bản đồ điều phối.");
    }

    public async Task<ApiResponseDto<List<TrafficHotspotResponseDto>>> GetActiveHotspotsAsync(
        CancellationToken cancellationToken = default)
    {
        var list = await _trafficRepository.GetActiveHotspotsAsync(cancellationToken);
        var dtos = list.Select(MapToResponseDto).ToList();

        return ApiResponseDto<List<TrafficHotspotResponseDto>>.SuccessResult(
            dtos,
            $"Lấy danh sách {dtos.Count} điểm nghẽn đang hoạt động thành công.");
    }

    public async Task<ApiResponseDto<TrafficHotspotResponseDto>> ResolveHotspotAsync(
        Guid id,
        Guid resolverUserId,
        CancellationToken cancellationToken = default)
    {
        var hotspot = await _trafficRepository.GetByIdAsync(id);
        if (hotspot == null || !hotspot.IsActive)
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.FailureResult("Không tìm thấy điểm nghẽn hoặc điểm nghẽn đã hết hiệu lực.");
        }

        hotspot.IsActive = false;
        hotspot.ResolvedAt = DateTime.UtcNow;
        hotspot.ResolvedByUserId = resolverUserId;
        hotspot.UpdatedAt = DateTime.UtcNow;

        await _trafficRepository.UpdateAsync(hotspot);

        _logger.LogInformation("Điểm nghẽn {HotspotId} đã được giải tỏa bởi người dùng {UserId}", id, resolverUserId);

        var responseDto = MapToResponseDto(hotspot);

        // Cập nhật lại bản đồ qua SignalR
        await _realtimeNotifier.TrafficHotspotReportedAsync(responseDto, cancellationToken);

        return ApiResponseDto<TrafficHotspotResponseDto>.SuccessResult(
            responseDto,
            "Đã cập nhật giải tỏa điểm nghẽn thành công.");
    }

    public async Task<int> CleanupExpiredHotspotsAsync(CancellationToken cancellationToken = default)
    {
        var count = await _trafficRepository.DeactivateExpiredHotspotsAsync(cancellationToken);
        if (count > 0)
        {
            _logger.LogInformation("Hotspot Cleanup Worker: Đã dọn dẹp và vô hiệu hóa {Count} điểm nghẽn quá hạn TTL.", count);
        }
        return count;
    }

    private static TrafficHotspotResponseDto MapToResponseDto(TrafficHotspot hotspot)
    {
        return new TrafficHotspotResponseDto
        {
            Id = hotspot.Id,
            Title = hotspot.Title,
            Description = hotspot.Description,
            Latitude = hotspot.Latitude,
            Longitude = hotspot.Longitude,
            LocationAddress = hotspot.LocationAddress,
            CongestionLevel = hotspot.CongestionLevel,
            MediaUrl = hotspot.MediaUrl,
            ReportedByUserId = hotspot.ReportedByUserId,
            ReporterName = hotspot.ReporterName ?? hotspot.ReportedByUser?.FullName,
            CreatedAt = hotspot.CreatedAt,
            ExpiresAt = hotspot.ExpiresAt,
            IsActive = hotspot.IsActive
        };
    }
}
