using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Thông báo trong ứng dụng của người dùng đang đăng nhập (realtime: SignalR "ReceiveNotification" trên /hubs/incidents)
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<NotificationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _notificationService.GetMyNotificationsAsync(GetCurrentUserId(), unreadOnly, pageIndex, pageSize, cancellationToken);
        return Ok(ApiResponseDto<PaginatedResultDto<NotificationDto>>.Ok(result));
    }

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(ApiResponseDto<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
        => Ok(ApiResponseDto<int>.Ok(await _notificationService.GetUnreadCountAsync(GetCurrentUserId(), cancellationToken)));

    [HttpPut("{id:guid}/read")]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
        => Ok(ApiResponseDto<bool>.Ok(await _notificationService.MarkReadAsync(GetCurrentUserId(), id, cancellationToken)));

    [HttpPut("read-all")]
    [ProducesResponseType(typeof(ApiResponseDto<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
        => Ok(ApiResponseDto<int>.Ok(await _notificationService.MarkAllReadAsync(GetCurrentUserId(), cancellationToken)));

    private Guid GetCurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
