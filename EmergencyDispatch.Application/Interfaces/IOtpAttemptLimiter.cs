namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Đếm số lần nhập sai OTP của từng mã (theo mục đích + email) để chống dò mã 6 số
/// </summary>
public interface IOtpAttemptLimiter
{
    /// <summary>
    /// Ghi nhận một lần nhập sai. Trả về true khi đã chạm giới hạn → mã phải bị vô hiệu
    /// </summary>
    bool RegisterFailure(string purpose, string email);

    /// <summary>
    /// Xóa bộ đếm (khi nhập đúng hoặc khi phát mã mới)
    /// </summary>
    void Reset(string purpose, string email);
}
