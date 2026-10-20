using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// API Điều phối cứu hộ &amp; Bộ tối ưu hóa phân bổ nguồn lực (RQ2: Nearest-Available vs Load-Aware)
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DispatchController : ControllerBase
{
    private readonly IDispatchService _dispatchService;

    public DispatchController(IDispatchService dispatchService)
    {
        _dispatchService = dispatchService;
    }

    /// <summary>
    /// Gợi ý đơn vị cứu hộ tối ưu cho sự cố theo thuật toán Nearest-Available hoặc Load-Aware (Nghiên cứu RQ2)
    /// </summary>
    /// <param name="incidentId">Mã định danh sự cố</param>
    /// <param name="algorithm">Thuật toán lựa chọn: NearestAvailable (0) hoặc LoadAware (1)</param>
    /// <param name="unitType">Tùy chọn lọc theo loại phương tiện (vd: Ambulance, FireTruck...)</param>
    /// <param name="maxDistanceKm">Bán kính quét tối đa tính từ hiện trường (mặc định 15 km)</param>
    /// <param name="cancellationToken">Cancellation Token</param>
    [HttpGet("recommend")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<List<RecommendedUnitResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<List<RecommendedUnitResponseDto>>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] Guid incidentId,
        [FromQuery] DispatchAlgorithm algorithm = DispatchAlgorithm.NearestAvailable,
        [FromQuery] RescueUnitType? unitType = null,
        [FromQuery] double maxDistanceKm = 15.0,
        CancellationToken cancellationToken = default)
    {
        var result = await _dispatchService.GetRecommendationsAsync(
            incidentId,
            algorithm,
            unitType,
            maxDistanceKm,
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách các phương tiện cứu hộ đang khả dụng trong bán kính gần sự cố
    /// </summary>
    [HttpGet("available-units")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<List<RecommendedUnitResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableUnits(
        [FromQuery] Guid incidentId,
        [FromQuery] double maxDistanceKm = 15.0,
        CancellationToken cancellationToken = default)
    {
        var result = await _dispatchService.GetAvailableUnitsNearbyAsync(incidentId, maxDistanceKm, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Phát lệnh điều phối xe cứu hộ tới hiện trường sự cố
    /// </summary>
    [HttpPost("assign")]
    [Authorize(Roles = "Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<DispatchAssignmentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<DispatchAssignmentResponseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AssignUnit(
        [FromBody] AssignDispatchDto dto,
        CancellationToken cancellationToken)
    {
        var operatorId = GetCurrentUserId();
        var result = await _dispatchService.AssignUnitAsync(dto, operatorId, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Lấy chi tiết lượt điều phối theo ID
    /// </summary>
    [HttpGet("assignments/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponseDto<DispatchAssignmentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<DispatchAssignmentResponseDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssignmentById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _dispatchService.GetAssignmentByIdAsync(id, cancellationToken);
        if (!result.Success)
        {
            return NotFound(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách các lượt điều phối của một sự cố (Cho phép người dân theo dõi vị trí xe cứu hộ)
    /// </summary>
    [HttpGet("assignments/incident/{incidentId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponseDto<List<DispatchAssignmentResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAssignmentsByIncident(Guid incidentId, CancellationToken cancellationToken)
    {
        var result = await _dispatchService.GetAssignmentsByIncidentIdAsync(incidentId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Lấy thông tin đường di chuyển cứu hộ, tọa độ xe thời gian thực và thời gian dự kiến (ETA) cho Người dân
    /// </summary>
    [HttpGet("tracking/incident/{incidentId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponseDto<CitizenTrackingResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<CitizenTrackingResponseDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCitizenTracking(Guid incidentId, CancellationToken cancellationToken)
    {
        var result = await _dispatchService.GetCitizenTrackingAsync(incidentId, cancellationToken);
        if (!result.Success)
        {
            return NotFound(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách các nhiệm vụ được phân công cho nhân viên cứu hộ đang đăng nhập
    /// </summary>
    [HttpGet("my-assignments")]
    [Authorize(Roles = "RescueStaff,Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<List<DispatchAssignmentResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyAssignments(CancellationToken cancellationToken)
    {
        var rescuerUserId = GetCurrentUserId();
        var result = await _dispatchService.GetMyAssignmentsAsync(rescuerUserId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Cập nhật tiến độ nhiệm vụ (Accepted -> EnRoute -> OnScene)
    /// </summary>
    [HttpPut("assignments/{id:guid}/status")]
    [Authorize(Roles = "RescueStaff,Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<DispatchAssignmentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<DispatchAssignmentResponseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAssignmentStatus(
        Guid id,
        [FromBody] UpdateAssignmentStatusDto dto,
        CancellationToken cancellationToken)
    {
        var rescuerUserId = GetCurrentUserId();
        var result = await _dispatchService.UpdateAssignmentStatusAsync(id, dto, rescuerUserId, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Nghiệm thu hiện trường, gửi báo cáo hoàn thành và đóng hồ sơ sự cố (Ghi nhận KPI T_Response)
    /// </summary>
    [HttpPost("assignments/{id:guid}/resolve")]
    [Authorize(Roles = "RescueStaff,Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<DispatchAssignmentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<DispatchAssignmentResponseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResolveAssignment(
        Guid id,
        [FromBody] ResolveDispatchDto dto,
        CancellationToken cancellationToken)
    {
        var rescuerUserId = GetCurrentUserId();
        var result = await _dispatchService.ResolveAssignmentAsync(id, dto, rescuerUserId, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var guid) ? guid : Guid.Empty;
    }
}
