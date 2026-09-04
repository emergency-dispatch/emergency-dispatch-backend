using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// API điều khiển Module Trí tuệ Nhân tạo (Vision-Language AI Qwen2.5-VL)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AiController : ControllerBase
{
    private readonly IAiClassificationService _aiClassificationService;

    public AiController(IAiClassificationService aiClassificationService)
    {
        _aiClassificationService = aiClassificationService;
    }

    /// <summary>
    /// Phân tích hình ảnh/video hiện trường độc lập bằng AI Vision-Language (Qwen2.5-VL)
    /// </summary>
    /// <remarks>
    /// Endpoint phục vụ kiểm thử mô hình và đánh giá bộ dữ liệu nghiên cứu (Research Benchmark).
    /// Luôn đảm bảo trả về kết quả an toàn với cơ chế Fallback (Severity = 0) nếu có lỗi.
    /// </remarks>
    /// <param name="request">Chứa MediaUrl và bối cảnh sự cố</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>AiClassificationResultDto gồm nhãn nguy cơ, điểm severity 0-5, tóm tắt và metadata</returns>
    [HttpPost("analyze")]
    [ProducesResponseType(typeof(ApiResponseDto<AiClassificationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<AiClassificationResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeMedia([FromBody] AiAnalyzeRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MediaUrl))
        {
            return BadRequest(ApiResponseDto<AiClassificationResultDto>.FailureResult("MediaUrl không được để trống."));
        }

        var result = await _aiClassificationService.AnalyzeAsync(request.MediaUrl, request.AdditionalContext, cancellationToken);
        var message = result.IsSuccess
            ? "Phân tích hiện trường bằng AI hoàn tất thành công."
            : "AI gặp sự cố kết nối hoặc lỗi xử lý, hệ thống đã kích hoạt cơ chế Fallback an toàn (Mức độ: Unclassified).";

        return Ok(ApiResponseDto<AiClassificationResultDto>.SuccessResult(result, message));
    }
}
