using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Report;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Lịch sử sự cố đã đóng và xuất báo cáo CSV / PDF
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>
    /// Lịch sử sự cố đã đóng (Completed / Cancelled), lọc theo khoảng thời gian báo cáo, trạm, mức độ, từ khóa; phân trang
    /// </summary>
    [HttpGet("incidents")]
    [ProducesResponseType(typeof(ApiResponseDto<PaginatedResultDto<IncidentHistoryItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetIncidentHistory([FromQuery] IncidentHistoryFilterDto filter, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _reportService.GetIncidentHistoryAsync(filter, cancellationToken);
            return Ok(ApiResponseDto<PaginatedResultDto<IncidentHistoryItemDto>>.Ok(result, "Lấy lịch sử sự cố thành công."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Số liệu tổng hợp theo cùng bộ lọc (tổng, hoàn thành, hủy, theo mức độ, thời gian phản ứng / xử lý trung bình)
    /// </summary>
    [HttpGet("incidents/summary")]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentReportSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetIncidentSummary([FromQuery] IncidentHistoryFilterDto filter, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _reportService.GetIncidentSummaryAsync(filter, cancellationToken);
            return Ok(ApiResponseDto<IncidentReportSummaryDto>.Ok(result, "Lấy số liệu tổng hợp thành công."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Xuất báo cáo sự cố đã đóng ra file CSV hoặc PDF (tối đa 5000 dòng), cùng bộ lọc với /incidents
    /// </summary>
    /// <param name="filter">Cùng bộ lọc với GET /api/reports/incidents (bỏ qua phân trang)</param>
    /// <param name="format">Csv | Pdf</param>
    /// <param name="cancellationToken">Hủy khi client ngắt kết nối</param>
    [HttpGet("incidents/export")]
    [Produces("text/csv", "application/pdf", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportIncidents([FromQuery] IncidentHistoryFilterDto filter, [FromQuery] ReportFormat format = ReportFormat.Csv, CancellationToken cancellationToken = default)
    {
        try
        {
            var file = await _reportService.ExportIncidentsAsync(filter, format, cancellationToken);
            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponseDto<object>.Fail(ex.Message));
        }
    }
}
