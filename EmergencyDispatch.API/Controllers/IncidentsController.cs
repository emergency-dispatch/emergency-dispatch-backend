using System.Security.Claims;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// API tiếp nhận báo cáo sự cố khẩn cấp, kích hoạt AI phân loại rủi ro và quản lý vòng đời sự cố
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
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
    [HttpPost("sos")]
    [AllowAnonymous]
    [EnableRateLimiting("sos")]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateIncident([FromBody] CreateIncidentDto dto, CancellationToken cancellationToken)
    {
        Guid? currentUserId = null;
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdClaim, out var parsedGuid))
        {
            currentUserId = parsedGuid;
        }

        var response = await _incidentService.CreateIncidentAsync(dto, currentUserId, cancellationToken);
        return this.ToHttpResult(response);
    }

    /// <summary>
    /// FR-CIT-05: lịch sử báo cáo của người dân đang đăng nhập (lọc All / Completed / Cancelled), kèm mốc thời gian xử lý
    /// </summary>
    [HttpGet("my-reports")]
    [Authorize(Roles = "Citizen")]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<MyReportDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyReports(
        [FromQuery] MyReportFilter status = MyReportFilter.All,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var response = await _incidentService.GetMyReportsAsync(GetCurrentUserId(), status, pageIndex, pageSize, cancellationToken);
        return this.ToHttpResult(response);
    }

    /// <summary>
    /// Nhật ký kiểm toán toàn bộ vòng đời của một sự cố (mới nhất trước)
    /// </summary>
    [HttpGet("{id:guid}/audit-trail")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<AuditTrailEntryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIncidentAuditTrail(
        Guid id,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await _incidentService.GetAuditTrailAsync(
            new AuditTrailFilterDto { IncidentId = id, PageIndex = pageIndex, PageSize = pageSize }, cancellationToken);
        return this.ToHttpResult(response);
    }

    /// <summary>
    /// Lấy chi tiết thông tin sự cố theo ID (kèm media và kết quả AI)
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIncidentById(Guid id, CancellationToken cancellationToken)
    {
        var response = await _incidentService.GetIncidentByIdAsync(id, GetCurrentUserId(), cancellationToken);
        if (!response.Success)
        {
            return this.ToHttpResult(response);
        }

        // Người dân chỉ được xem sự cố do chính mình báo cáo
        if (User.IsInRole(nameof(UserRole.Citizen)) && response.Data?.ReportedByUserId != GetCurrentUserId())
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseDto<IncidentResponseDto>.FailureResult("Bạn chỉ được xem sự cố do chính mình báo cáo."));
        }

        return Ok(response);
    }

    /// <summary>
    /// Lấy danh sách sự cố có phân trang, hỗ trợ lọc theo trạng thái, mức độ và tìm kiếm
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Operator,Admin")]
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
    /// Sắp xếp ưu tiên (FR-OP-02): Level 0 (Reject / AI lỗi) và Level 3 cùng nhóm cao nhất theo FIFO,
    /// sau đó đến Level 2, cuối cùng Level 1.
    /// </remarks>
    [HttpGet("queue")]
    [Authorize(Roles = "Operator,Admin")]
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
    [Authorize(Roles = "Operator,Admin")]
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
        return this.ToHttpResult(response);
    }

    /// <summary>
    /// Hủy sự cố: Điều phối viên hủy báo sai, hoặc Người dân tự hủy SOS của mình trước khi xe xuất phát (BR-08)
    /// </summary>
    [HttpPut("{id:guid}/cancel")]
    [Authorize(Roles = "Citizen,Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentResponseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelIncident(Guid id, [FromBody] CancelIncidentDto? dto, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty)
        {
            return Unauthorized(ApiResponseDto<IncidentResponseDto>.FailureResult("Bạn chưa xác thực tài khoản."));
        }

        var reason = !string.IsNullOrWhiteSpace(dto?.Reason) ? dto.Reason : LocalizationHelper.Localize("Người dùng tự hủy sự cố");
        var response = await _incidentService.CancelIncidentAsync(id, reason, currentUserId, cancellationToken);
        return this.ToHttpResult(response);
    }

    /// <summary>
    /// Giữ (hoặc gia hạn) khóa mềm 2 phút khi Operator mở sự cố để tránh 2 người cùng xử lý (FR-OP-02, BR-15)
    /// </summary>
    /// <remarks>Gọi lại định kỳ (VD mỗi 60 giây) khi vẫn đang mở sự cố để gia hạn. Người khác đang giữ → 409.</remarks>
    [HttpPut("{id:guid}/lock")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentLockDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentLockDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LockIncident(Guid id, CancellationToken cancellationToken)
    {
        var response = await _incidentService.LockIncidentAsync(id, GetCurrentUserId(), cancellationToken);
        return this.ToHttpResult(response);
    }

    /// <summary>
    /// Nhả khóa mềm khi Operator đóng màn hình sự cố. Admin có thể nhả khóa của người khác với force=true.
    /// </summary>
    [HttpDelete("{id:guid}/lock")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlockIncident(Guid id, [FromQuery] bool force = false, CancellationToken cancellationToken = default)
    {
        var allowForce = force && User.IsInRole(nameof(UserRole.Admin));
        var response = await _incidentService.UnlockIncidentAsync(id, GetCurrentUserId(), allowForce, cancellationToken);
        return this.ToHttpResult(response);
    }

    /// <summary>
    /// Hiện số điện thoại đầy đủ của người báo (BR-32). Mọi lượt xem đều được ghi vào Audit Trail.
    /// </summary>
    [HttpPost("{id:guid}/reveal-phone")]
    [Authorize(Roles = "Operator,Admin,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<EmergencyDispatch.Application.DTOs.Security.RevealPhoneDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<EmergencyDispatch.Application.DTOs.Security.RevealPhoneDto>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevealReporterPhone(Guid id, CancellationToken cancellationToken)
    {
        var response = await _incidentService.RevealReporterPhoneAsync(id, GetCurrentUserId(), cancellationToken);
        return this.ToHttpResult(response);
    }

    /// <summary>
    /// Chặn số điện thoại báo khống của sự cố (FR-OP-03, BR-05). Số bị chặn sẽ không gửi SOS được cho tới khi hết hạn.
    /// </summary>
    [HttpPost("{id:guid}/block-reporter")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<EmergencyDispatch.Application.DTOs.Security.BlockedNumberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<EmergencyDispatch.Application.DTOs.Security.BlockedNumberDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BlockReporter(Guid id, [FromBody] EmergencyDispatch.Application.DTOs.Security.BlockReporterDto dto, CancellationToken cancellationToken)
    {
        var response = await _incidentService.BlockReporterAsync(id, dto, GetCurrentUserId(), cancellationToken);
        return this.ToHttpResult(response);
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var guid) ? guid : Guid.Empty;
    }
}
