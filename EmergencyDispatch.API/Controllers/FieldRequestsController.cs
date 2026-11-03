using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Yêu cầu từ hiện trường: đội cứu hộ xin chi viện thêm xe (Backup) hoặc báo vấn đề khác (Other) về Operator.
/// Quá ngưỡng escalation.fieldRequestThresholdMinutes chưa xử lý → báo Admin. Mọi thao tác ghi audit của sự cố.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class FieldRequestsController : ControllerBase
{
    private readonly IFieldRequestService _fieldRequestService;

    public FieldRequestsController(IFieldRequestService fieldRequestService)
    {
        _fieldRequestService = fieldRequestService;
    }

    /// <summary>
    /// Staff gửi yêu cầu cho lượt điều phối của đội mình (Accepted / EnRoute / OnScene)
    /// </summary>
    [HttpPost("dispatch/assignments/{assignmentId:guid}/requests")]
    [Authorize(Roles = "RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(Guid assignmentId, [FromBody] CreateFieldRequestDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _fieldRequestService.CreateAsync(assignmentId, dto, GetCurrentUserId(), cancellationToken));

    /// <summary>
    /// Yêu cầu đang mở (Open / Acknowledged), cũ nhất trước. Operator thấy yêu cầu của sự cố trạm mình và của xe trạm mình đi hỗ trợ
    /// </summary>
    [HttpGet("field-requests")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<List<FieldRequestDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOpen(CancellationToken cancellationToken)
        => this.ToHttpResult(await _fieldRequestService.GetOpenAsync(cancellationToken));

    /// <summary>
    /// Toàn bộ yêu cầu của một sự cố (Staff chỉ thấy yêu cầu của xe trạm mình)
    /// </summary>
    [HttpGet("incidents/{incidentId:guid}/field-requests")]
    [Authorize(Roles = "Operator,Admin,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<List<FieldRequestDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByIncident(Guid incidentId, CancellationToken cancellationToken)
        => this.ToHttpResult(await _fieldRequestService.GetByIncidentAsync(incidentId, GetCurrentUserId(), cancellationToken));

    /// <summary>
    /// Operator tiếp nhận (đang xử lý) — báo lại cho đội gửi
    /// </summary>
    [HttpPut("field-requests/{id:guid}/acknowledge")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken)
        => this.ToHttpResult(await _fieldRequestService.AcknowledgeAsync(id, GetCurrentUserId(), cancellationToken));

    /// <summary>
    /// Đánh dấu đã đáp ứng (ghi chú tùy chọn). Backup: nên giao xe qua POST /api/dispatch/assign kèm fieldRequestId — yêu cầu tự Fulfilled
    /// </summary>
    [HttpPut("field-requests/{id:guid}/fulfill")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Fulfill(Guid id, [FromBody] RespondFieldRequestDto? dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _fieldRequestService.FulfillAsync(id, dto?.Note, GetCurrentUserId(), cancellationToken));

    /// <summary>
    /// Từ chối (bắt buộc lý do ≥ 5 ký tự trong note)
    /// </summary>
    [HttpPut("field-requests/{id:guid}/decline")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decline(Guid id, [FromBody] RespondFieldRequestDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _fieldRequestService.DeclineAsync(id, dto.Note, GetCurrentUserId(), cancellationToken));

    /// <summary>
    /// Staff tự hủy yêu cầu của đội mình khi không cần nữa (lý do tùy chọn)
    /// </summary>
    [HttpPut("field-requests/{id:guid}/cancel")]
    [Authorize(Roles = "RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<FieldRequestDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] RespondFieldRequestDto? dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _fieldRequestService.CancelAsync(id, dto?.Note, GetCurrentUserId(), cancellationToken));

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var guid) ? guid : Guid.Empty;
    }
}
