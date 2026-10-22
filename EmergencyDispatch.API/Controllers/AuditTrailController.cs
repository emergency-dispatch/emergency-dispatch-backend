using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Tra cứu nhật ký kiểm toán bất biến của toàn hệ thống (BR-03, BR-AD-06)
/// </summary>
[ApiController]
[Route("api/audit-trail")]
[Authorize(Roles = "Admin")]
public class AuditTrailController : ControllerBase
{
    private readonly IIncidentService _incidentService;
    private readonly ISystemAuditService _systemAuditService;

    public AuditTrailController(IIncidentService incidentService, ISystemAuditService systemAuditService)
    {
        _incidentService = incidentService;
        _systemAuditService = systemAuditService;
    }

    /// <summary>
    /// Nhật ký thao tác trên sự cố. Lọc theo incidentId, actorId, action, khoảng thời gian from–to (UTC); phân trang
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<AuditTrailEntryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] AuditTrailFilterDto filter, CancellationToken cancellationToken)
        => this.ToHttpResult(await _incidentService.GetAuditTrailAsync(filter, cancellationToken));

    /// <summary>
    /// Nhật ký thao tác quản trị (BR-AD-06): trạm, xe, tài khoản, tổ, cấu hình, quy tắc leo thang, chặn số, xuất báo cáo.
    /// Lọc theo entityType, entityId, actorId, action, from–to (UTC); phân trang
    /// </summary>
    [HttpGet("system")]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<SystemAuditLogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSystem([FromQuery] SystemAuditLogFilterDto filter, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _systemAuditService.GetPagedAsync(filter, cancellationToken);
            return Ok(ApiResponseDto<PaginatedResultDto<SystemAuditLogDto>>.Ok(result, "Lấy nhật ký quản trị thành công."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
    }
}
