using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// Controller kiểm tra và chuyển đổi ngôn ngữ (Bilingual vi/en)
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class LocalizationController : ControllerBase
{
    private readonly ILocalizationService _localizer;

    public LocalizationController(ILocalizationService localizer)
    {
        _localizer = localizer;
    }

    /// <summary>
    /// Kiểm tra ngôn ngữ hiện tại của request dựa trên header Accept-Language hoặc query ?culture=
    /// </summary>
    [HttpGet("info")]
    [ProducesResponseType(typeof(ApiResponseDto<object>), StatusCodes.Status200OK)]
    public IActionResult GetLanguageInfo()
    {
        var data = new
        {
            CurrentCulture = _localizer.CurrentCulture,
            IsEnglish = _localizer.IsEnglish,
            SupportedLanguages = new[] { "vi", "en" },
            SampleMessages = new
            {
                Success = _localizer.Get("Thành công"),
                InvalidData = _localizer.Get("Dữ liệu không hợp lệ"),
                AuthFailed = _localizer.Get("Tài khoản hoặc mật khẩu không chính xác."),
                EmailInUse = _localizer.Get("Email này đã được sử dụng trong hệ thống."),
                SosSubmitted = _localizer.Get("Gửi yêu cầu cứu hộ khẩn cấp thành công.")
            }
        };

        return Ok(ApiResponseDto<object>.Ok(data, "Thành công"));
    }
}
