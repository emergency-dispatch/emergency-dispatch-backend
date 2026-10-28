using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EmergencyDispatch.API.Filters;

/// <summary>
/// Chuyển ConcurrencyConflictException (dữ liệu vừa bị người khác sửa) thành HTTP 409 + errorCode CONFLICT
/// thay vì 500, để client tải lại dữ liệu rồi thử lại.
/// </summary>
public sealed class ConcurrencyConflictExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ConcurrencyConflictExceptionFilter> _logger;

    public ConcurrencyConflictExceptionFilter(ILogger<ConcurrencyConflictExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ConcurrencyConflictException conflict)
        {
            return;
        }

        _logger.LogWarning("Xung đột đồng thời trên {Entity} tại {Path}.", conflict.EntityName, context.HttpContext.Request.Path);

        var message = conflict.EntityName == nameof(Domain.Entities.DispatchAssignment)
            ? "Nhiệm vụ vừa được cập nhật từ thiết bị khác. Vui lòng tải lại và thử lại."
            : "Sự cố vừa được người khác cập nhật. Vui lòng tải lại và thử lại.";

        context.Result = new ConflictObjectResult(ApiResponseDto<object>.ConflictResult(message));
        context.ExceptionHandled = true;
    }
}
