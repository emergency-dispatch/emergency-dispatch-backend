namespace EmergencyDispatch.Application.DTOs.Common;

/// <summary>
/// Định dạng chuẩn cho response API
/// </summary>
/// <typeparam name="T">Kiểu dữ liệu data trả về</typeparam>
public class ApiResponseDto<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }

    public static ApiResponseDto<T> Ok(T data, string message = "Thành công") =>
        new() { Success = true, Message = message, Data = data };

    public static ApiResponseDto<T> Fail(string message, List<string>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors };

    public static ApiResponseDto<T> SuccessResult(T data, string message = "Thành công") =>
        Ok(data, message);

    public static ApiResponseDto<T> FailureResult(string message, List<string>? errors = null) =>
        Fail(message, errors);

    public static ApiResponseDto<T> FailureResult(List<string> errors, string message = "Dữ liệu không hợp lệ") =>
        Fail(message, errors);
}
