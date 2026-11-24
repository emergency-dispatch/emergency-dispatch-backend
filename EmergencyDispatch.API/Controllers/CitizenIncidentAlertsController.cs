using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>Area-only announcements addressed to the current citizen / Thông báo khu vực gửi cho người dân hiện tại.</summary>
[ApiController]
[Route("api/citizen-incident-alerts")]
[Authorize(Roles = "Citizen")]
public sealed class CitizenIncidentAlertsController(ICitizenIncidentAlertService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<CitizenIncidentAlertDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(ApiResponseDto<PaginatedResultDto<CitizenIncidentAlertDto>>.Ok(await service.GetMineAsync(UserId(), pageIndex, pageSize, cancellationToken)));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponseDto<CitizenIncidentAlertDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<CitizenIncidentAlertDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOne(Guid id, CancellationToken cancellationToken)
        => this.ToHttpResult(await service.GetMineAsync(id, UserId(), cancellationToken));

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
