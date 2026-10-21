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

    public AuditTrailController(IIncidentService incidentService)
    {
        _incidentService = incidentService;
    }

    /// <summary>
    /// Lọc theo incidentId, actorId, action, khoảng thời gian from–to (UTC); phân trang
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<AuditTrailEntryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] AuditTrailFilterDto filter, CancellationToken cancellationToken)
        => this.ToHttpResult(await _incidentService.GetAuditTrailAsync(filter, cancellationToken));
}
