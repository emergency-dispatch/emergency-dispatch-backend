using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Escalation;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// API Quản lý Quy tắc &amp; Nhật ký Leo thang Tự động (Escalation Rules &amp; Logs)
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public class EscalationController : ControllerBase
{
    private readonly IEscalationService _escalationService;

    public EscalationController(IEscalationService escalationService)
    {
        _escalationService = escalationService;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ các quy tắc leo thang sự cố
    /// </summary>
    [HttpGet("escalation-rules")]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<IReadOnlyList<EscalationRuleDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRules(CancellationToken cancellationToken)
    {
        var rules = await _escalationService.GetRulesAsync(cancellationToken);
        return Ok(ApiResponseDto<IReadOnlyList<EscalationRuleDto>>.SuccessResult(rules, "Lấy danh sách quy tắc leo thang thành công."));
    }

    /// <summary>
    /// Lấy chi tiết một quy tắc leo thang theo ID
    /// </summary>
    [HttpGet("escalation-rules/{id:guid}")]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<EscalationRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<EscalationRuleDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRuleById(Guid id, CancellationToken cancellationToken)
    {
        var rule = await _escalationService.GetRuleByIdAsync(id, cancellationToken);
        if (rule == null)
        {
            return NotFound(ApiResponseDto<EscalationRuleDto>.FailureResult("Không tìm thấy quy tắc leo thang chỉ định."));
        }

        return Ok(ApiResponseDto<EscalationRuleDto>.SuccessResult(rule));
    }

    /// <summary>
    /// Tạo quy tắc leo thang mới (Chỉ dành cho Ban Quản trị)
    /// </summary>
    [HttpPost("escalation-rules")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<EscalationRuleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponseDto<EscalationRuleDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRule(
        [FromBody] CreateEscalationRuleDto dto,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponseDto<EscalationRuleDto>.FailureResult("Dữ liệu quy tắc không hợp lệ."));
        }

        var created = await _escalationService.CreateRuleAsync(dto, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponseDto<EscalationRuleDto>.SuccessResult(created, "Tạo mới quy tắc leo thang thành công."));
    }

    /// <summary>
    /// Cập nhật quy tắc leo thang (Chỉ dành cho Ban Quản trị)
    /// </summary>
    [HttpPut("escalation-rules/{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<EscalationRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<EscalationRuleDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRule(
        Guid id,
        [FromBody] UpdateEscalationRuleDto dto,
        CancellationToken cancellationToken)
    {
        var updated = await _escalationService.UpdateRuleAsync(id, dto, cancellationToken);
        if (updated == null)
        {
            return NotFound(ApiResponseDto<EscalationRuleDto>.FailureResult("Không tìm thấy quy tắc leo thang để cập nhật."));
        }

        return Ok(ApiResponseDto<EscalationRuleDto>.SuccessResult(updated, "Cập nhật quy tắc leo thang thành công."));
    }

    /// <summary>
    /// Xóa mềm một quy tắc leo thang (Chỉ dành cho Ban Quản trị)
    /// </summary>
    [HttpDelete("escalation-rules/{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _escalationService.DeleteRuleAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(ApiResponseDto<bool>.FailureResult("Không tìm thấy quy tắc để xóa."));
        }

        return Ok(ApiResponseDto<bool>.SuccessResult(true, "Đã xóa quy tắc leo thang thành công."));
    }

    /// <summary>
    /// Tra cứu lịch sử / nhật ký kích hoạt leo thang tự động của hệ thống
    /// </summary>
    [HttpGet("escalation-logs")]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<IReadOnlyList<EscalationLogDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? incidentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var logs = await _escalationService.GetLogsAsync(incidentId, page, pageSize, cancellationToken);
        return Ok(ApiResponseDto<IReadOnlyList<EscalationLogDto>>.SuccessResult(logs, "Lấy lịch sử leo thang thành công."));
    }

    /// <summary>
    /// Kích hoạt chu kỳ quét leo thang thủ công ngay lập tức (Test / Benchmark / Quản trị)
    /// </summary>
    [HttpPost("escalation/trigger-check")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TriggerCheck(CancellationToken cancellationToken)
    {
        var count = await _escalationService.ProcessEscalationsAsync(cancellationToken);
        return Ok(ApiResponseDto<int>.SuccessResult(count, $"Đã thực hiện quét kiểm tra và kích hoạt {count} sự cố leo thang."));
    }
}
