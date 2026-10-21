using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Security;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Quản lý danh sách số điện thoại bị chặn gửi SOS (FR-OP-03, BR-05). Chặn số thực hiện tại POST /api/incidents/{id}/block-reporter.
/// </summary>
[ApiController]
[Route("api/blocked-numbers")]
[Authorize(Roles = "Admin,Operator")]
public class BlockedNumbersController : ControllerBase
{
    private readonly IBlockedNumberService _service;

    public BlockedNumbersController(IBlockedNumberService service)
    {
        _service = service;
    }

    /// <summary>
    /// Danh sách số bị chặn (số điện thoại luôn được che). Lọc theo status: Active / Expired / Removed.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<BlockedNumberDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] BlockedNumberStatus? status,
        [FromQuery] string? search,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetPagedAsync(status, search, pageIndex, pageSize, cancellationToken);
        return Ok(ApiResponseDto<PaginatedResultDto<BlockedNumberDto>>.Ok(result));
    }

    /// <summary>
    /// Gỡ chặn số điện thoại (bắt buộc lý do)
    /// </summary>
    [HttpPost("{id:guid}/unblock")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<BlockedNumberDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Unblock(Guid id, [FromBody] UnblockNumberDto dto, CancellationToken cancellationToken)
    {
        var result = await _service.UnblockAsync(id, dto, GetCurrentUserId(), cancellationToken);
        return this.ToHttpResult(result);
    }

    /// <summary>
    /// Gia hạn thời gian chặn, tính lại từ thời điểm hiện tại (bắt buộc lý do)
    /// </summary>
    [HttpPost("{id:guid}/extend")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<BlockedNumberDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Extend(Guid id, [FromBody] ExtendBlockDto dto, CancellationToken cancellationToken)
    {
        var result = await _service.ExtendAsync(id, dto, GetCurrentUserId(), cancellationToken);
        return this.ToHttpResult(result);
    }

    private Guid GetCurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
