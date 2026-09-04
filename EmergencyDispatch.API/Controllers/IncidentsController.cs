using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// API tiếp nhận báo cáo sự cố khẩn cấp, kích hoạt AI phân loại rủi ro và quản lý vòng đời sự cố
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class IncidentsController : ControllerBase
{
    private readonly IIncidentService _incidentService;

    public IncidentsController(IIncidentService incidentService)
    {
        _incidentService = incidentService;
    }

    /// <summary>
    /// Gửi báo cáo sự cố khẩn cấp mới (SOS Report) kèm ảnh/video
    /// </summary>
    /// <remarks>
    /// Cho phép cả người dân chưa đăng nhập (khách) hoặc đã đăng nhập báo cáo.
    /// Hệ thống sẽ tự động kích hoạt mô hình Vision-Language AI Qwen2.5-VL để phát hiện nguy cơ và chấm điểm Severity.
    /// Nếu AI lỗi, hệ thống tự động Fallback về mức Unclassified (0) và đẩy lên hàng đợi để Operator xác minh.
    /// </remarks>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateIncident([FromBody] CreateIncidentDto dto, CancellationToken cancellationToken)
    {
        Guid? currentUserId = null;
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdClaim, out var parsedGuid))
        {
            currentUserId = parsedGuid;
        }

        var response = await _incidentService.CreateIncidentAsync(dto, currentUserId, cancellationToken);
        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Lấy chi tiết thông tin sự cố theo ID (kèm media và kết quả AI)
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIncidentById(Guid id, CancellationToken cancellationToken)
    {
        var response = await _incidentService.GetIncidentByIdAsync(id, cancellationToken);
        if (!response.Success)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Lấy danh sách sự cố có phân trang, hỗ trợ lọc theo trạng thái, mức độ và tìm kiếm
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<IncidentResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIncidents([FromQuery] IncidentFilterDto filter, CancellationToken cancellationToken)
    {
        var response = await _incidentService.GetIncidentsAsync(filter, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Lấy hàng đợi sự cố chờ xác minh dành cho Điều phối viên (Operator Queue)
    /// </summary>
    /// <remarks>
    /// Sắp xếp ưu tiên: Các vụ Unclassified (Severity = 0 / AI Fallback) được đẩy lên đầu để thẩm tra khẩn cấp,
    /// tiếp theo là các sự cố nguy hiểm từ Level 5 giảm dần đến Level 1.
    /// </remarks>
    [HttpGet("queue")]
    [ProducesResponseType(typeof(ApiResponseDto<List<IncidentResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueue(CancellationToken cancellationToken)
    {
        var response = await _incidentService.GetQueueAsync(cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Xác minh sự cố bởi Điều phối viên (Human-in-the-loop)
    /// </summary>
    /// <remarks>
    /// Cho phép Operator xác nhận hoặc ghi đè mức độ nghiêm trọng mà AI đã chấm điểm trước khi điều phối đội cứu hộ.
    /// </remarks>
    [HttpPut("{id:guid}/verify")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyIncident(Guid id, [FromBody] VerifyIncidentDto dto, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var operatorId))
        {
            return Unauthorized(ApiResponseDto<IncidentResponseDto>.FailureResult("Bạn chưa xác thực tài khoản."));
        }

        var response = await _incidentService.VerifyIncidentAsync(id, dto, operatorId, cancellationToken);
        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Hủy sự cố (báo sai, báo khống hoặc trùng lặp)
    /// </summary>
    [HttpPut("{id:guid}/cancel")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelIncident(Guid id, [FromBody] string reason, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var operatorId))
        {
            return Unauthorized(ApiResponseDto<IncidentResponseDto>.FailureResult("Bạn chưa xác thực tài khoản."));
        }

        var response = await _incidentService.CancelIncidentAsync(id, reason, operatorId, cancellationToken);
        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}
