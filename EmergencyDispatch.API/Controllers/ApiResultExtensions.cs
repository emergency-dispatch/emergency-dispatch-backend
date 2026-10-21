using EmergencyDispatch.Application.DTOs.Common;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Ánh xạ kết quả nghiệp vụ sang mã HTTP tương ứng (200 / 400 / 403 / 404 / 409)
/// </summary>
internal static class ApiResultExtensions
{
    public static IActionResult ToHttpResult<T>(this ControllerBase controller, ApiResponseDto<T> result)
    {
        if (result.Success)
        {
            return controller.Ok(result);
        }

        return result.ErrorCode switch
        {
            ApiErrorCodes.Conflict => controller.Conflict(result),
            ApiErrorCodes.Forbidden => controller.StatusCode(StatusCodes.Status403Forbidden, result),
            ApiErrorCodes.NotFound => controller.NotFound(result),
            ApiErrorCodes.TooManyRequests => controller.StatusCode(StatusCodes.Status429TooManyRequests, result),
            _ => controller.BadRequest(result)
        };
    }
}
