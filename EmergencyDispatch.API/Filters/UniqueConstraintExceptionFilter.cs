using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EmergencyDispatch.API.Filters;

/// <summary>
/// Lưới an toàn: vi phạm chỉ mục duy nhất của PostgreSQL (23505) → HTTP 409 + CONFLICT thay vì 500.
/// Xảy ra khi hai request tạo cùng biển số / email gần như đồng thời (cả hai đều qua bước kiểm tra trùng),
/// hoặc khi service chưa kiểm tra trùng cho một trường duy nhất.
/// </summary>
public sealed class UniqueConstraintExceptionFilter : IExceptionFilter
{
    public const string UniqueViolation = "23505";

    private readonly ILogger<UniqueConstraintExceptionFilter> _logger;

    public UniqueConstraintExceptionFilter(ILogger<UniqueConstraintExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (!TryGetUniqueViolation(context.Exception, out var constraint))
        {
            return;
        }

        _logger.LogWarning("Vi phạm ràng buộc duy nhất {Constraint} tại {Path}.", constraint, context.HttpContext.Request.Path);
        context.Result = new ConflictObjectResult(ApiResponseDto<object>.ConflictResult(LocalizationHelper.Localize(MessageFor(constraint))));
        context.ExceptionHandled = true;
    }

    public static bool TryGetUniqueViolation(Exception exception, out string? constraint)
    {
        constraint = null;
        if (exception is DbUpdateException { InnerException: PostgresException { SqlState: UniqueViolation } pg })
        {
            constraint = pg.ConstraintName;
            return true;
        }

        return false;
    }

    public static string MessageFor(string? constraint) => constraint switch
    {
        "IX_RescueUnits_PlateNumber" => "Biển số xe đã tồn tại trong hệ thống.",
        "IX_Users_Email" => "Email này đã được sử dụng trong hệ thống.",
        "IX_Stations_Name" => "Tên trạm đã tồn tại.",
        "IX_Stations_Code" => "Mã trạm đã tồn tại.",
        _ => "Dữ liệu trùng với một bản ghi đã có. Vui lòng kiểm tra lại."
    };
}
