using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Station;
using EmergencyDispatch.Application.DTOs.Team;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StationsController : ControllerBase
{
    private readonly IStationService _stationService;
    private readonly IStationTeamService _teamService;

    public StationsController(IStationService stationService, IStationTeamService teamService)
    {
        _stationService = stationService;
        _teamService = teamService;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ trạm cứu hộ trong hệ thống
    /// </summary>
    /// <param name="activeOnly">Chỉ lấy các trạm đang hoạt động (mặc định: false)</param>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDto<IReadOnlyList<StationResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = false)
    {
        var stations = (await _stationService.GetAllAsync(activeOnly))
            .Select(s => User.CanViewStation(s.Id) ? s : WithoutFleetDetails(s))
            .ToList();
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
            if (!User.CanViewStation(id))
            {
                station = WithoutFleetDetails(station);
            }
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
        try
        {
            var created = await _stationService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.Id },
                ApiResponseDto<StationResponseDto>.Ok(created, "Thêm mới trạm cứu hộ thành công."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
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

    /// <summary>
    /// Lấy danh sách nhân viên trực thuộc trạm cứu hộ kèm Tổ làm việc
    /// </summary>
    [HttpGet("{id:guid}/staff")]
    [Authorize(Roles = "Admin,Operator,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<IReadOnlyList<StationStaffMemberDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStationStaff(Guid id)
    {
        if (!User.CanViewStation(id))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail("Bạn chỉ được xem dữ liệu của trạm mình đang công tác."));
        }

        try
        {
            var station = await _stationService.GetByIdAsync(id);
            return Ok(ApiResponseDto<IReadOnlyList<StationStaffMemberDto>>.Ok(station.StaffMembers, "Lấy danh sách nhân viên trạm thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Danh sách tổ làm việc của trạm (tên, xe phụ trách, thành viên kèm vai trò kíp)
    /// </summary>
    [HttpGet("{id:guid}/teams")]
    [Authorize(Roles = "Admin,Operator,RescueStaff")]
    [ProducesResponseType(typeof(ApiResponseDto<List<StationTeamDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeams(Guid id, CancellationToken cancellationToken)
    {
        if (!User.CanViewStation(id))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail("Bạn chỉ được xem dữ liệu của trạm mình đang công tác."));
        }
        return this.ToHttpResult(await _teamService.GetTeamsAsync(id, cancellationToken));
    }

    /// <summary>
    /// Tạo tổ làm việc: tối đa 6 người, tối đa 1 tổ trưởng (Commander), thành viên là RescueStaff của trạm, mỗi xe tối đa 1 tổ
    /// </summary>
    [HttpPost("{id:guid}/teams")]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<StationTeamDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<StationTeamDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTeam(Guid id, [FromBody] StationTeamInputDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _teamService.CreateAsync(id, dto, cancellationToken));

    /// <summary>
    /// Cập nhật tên, xe phụ trách và danh sách thành viên của tổ (gửi đầy đủ danh sách thành viên mong muốn)
    /// </summary>
    [HttpPut("{id:guid}/teams/{teamId:guid}")]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<StationTeamDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<StationTeamDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateTeam(Guid id, Guid teamId, [FromBody] StationTeamInputDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _teamService.UpdateAsync(id, teamId, dto, cancellationToken));

    /// <summary>
    /// Xóa tổ (thành viên vẫn thuộc trạm)
    /// </summary>
    [HttpDelete("{id:guid}/teams/{teamId:guid}")]
    [Authorize(Roles = "Admin,Operator")]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTeam(Guid id, Guid teamId, CancellationToken cancellationToken)
        => this.ToHttpResult(await _teamService.DeleteAsync(id, teamId, cancellationToken));

    /// <summary>
    /// Người không quản lý trạm (Citizen, nhân viên trạm khác) chỉ thấy thông tin công khai của trạm:
    /// ẩn vị trí / trạng thái từng xe và danh sách nhân sự
    /// </summary>
    private static StationResponseDto WithoutFleetDetails(StationResponseDto s)
    {
        s.RescueUnits = new List<EmergencyDispatch.Application.DTOs.RescueUnit.RescueUnitResponseDto>();
        s.StaffMembers = new List<StationStaffMemberDto>();
        s.ActiveUnitsCount = 0;
        s.StaffCount = 0;
        s.HeadOperatorPhone = null;
        return s;
    }
}
