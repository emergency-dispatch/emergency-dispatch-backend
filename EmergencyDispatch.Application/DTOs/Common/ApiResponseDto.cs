using EmergencyDispatch.Application.Localization;

namespace EmergencyDispatch.Application.DTOs.Common;

/// <summary>
/// Định dạng chuẩn cho response API (tự động chuyển ngữ song ngữ vi/en theo Accept-Language)
/// </summary>
/// <typeparam name="T">Kiểu dữ liệu data trả về</typeparam>
public class ApiResponseDto<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }

    /// <summary>
    /// Mã lỗi nghiệp vụ để client phân biệt (CONFLICT, FORBIDDEN, NOT_FOUND); null khi thành công hoặc lỗi dữ liệu thông thường
    /// </summary>
    public string? ErrorCode { get; set; }

    public static ApiResponseDto<T> Ok(T data, string? message = null)
    {
        var rawMessage = string.IsNullOrWhiteSpace(message) ? "Thành công" : message;
        return new()
        {
            Success = true,
            Message = LocalizationHelper.Localize(rawMessage),
            Data = data
        };
    }

    public static ApiResponseDto<T> Fail(string message, List<string>? errors = null) =>
        new()
        {
            Success = false,
            Message = LocalizationHelper.Localize(message),
            Errors = errors?.Select(e => LocalizationHelper.Localize(e)).ToList()
        };

    public static ApiResponseDto<T> SuccessResult(T data, string? message = null) =>
        Ok(data, message);

    public static ApiResponseDto<T> FailureResult(string message, List<string>? errors = null) =>
        Fail(message, errors);

    public static ApiResponseDto<T> FailureResult(List<string> errors, string message = "Dữ liệu không hợp lệ") =>
        Fail(message, errors);

    public static ApiResponseDto<T> ConflictResult(string message)
    {
        var result = Fail(message);
        result.ErrorCode = ApiErrorCodes.Conflict;
        return result;
    }

    public static ApiResponseDto<T> ForbiddenResult(string message)
    {
        var result = Fail(message);
        result.ErrorCode = ApiErrorCodes.Forbidden;
        return result;
    }

    public static ApiResponseDto<T> TooManyRequestsResult(string message)
    {
        var result = Fail(message);
        result.ErrorCode = ApiErrorCodes.TooManyRequests;
        return result;
    }

    public static ApiResponseDto<T> NotFoundResult(string message)
    {
        var result = Fail(message);
        result.ErrorCode = ApiErrorCodes.NotFound;
        return result;
    }
}

/// <summary>
/// Mã lỗi nghiệp vụ dùng chung giữa Backend và Frontend/Mobile
/// </summary>
public static class ApiErrorCodes
{
    /// <summary>Xung đột đồng thời: sự cố đang bị Operator khác khóa, xe đã được giao (HTTP 409 — MSG32)</summary>
    public const string Conflict = "CONFLICT";

    /// <summary>Không đủ quyền trên tài nguyên cụ thể (HTTP 403)</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>Không tìm thấy tài nguyên (HTTP 404)</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>Gửi quá nhiều yêu cầu trong thời gian ngắn — BR-CIT-01 (HTTP 429)</summary>
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
}
