using System.ComponentModel.DataAnnotations;

namespace EmergencyDispatch.Application.DTOs.User;

public class UpdateFcmTokenDto
{
    [Required(ErrorMessage = "FCM Token không được để trống")]
    public string FcmToken { get; set; } = string.Empty;
}
