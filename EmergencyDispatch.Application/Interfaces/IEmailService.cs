namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Dịch vụ gửi email thông báo và bảo mật tài khoản
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Gửi email xác thực tài khoản kèm mã OTP sau khi đăng ký
    /// </summary>
    Task SendVerificationEmailAsync(string toEmail, string fullName, string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi email chào mừng (Welcome) sau khi kích hoạt tài khoản thành công
    /// </summary>
    Task SendWelcomeEmailAsync(string toEmail, string fullName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi email chứa mã OTP/Token đặt lại mật khẩu khi người dùng bấm Quên mật khẩu
    /// </summary>
    Task SendPasswordResetEmailAsync(string toEmail, string fullName, string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi email cảnh báo bảo mật thời gian thực khi mật khẩu tài khoản được thay đổi thành công
    /// </summary>
    Task SendPasswordChangedAlertAsync(string toEmail, string fullName, DateTime changedAt, string? ipAddress = null, CancellationToken cancellationToken = default);
}
