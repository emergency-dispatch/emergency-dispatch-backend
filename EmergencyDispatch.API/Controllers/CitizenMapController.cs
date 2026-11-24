using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>Public incident markers for active citizens / Sự cố công khai trên bản đồ người dân.</summary>
[ApiController]
[Route("api/citizen-map/incidents")]
[Authorize(Roles = "Citizen")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CitizenMapController(ICitizenMapService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<CitizenMapIncidentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get([FromQuery] CitizenMapQueryDto query, CancellationToken cancellationToken)
        => this.ToHttpResult(await service.GetAsync(UserId(), query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponseDto<CitizenMapIncidentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOne(Guid id, CancellationToken cancellationToken)
        => this.ToHttpResult(await service.GetAsync(UserId(), id, cancellationToken));

    /// <summary>Open a current marker from the publicAlertId in a citizen notification.</summary>
    [HttpGet("by-alert/{publicAlertId:guid}")]
    [ProducesResponseType(typeof(ApiResponseDto<CitizenMapIncidentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByAlert(Guid publicAlertId, CancellationToken cancellationToken)
        => this.ToHttpResult(await service.GetByAlertAsync(UserId(), publicAlertId, cancellationToken));

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
