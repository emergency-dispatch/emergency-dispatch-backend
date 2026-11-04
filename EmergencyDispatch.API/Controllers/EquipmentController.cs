using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Equipment;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Thiết bị cứu hộ của trạm (trạng thái Available / Maintenance).
/// Admin: mọi trạm. Operator: quản lý thiết bị trạm mình. RescueStaff: xem và đổi trạng thái thiết bị trạm mình.
/// </summary>
[ApiController]
[Route("api/equipment")]
[Authorize]
public class EquipmentController : ControllerBase
{
    private readonly IEquipmentService _equipmentService;

    public EquipmentController(IEquipmentService equipmentService)
    {
        _equipmentService = equipmentService;
    }

    /// <summary>
    /// Danh sách thiết bị. Operator / RescueStaff bỏ trống stationId thì lấy trạm mình; trạm khác → 403
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Operator,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<List<EquipmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<List<EquipmentDto>>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] Guid? stationId,
        [FromQuery] Guid? rescueUnitId,
        [FromQuery] EquipmentStatus? status,
        [FromQuery] EquipmentCategory? category,
        CancellationToken cancellationToken)
        => this.ToHttpResult(await _equipmentService.GetListAsync(new EquipmentFilterDto
        {
            StationId = stationId,
            RescueUnitId = rescueUnitId,
            Status = status,
            Category = category
        }, cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Operator,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => this.ToHttpResult(await _equipmentService.GetByIdAsync(id, cancellationToken));

    /// <summary>
    /// Thêm thiết bị (trạng thái ban đầu Available). Xe trang bị (tùy chọn) phải cùng trạm
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateEquipmentDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _equipmentService.CreateAsync(dto, cancellationToken));

    /// <summary>
    /// Sửa thông tin thiết bị; chỉ Admin được đổi trạm sở hữu
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEquipmentDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _equipmentService.UpdateAsync(id, dto, cancellationToken));

    /// <summary>
    /// Đổi trạng thái Available / Maintenance (Maintenance bắt buộc lý do ≥ 5 ký tự). Nhân viên cứu hộ dùng để báo thiết bị hỏng
    /// </summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = "Admin,Operator,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponseDto<EquipmentDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateEquipmentStatusDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _equipmentService.UpdateStatusAsync(id, dto, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => this.ToHttpResult(await _equipmentService.DeleteAsync(id, cancellationToken));
}
