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
    /// Lấy danh sách người dùng có phân trang và tìm kiếm (Dành cho Quản trị viên)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin))]
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
    /// Tạo tài khoản người dùng mới (Operator, RescueStaff, Admin) (Dành cho Quản trị viên)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(ApiResponseDto<UserResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        try
        {
            var created = await _userService.CreateUserAsync(dto);
            return CreatedAtAction(
                nameof(GetById),
                new { id = created.Id },
                ApiResponseDto<UserResponseDto>.Ok(created, "Tạo người dùng thành công."));
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
    /// Cập nhật thông tin người dùng (Dành cho Quản trị viên)
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(ApiResponseDto<UserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDto dto)
    {
        try
        {
            var updated = await _userService.UpdateUserAsync(id, dto);
            return Ok(ApiResponseDto<UserResponseDto>.Ok(updated, "Cập nhật người dùng thành công."));
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
}
