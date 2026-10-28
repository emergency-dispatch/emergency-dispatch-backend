using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Traffic;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// API Báo cáo &amp; Giám sát Điểm nghẽn Giao thông / Chướng ngại vật nội khu (Traffic Hotspots)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TrafficController : ControllerBase
{
    private readonly ITrafficService _trafficService;

    public TrafficController(ITrafficService trafficService)
    {
        _trafficService = trafficService;
    }

    /// <summary>
    /// Báo cáo điểm nghẽn giao thông hoặc chướng ngại vật nội khu mới
    /// </summary>
    /// <remarks>
    /// Cho phép cả công nhân/người dân chưa đăng nhập hoặc đã đăng nhập báo cáo.
    /// Thông tin sẽ tự động được phát sóng lên Live Map của Điều phối viên và Đội cứu hộ để chủ động điều hướng lộ trình né tắc đường.
    /// </remarks>
    [HttpPost("report")]
    [AllowAnonymous]
    [EnableRateLimiting("traffic-report")]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReportHotspot(
        [FromBody] ReportTrafficHotspotDto dto,
        CancellationToken cancellationToken)
    {
        Guid? currentUserId = null;
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(claim, out var guid))
        {
            currentUserId = guid;
        }

        var result = await _trafficService.ReportHotspotAsync(dto, currentUserId, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách toàn bộ các điểm nghẽn giao thông đang còn hiệu lực (chưa hết hạn TTL)
    /// </summary>
    [HttpGet("active")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponseDto<List<TrafficHotspotResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveHotspots(CancellationToken cancellationToken)
    {
        var result = await _trafficService.GetActiveHotspotsAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Giải tỏa thủ công một điểm nghẽn giao thông (sau khi giao thông thông thoáng hoặc dọn xong vật cản)
    /// </summary>
    [HttpPut("{id:guid}/resolve")]
    [Authorize(Roles = "Operator,Admin,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResolveHotspot(Guid id, CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = Guid.TryParse(claim, out var guid) ? guid : Guid.Empty;

        var result = await _trafficService.ResolveHotspotAsync(id, userId, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
