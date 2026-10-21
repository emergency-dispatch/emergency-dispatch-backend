using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Tham số vận hành: cấu hình leo thang (FR-SYS-02, BR-19) và tham số gợi ý điều phối (FR-OP-04, RQ2)
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize(Roles = "Admin,Operator")]
public class SettingsController : ControllerBase
{
    private readonly ISystemSettingsService _settingsService;

    public SettingsController(ISystemSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Cấu hình leo thang: ngưỡng ngày/đêm, khung giờ đêm, hệ số mở rộng bán kính, bán kính tối đa
    /// </summary>
    [HttpGet("escalation")]
    [ProducesResponseType(typeof(ApiResponseDto<SettingsRecordDto<EscalationSettingsDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEscalation(CancellationToken cancellationToken)
        => Ok(ApiResponseDto<SettingsRecordDto<EscalationSettingsDto>>.Ok(await _settingsService.GetEscalationRecordAsync(cancellationToken)));

    [HttpPut("escalation")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<SettingsRecordDto<EscalationSettingsDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<SettingsRecordDto<EscalationSettingsDto>>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateEscalation([FromBody] EscalationSettingsDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _settingsService.UpdateEscalationAsync(dto, GetCurrentUserId(), cancellationToken));

    /// <summary>
    /// Tham số điều phối: trọng số Load-Aware, các nấc bán kính tìm xe, thời gian hồi sức xe
    /// </summary>
    [HttpGet("dispatch")]
    [ProducesResponseType(typeof(ApiResponseDto<SettingsRecordDto<DispatchSettingsDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDispatch(CancellationToken cancellationToken)
        => Ok(ApiResponseDto<SettingsRecordDto<DispatchSettingsDto>>.Ok(await _settingsService.GetDispatchRecordAsync(cancellationToken)));

    [HttpPut("dispatch")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<SettingsRecordDto<DispatchSettingsDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<SettingsRecordDto<DispatchSettingsDto>>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateDispatch([FromBody] DispatchSettingsDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _settingsService.UpdateDispatchAsync(dto, GetCurrentUserId(), cancellationToken));

    /// <summary>
    /// Giá trị mặc định theo SRS (phục vụ nút "Khôi phục mặc định")
    /// </summary>
    [HttpGet("defaults")]
    [ProducesResponseType(typeof(ApiResponseDto<SettingsDefaultsDto>), StatusCodes.Status200OK)]
    public IActionResult GetDefaults() => Ok(ApiResponseDto<SettingsDefaultsDto>.Ok(new SettingsDefaultsDto()));

    private Guid GetCurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
