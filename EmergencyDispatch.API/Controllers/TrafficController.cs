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
    /// Chi tiết một điểm nghẽn (kể cả đã giải tỏa / hết hạn) – Operator, Admin, RescueStaff
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Operator,Admin,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHotspot(Guid id, CancellationToken cancellationToken)
        => this.ToHttpResult(await _trafficService.GetByIdAsync(id, cancellationToken));

    /// <summary>
    /// Giao một xe đang sẵn sàng của trạm xuống phân luồng, giải tỏa điểm nghẽn (Operator trạm phụ trách, Admin).
    /// Xe chuyển sang Dispatched (không được gợi ý cho sự cố) tới khi giải tỏa hoặc bị rút.
    /// </summary>
    [HttpPut("{id:guid}/assign")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignUnit(Guid id, [FromBody] AssignTrafficHotspotDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _trafficService.AssignUnitAsync(id, dto, CurrentUserId(), cancellationToken));

    /// <summary>
    /// Rút xe khỏi điểm nghẽn (VD: cần xe cho sự cố khẩn cấp). Lý do bắt buộc ≥ 5 ký tự. Xe về Sẵn sàng, điểm nghẽn quay lại Active
    /// </summary>
    [HttpPut("{id:guid}/unassign")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnassignUnit(Guid id, [FromBody] UnassignTrafficHotspotDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _trafficService.UnassignUnitAsync(id, dto, CurrentUserId(), cancellationToken));

    /// <summary>
    /// Giải tỏa điểm nghẽn (sau khi giao thông thông thoáng hoặc dọn xong vật cản). Body tùy chọn: { "note": "..." }.
    /// Điểm nghẽn đã giao đội: chỉ Operator / Admin hoặc nhân viên trạm của xe được giao. Xe được giao trở về Sẵn sàng.
    /// </summary>
    [HttpPut("{id:guid}/resolve")]
    [Authorize(Roles = "Operator,Admin,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<TrafficHotspotResponseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResolveHotspot(Guid id, [FromBody] ResolveTrafficHotspotDto? dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _trafficService.ResolveHotspotAsync(id, CurrentUserId(), dto?.Note, cancellationToken));

    private Guid CurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
