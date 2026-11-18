using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.User;

public class UpdateFcmTokenDto
{
    [Required(ErrorMessage = "FCM Token không được để trống")]
    public string FcmToken { get; set; } = string.Empty;
}

/// <summary>
/// PUT /api/users/ui-language
/// </summary>
public class UpdateUiLanguageDto
{
    /// <summary>
    /// "vi" hoặc "en"
    /// </summary>
    [Required(ErrorMessage = "Ngôn ngữ không được để trống")]
    public string Language { get; set; } = string.Empty;
}
