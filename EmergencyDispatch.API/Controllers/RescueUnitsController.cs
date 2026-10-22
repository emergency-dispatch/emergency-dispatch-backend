using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

[ApiController]
[Route("api/rescue-units")]
[Authorize]
public class RescueUnitsController : ControllerBase
{
    private readonly IRescueUnitService _rescueUnitService;

    public RescueUnitsController(IRescueUnitService rescueUnitService)
    {
        _rescueUnitService = rescueUnitService;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ phương tiện / đội xe cứu hộ trong hệ thống (chỉ Admin, Operator)
    /// </summary>
    /// <param name="unitType">Lọc theo loại xe (Ambulance, FireTruck, LadderTruck...)</param>
    /// <param name="availableOnly">Chỉ lấy các xe đang sẵn sàng nhận lệnh (Available)</param>
    /// <param name="stationId">Lọc theo trạm</param>
    /// <param name="status">Lọc theo trạng thái xe</param>
    [HttpGet]
    [Authorize(Roles = StationAccess.FleetManagerRoles)]
    [ProducesResponseType(typeof(ApiResponseDto<IReadOnlyList<RescueUnitResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] RescueUnitType? unitType,
        [FromQuery] bool availableOnly = false,
        [FromQuery] Guid? stationId = null,
        [FromQuery] RescueUnitStatus? status = null)
    {
        var units = availableOnly
            ? await _rescueUnitService.GetAvailableUnitsAsync(unitType)
            : await _rescueUnitService.GetAllAsync();

        if (unitType.HasValue && !availableOnly)
        {
            units = units.Where(u => u.UnitType == unitType.Value).ToList();
        }
        if (stationId.HasValue)
        {
            units = units.Where(u => u.StationId == stationId.Value).ToList();
        }
        if (status.HasValue)
        {
            units = units.Where(u => u.Status == status.Value).ToList();
        }

        return Ok(ApiResponseDto<IReadOnlyList<RescueUnitResponseDto>>.Ok(units, "Lấy danh sách phương tiện cứu hộ thành công."));
    }

    /// <summary>
    /// Lấy thông tin chi tiết một phương tiện cứu hộ theo ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Operator,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<RescueUnitResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var unit = await _rescueUnitService.GetByIdAsync(id);
            if (!User.CanViewFleetOf(unit.StationId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail("Bạn chỉ được xem dữ liệu của trạm mình đang công tác."));
            }
            return Ok(ApiResponseDto<RescueUnitResponseDto>.Ok(unit, "Lấy thông tin xe cứu hộ thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Lấy danh sách xe thuộc một trạm cứu hộ cụ thể
    /// </summary>
    [HttpGet("station/{stationId:guid}")]
    [Authorize(Roles = "Admin,Operator,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<IReadOnlyList<RescueUnitResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByStation(Guid stationId)
    {
        if (!User.CanViewFleetOf(stationId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail("Bạn chỉ được xem dữ liệu của trạm mình đang công tác."));
        }

        try
        {
            var units = await _rescueUnitService.GetByStationIdAsync(stationId);
            return Ok(ApiResponseDto<IReadOnlyList<RescueUnitResponseDto>>.Ok(units, "Lấy danh sách xe theo trạm thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Thêm mới phương tiện cứu hộ vào trạm (Chỉ dành cho Admin / Operator)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<RescueUnitResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateRescueUnitDto dto)
    {
        try
        {
            var created = await _rescueUnitService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.Id },
                ApiResponseDto<RescueUnitResponseDto>.Ok(created, "Thêm mới phương tiện cứu hộ thành công."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật thông tin phương tiện cứu hộ (Chỉ dành cho Admin / Operator)
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<RescueUnitResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRescueUnitDto dto)
    {
        try
        {
            var updated = await _rescueUnitService.UpdateAsync(id, dto);
            return Ok(ApiResponseDto<RescueUnitResponseDto>.Ok(updated, "Cập nhật phương tiện cứu hộ thành công."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật tọa độ GPS trực tiếp của xe (Dành cho Mobile App của tài xế / xe cứu hộ)
    /// </summary>
    [HttpPut("{id:guid}/location")]
    [Authorize(Roles = "Admin,Operator,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<RescueUnitResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateLocation(Guid id, [FromBody] UpdateRescueUnitLocationDto dto)
    {
        try
        {
            // RescueStaff chỉ cập nhật xe thuộc trạm của mình
            var unit = await _rescueUnitService.GetByIdAsync(id);
            if (!User.CanViewStation(unit.StationId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail("Bạn chỉ được cập nhật xe thuộc trạm mình đang công tác."));
            }

            var updated = await _rescueUnitService.UpdateLocationAsync(id, dto);
            return Ok(ApiResponseDto<RescueUnitResponseDto>.Ok(updated, "Cập nhật tọa độ GPS thành công."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật trạng thái xe (Available, Dispatched, OnScene, Returning, Maintenance)
    /// </summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = "Admin,Operator,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<RescueUnitResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateRescueUnitStatusDto dto)
    {
        try
        {
            // RescueStaff chỉ cập nhật xe thuộc trạm của mình
            var unit = await _rescueUnitService.GetByIdAsync(id);
            if (!User.CanViewStation(unit.StationId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail("Bạn chỉ được cập nhật xe thuộc trạm mình đang công tác."));
            }

            var updated = await _rescueUnitService.UpdateStatusAsync(id, dto);
            return Ok(ApiResponseDto<RescueUnitResponseDto>.Ok(updated, "Cập nhật trạng thái xe thành công."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Xóa phương tiện cứu hộ khỏi hệ thống (Chỉ dành cho Admin)
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _rescueUnitService.DeleteAsync(id);
            return Ok(ApiResponseDto<object>.Ok(null!, "Xóa phương tiện cứu hộ thành công."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
    }
}
