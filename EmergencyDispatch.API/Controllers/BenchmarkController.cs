using EmergencyDispatch.Application.DTOs.Benchmark;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Số liệu thực nghiệm phục vụ khóa luận (RQ2: so sánh thuật toán điều phối, RQ3: hiệu quả AI hỗ trợ điều phối)
/// </summary>
[ApiController]
[Route("api/benchmark")]
[Authorize(Roles = "Admin,Operator")]
public class BenchmarkController : ControllerBase
{
    private readonly ITelemetryService _telemetryService;

    public BenchmarkController(ITelemetryService telemetryService)
    {
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// T_AI, T_Operator (có AI / thủ công), T_Dispatch, T_Response (theo thuật toán), T_Resolution và độ đồng thuận AI–Operator (Accuracy, Cohen's Kappa, ma trận nhầm lẫn)
    /// </summary>
    /// <param name="from">Lọc sự cố tạo từ thời điểm (UTC)</param>
    /// <param name="to">Lọc sự cố tạo đến thời điểm (UTC)</param>
    /// <param name="cancellationToken">Token hủy yêu cầu</param>
    [HttpGet("telemetry-summary")]
    [ProducesResponseType(typeof(ApiResponseDto<TelemetrySummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTelemetrySummary([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
        => Ok(ApiResponseDto<TelemetrySummaryDto>.Ok(await _telemetryService.GetSummaryAsync(from, to, cancellationToken)));
}
