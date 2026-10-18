using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EmergencyDispatch.API.Filters;

/// <summary>
/// Thêm header Accept-Language vào Swagger UI để chọn nhanh giữa Tiếng Việt (vi) và Tiếng Anh (en)
/// </summary>
public class AcceptLanguageHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Accept-Language",
            In = ParameterLocation.Header,
            Description = "Ngôn ngữ mong muốn nhận thông báo từ hệ thống (vi: Tiếng Việt, en: English)",
            Required = false,
            Schema = new OpenApiSchema
            {
                Type = "string",
                Default = new OpenApiString("vi"),
                Enum = new List<IOpenApiAny>
                {
                    new OpenApiString("vi"),
                    new OpenApiString("en")
                }
            }
        });
    }
}
