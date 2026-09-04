using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.Auth;

public class GoogleLoginDto
{
    [Required(ErrorMessage = "Google ID Token là bắt buộc")]
    public string IdToken { get; set; } = string.Empty;
}
