using EmergencyDispatch.Application.Localization;
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
    private readonly IReportFileGenerator _fileGenerator;

    public BenchmarkController(ITelemetryService telemetryService, IReportFileGenerator fileGenerator)
    {
        _telemetryService = telemetryService;
        _fileGenerator = fileGenerator;
    }

    /// <summary>
    /// Dữ liệu từng sự cố cho phân tích RQ2 / RQ3: mốc AI, xác minh, điều xe, tới nơi, đóng; có ghi đè AI không; thuật toán điều phối
    /// </summary>
    /// <param name="from">Lọc sự cố tạo từ thời điểm (UTC)</param>
    /// <param name="to">Lọc sự cố tạo đến thời điểm (UTC)</param>
    /// <param name="format">Json (mặc định) hoặc Csv (tải file)</param>
    /// <param name="cancellationToken">Hủy khi client ngắt kết nối</param>
    [HttpGet("incidents")]
    [Produces("application/json", "text/csv")]
    [ProducesResponseType(typeof(ApiResponseDto<IReadOnlyList<TelemetryIncidentRowDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIncidentRows([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string format = "Json", CancellationToken cancellationToken = default)
    {
        if (from.HasValue && to.HasValue && from > to)
        {
            return BadRequest(ApiResponseDto<object>.Fail("Thời gian bắt đầu phải trước thời gian kết thúc."));
        }

        var rows = await _telemetryService.GetIncidentRowsAsync(from, to, cancellationToken);
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            return File(_fileGenerator.BuildTelemetryCsv(rows), "text/csv; charset=utf-8",
                $"telemetry-incidents_{DateTime.UtcNow.AddHours(7):yyyyMMdd_HHmm}.csv");
        }

        return Ok(ApiResponseDto<IReadOnlyList<TelemetryIncidentRowDto>>.Ok(rows, LocalizationHelper.Localize("{0} sự cố.", rows.Count)));
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
