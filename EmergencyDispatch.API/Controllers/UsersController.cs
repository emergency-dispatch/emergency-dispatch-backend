using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.User;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Lấy thông tin cá nhân của người dùng đang đăng nhập
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDto<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId))
            {
                return Unauthorized(ApiResponseDto<object>.Fail("Không xác định được danh tính người dùng."));
            }

            var profile = await _userService.GetProfileAsync(userId);
            return Ok(ApiResponseDto<UserResponseDto>.Ok(profile, "Lấy thông tin cá nhân thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Cập nhật thông tin cá nhân của người dùng đang đăng nhập
    /// </summary>
    [HttpPut("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDto<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        try
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId))
            {
                return Unauthorized(ApiResponseDto<object>.Fail("Không xác định được danh tính người dùng."));
            }

            var updated = await _userService.UpdateProfileAsync(userId, dto);
            return Ok(ApiResponseDto<UserResponseDto>.Ok(updated, "Cập nhật thông tin cá nhân thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Đăng ký hoặc cập nhật FCM Device Token cho thiết bị (phục vụ nhận Push Notification âm thanh còi hú)
    /// </summary>
    [HttpPut("fcm-token")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateFcmToken([FromBody] EmergencyDispatch.Application.DTOs.User.UpdateFcmTokenDto dto)
    {
        try
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId))
            {
                return Unauthorized(ApiResponseDto<object>.Fail("Không xác định được danh tính người dùng."));
            }

            var success = await _userService.UpdateFcmTokenAsync(userId, dto.FcmToken);
            if (!success)
            {
                return BadRequest(ApiResponseDto<object>.Fail("Không thể cập nhật FCM Token."));
            }

            return Ok(ApiResponseDto<object>.Ok(null!, "Cập nhật FCM Token thành công."));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Lấy danh sách người dùng có phân trang và tìm kiếm (Dành cho Quản trị viên và Điều phối viên)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Operator)}")]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<UserResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] UserRole? role = null,
        [FromQuery] UserStatus? status = null)
    {
        try
        {
            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var result = await _userService.GetAllAsync(pageIndex, pageSize, search, role, status);
            return Ok(ApiResponseDto<PaginatedResultDto<UserResponseDto>>.Ok(result, "Lấy danh sách người dùng thành công."));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Lấy thông tin chi tiết một người dùng theo Id (Admin hoặc Operator)
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Operator)}")]
    [ProducesResponseType(typeof(ApiResponseDto<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var user = await _userService.GetByIdAsync(id);
            return Ok(ApiResponseDto<UserResponseDto>.Ok(user, "Lấy thông tin người dùng thành công."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Tạo tài khoản người dùng mới (Dành cho Quản trị viên và Điều phối viên)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Operator)}")]
    [ProducesResponseType(typeof(ApiResponseDto<UserResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        try
        {
            var created = await _userService.CreateUserAsync(dto, GetCurrentRole());
            return CreatedAtAction(
                nameof(GetById),
                new { id = created.Id },
                ApiResponseDto<UserResponseDto>.Ok(created, "Tạo người dùng thành công."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Cập nhật thông tin người dùng, trạm và tổ làm việc (Dành cho Quản trị viên và Điều phối viên)
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Operator)}")]
    [ProducesResponseType(typeof(ApiResponseDto<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDto dto)
    {
        try
        {
            var updated = await _userService.UpdateUserAsync(id, dto, GetCurrentRole());
            return Ok(ApiResponseDto<UserResponseDto>.Ok(updated, "Cập nhật người dùng thành công."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Xóa mềm người dùng (Dành cho Quản trị viên)
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        try
        {
            var deleted = await _userService.DeleteUserAsync(id);
            if (!deleted)
            {
                return NotFound(ApiResponseDto<object>.Fail("Không tìm thấy người dùng cần xóa."));
            }

            return Ok(ApiResponseDto<object>.Ok(null!, "Xóa người dùng thành công."));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Lấy thông tin Tổ làm việc, đồng đội và danh sách xe trực ban thuộc trạm của tài khoản đang đăng nhập
    /// </summary>
    [HttpGet("my-team")]
    [Authorize(Roles = "RescueStaff,Operator,Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<StaffTeamResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyTeam()
    {
        try
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId))
            {
                return Unauthorized(ApiResponseDto<object>.Fail("Không xác định được danh tính người dùng."));
            }

            var team = await _userService.GetMyTeamAsync(userId);
            return Ok(ApiResponseDto<StaffTeamResponseDto>.Ok(team, "Lấy thông tin Tổ làm việc thành công."));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Phân công nhân viên vào Trạm cứu hộ và chỉ định Tổ làm việc (Dành cho Điều phối viên &amp; Admin)
    /// </summary>
    [HttpPut("{id:guid}/assign-station-team")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Operator)}")]
    [ProducesResponseType(typeof(ApiResponseDto<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignStationAndTeam(Guid id, [FromBody] AssignStaffToStationAndTeamDto dto)
    {
        try
        {
            var updated = await _userService.AssignStaffToStationAndTeamAsync(id, dto, GetCurrentRole());
            return Ok(ApiResponseDto<UserResponseDto>.Ok(updated, "Phân công nhân viên vào trạm và tổ làm việc thành công."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Lấy danh sách nhân viên trực thuộc một trạm cứu hộ kèm Tổ làm việc
    /// </summary>
    [HttpGet("station/{stationId:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Operator)},{nameof(UserRole.RescueStaff)}")]
    [ProducesResponseType(typeof(ApiResponseDto<IReadOnlyList<TeamMemberSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStaffByStation(Guid stationId)
    {
        if (!User.CanViewStation(stationId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail("Bạn chỉ được xem dữ liệu của trạm mình đang công tác."));
        }

        try
        {
            var staff = await _userService.GetStaffByStationAsync(stationId);
            return Ok(ApiResponseDto<IReadOnlyList<TeamMemberSummaryDto>>.Ok(staff, "Lấy danh sách nhân viên trạm thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto<object>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Admin khóa tài khoản thủ công kèm lý do (tối thiểu 5 ký tự); thu hồi mọi phiên đăng nhập
    /// </summary>
    [HttpPost("{id:guid}/lock")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(ApiResponseDto<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LockUser(Guid id, [FromBody] EmergencyDispatch.Application.DTOs.Security.UserLockDto dto)
    {
        try
        {
            var actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : Guid.Empty;
            var updated = await _userService.LockUserAsync(id, dto.Reason ?? string.Empty, actorId, GetCurrentRole());
            return Ok(ApiResponseDto<UserResponseDto>.Ok(updated, "Đã khóa tài khoản."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Admin mở khóa tài khoản (gỡ cả khóa thủ công và khóa tạm do đăng nhập sai)
    /// </summary>
    [HttpPost("{id:guid}/unlock")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(ApiResponseDto<UserResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlockUser(Guid id)
    {
        try
        {
            var updated = await _userService.UnlockUserAsync(id, GetCurrentRole());
            return Ok(ApiResponseDto<UserResponseDto>.Ok(updated, "Đã mở khóa tài khoản."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponseDto<object>.Fail(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    private UserRole GetCurrentRole()
    {
        var roleStr = User.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(roleStr, out var role) ? role : UserRole.Citizen;
    }
}
