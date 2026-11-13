using EmergencyDispatch.Application.DTOs.Catalog;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Danh mục loại phương tiện cứu hộ. Mọi tài khoản đăng nhập đọc được; chỉ Admin thêm / sửa / ẩn / xóa
/// </summary>
[ApiController]
[Route("api/rescue-unit-types")]
[Authorize]
public class RescueUnitTypesController : ControllerBase
{
    private readonly ITypeCatalogService _catalog;

    public RescueUnitTypesController(ITypeCatalogService catalog)
    {
        _catalog = catalog;
    }

    /// <summary>
    /// Danh sách loại phương tiện. includeInactive = true (chỉ Admin) để thấy cả loại đã ẩn
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponseDto<List<RescueUnitTypeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive, CancellationToken cancellationToken)
        => Ok(ApiResponseDto<List<RescueUnitTypeDto>>.SuccessResult(
            await _catalog.GetUnitTypesAsync(includeInactive && User.IsInRole("Admin"), cancellationToken)));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<RescueUnitTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<RescueUnitTypeDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] UpsertRescueUnitTypeDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _catalog.CreateUnitTypeAsync(dto, cancellationToken));

    /// <summary>
    /// Sửa tên / icon / thứ tự / ẩn hiện. Code không đổi được
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<RescueUnitTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<RescueUnitTypeDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertRescueUnitTypeDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _catalog.UpdateUnitTypeAsync(id, dto, cancellationToken));

    /// <summary>
    /// Xóa loại chưa từng được dùng (không phải loại hệ thống). Loại đang dùng → 409, hãy ẩn
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => this.ToHttpResult(await _catalog.DeleteUnitTypeAsync(id, cancellationToken));
}

/// <summary>
/// Danh mục loại sự cố và quy tắc loại xe theo loại sự cố. Danh sách công khai (form SOS của khách);
/// chỉ Admin thêm / sửa / ẩn / xóa và cấu hình quy tắc
/// </summary>
[ApiController]
[Route("api/incident-types")]
public class IncidentTypesController : ControllerBase
{
    private readonly ITypeCatalogService _catalog;

    public IncidentTypesController(ITypeCatalogService catalog)
    {
        _catalog = catalog;
    }

    /// <summary>
    /// Danh sách loại sự cố đang dùng (kèm quy tắc loại xe). includeInactive = true (chỉ Admin) để thấy cả loại đã ẩn
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponseDto<List<IncidentTypeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive, CancellationToken cancellationToken)
        => Ok(ApiResponseDto<List<IncidentTypeDto>>.SuccessResult(
            await _catalog.GetIncidentTypesAsync(includeInactive && User.IsInRole("Admin"), cancellationToken)));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentTypeDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] UpsertIncidentTypeDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _catalog.CreateIncidentTypeAsync(dto, cancellationToken));

    /// <summary>
    /// Sửa tên / nhóm / icon / thứ tự / ẩn hiện. Code không đổi được; loại hệ thống không đổi nhóm
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentTypeDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertIncidentTypeDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _catalog.UpdateIncidentTypeAsync(id, dto, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => this.ToHttpResult(await _catalog.DeleteIncidentTypeAsync(id, cancellationToken));

    /// <summary>
    /// Kế hoạch điều xe theo mức độ (Admin): mỗi Level gồm loại xe × số lượng, tổng ≤ 5.
    /// VD Level1: 1 xe chữa cháy; Level3: 2 xe chữa cháy + 1 xe thang + 1 xe cấp cứu.
    /// Dùng cho gợi ý xe, số xe chuẩn BR-14 và AI tự điều phối
    /// </summary>
    [HttpPut("{id:guid}/dispatch-plan")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<IncidentTypeDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateDispatchPlan(Guid id, [FromBody] UpdateDispatchPlanDto dto, CancellationToken cancellationToken)
        => this.ToHttpResult(await _catalog.UpdateDispatchPlanAsync(id, dto, cancellationToken));
}
