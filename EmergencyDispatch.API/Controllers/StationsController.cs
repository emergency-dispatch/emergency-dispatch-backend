using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Station;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StationsController : ControllerBase
{
    private readonly IStationService _stationService;

    public StationsController(IStationService stationService)
    {
        _stationService = stationService;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ trạm cứu hộ trong hệ thống
    /// </summary>
    /// <param name="activeOnly">Chỉ lấy các trạm đang hoạt động (mặc định: false)</param>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDto<IReadOnlyList<StationResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = false)
    {
        var stations = await _stationService.GetAllAsync(activeOnly);
        return Ok(ApiResponseDto<IReadOnlyList<StationResponseDto>>.Ok(stations, "Lấy danh sách trạm cứu hộ thành công."));
    }

    /// <summary>
    /// Lấy chi tiết một trạm cứu hộ kèm danh sách xe trực thuộc
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponseDto<StationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var station = await _stationService.GetByIdAsync(id);
            return Ok(ApiResponseDto<StationResponseDto>.Ok(station, "Lấy thông tin trạm cứu hộ thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Thêm mới trạm cứu hộ vào hệ thống (Chỉ dành cho Admin)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<StationResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateStationDto dto)
    {
        var created = await _stationService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.Id },
            ApiResponseDto<StationResponseDto>.Ok(created, "Thêm mới trạm cứu hộ thành công."));
    }

    /// <summary>
    /// Cập nhật thông tin trạm cứu hộ (Chỉ dành cho Admin)
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<StationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStationDto dto)
    {
        try
        {
            var updated = await _stationService.UpdateAsync(id, dto);
            return Ok(ApiResponseDto<StationResponseDto>.Ok(updated, "Cập nhật trạm cứu hộ thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Xóa trạm cứu hộ khỏi hệ thống (Chỉ dành cho Admin)
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _stationService.DeleteAsync(id);
            return Ok(ApiResponseDto<object>.Ok(null!, "Xóa trạm cứu hộ thành công."));
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
