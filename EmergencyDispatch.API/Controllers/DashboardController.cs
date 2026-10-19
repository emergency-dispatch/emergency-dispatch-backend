using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Dashboard;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Operator")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Lấy toàn bộ chỉ số thống kê tổng hợp (KPIs, Incidents, Phương tiện, Trạm, Nhân sự) cho Quản trị viên
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ApiResponseDto<AdminDashboardStatsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        var stats = await _dashboardService.GetAdminStatsAsync(cancellationToken);
        return Ok(ApiResponseDto<AdminDashboardStatsDto>.Ok(stats, "Lấy số liệu thống kê quản trị thành công."));
    }
}
