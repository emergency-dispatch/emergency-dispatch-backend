using System.Collections.Concurrent;

namespace EmergencyDispatch.Application.Localization;

/// <summary>
/// Từ điển từ vựng và câu thông báo song ngữ (Vietnamese / English)
/// </summary>
public static class LocalizationCatalog
{
    // Từ điển ánh xạ Vietnamese -> English
    public static readonly Dictionary<string, string> ViToEn = new(StringComparer.OrdinalIgnoreCase)
    {
        // Thông báo chung
        { "Thành công", "Success" },
        { "Thất bại", "Failed" },
        { "Dữ liệu không hợp lệ", "Invalid data" },
        { "Lỗi hệ thống", "System error" },
        { "Không tìm thấy dữ liệu yêu cầu.", "Requested data not found." },
        { "Không có quyền thực hiện thao tác này.", "You do not have permission to perform this action." },
        { "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.", "Session is invalid or has expired." },

        // Xác thực & Tài khoản (Auth & User)
        { "Đăng ký tài khoản thành công. Mã xác thực (OTP) đã được gửi tới email của bạn.", "Account registered successfully. An OTP verification code has been sent to your email." },
        { "Đăng ký tài khoản thành công.", "Account registered successfully." },
        { "Email này đã được sử dụng trong hệ thống.", "This email is already in use in the system." },
        { "Email này đã tồn tại trong hệ thống.", "This email already exists in the system." },
        { "Không tìm thấy tài khoản tương ứng với email.", "Account corresponding to this email was not found." },
        { "Tài khoản đã được xác thực trước đó.", "Account has already been verified." },
        { "Tài khoản này đã được xác thực email trước đó.", "This account has already verified its email." },
        { "Mã xác thực không chính xác hoặc đã hết hạn.", "Verification code is invalid or has expired." },
        { "Mã xác thực không hợp lệ hoặc đã hết hạn.", "Verification code is invalid or has expired." },
        { "Xác thực email thành công. Chào mừng bạn gia nhập hệ thống!", "Email verification successful. Welcome to the system!" },
        { "Mã xác thực mới đã được gửi tới email của bạn.", "A new verification code has been sent to your email." },
        { "Nếu email tồn tại trong hệ thống, mã xác thực mới đã được gửi.", "If the email exists in the system, a new verification code has been sent." },
        { "Nếu email hợp lệ, hướng dẫn đặt lại mật khẩu đã được gửi tới hộp thư của bạn.", "If the email is valid, password reset instructions have been sent to your inbox." },
        { "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại với mật khẩu mới.", "Password reset successfully. Please log in with your new password." },
        { "Đăng nhập thành công.", "Login successful." },
        { "Tài khoản hoặc mật khẩu không chính xác.", "Invalid email or password." },
        { "Tài khoản của bạn đã bị khóa hoặc chưa kích hoạt.", "Your account is locked or inactive." },
        { "Tài khoản chưa được kích hoạt xác thực email.", "Account has not been verified via email." },
        { "Đổi mật khẩu thành công.", "Password changed successfully." },
        { "Mật khẩu hiện tại không chính xác.", "Current password is incorrect." },
        { "Mật khẩu mới không được trùng với mật khẩu cũ.", "New password cannot be the same as the old password." },
        { "Cập nhật thông tin thành công.", "Profile updated successfully." },
        { "Cập nhật hồ sơ y tế khẩn cấp thành công.", "Emergency medical profile updated successfully." },
        { "Không tìm thấy người dùng.", "User not found." },
        { "Đăng xuất thành công.", "Logged out successfully." },
        { "Làm mới token thành công.", "Token refreshed successfully." },
        { "Refresh token không hợp lệ hoặc đã hết hạn.", "Refresh token is invalid or has expired." },
        { "Vô hiệu hóa tài khoản thành công.", "Account deactivated successfully." },
        { "Kích hoạt tài khoản thành công.", "Account activated successfully." },

        // Sự cố & Cứu nạn (Incident & SOS)
        { "Gửi yêu cầu cứu hộ khẩn cấp thành công.", "Emergency SOS report submitted successfully." },
        { "Tạo báo cáo sự cố thành công.", "Incident report created successfully." },
        { "Không tìm thấy sự cố.", "Incident not found." },
        { "Cập nhật sự cố thành công.", "Incident updated successfully." },
        { "Xác minh sự cố thành công.", "Incident verified successfully." },
        { "Hủy sự cố thành công.", "Incident cancelled successfully." },
        { "Đóng hồ sơ sự cố thành công.", "Incident closed successfully." },
        { "Sự cố đã được đóng hoặc đã bị hủy.", "Incident is already closed or cancelled." },
        { "Danh sách sự cố được tải thành công.", "Incidents retrieved successfully." },

        // Điều phối & Trạm cứu hộ (Dispatch & Stations)
        { "Điều phối đội cứu hộ thành công.", "Rescue unit dispatched successfully." },
        { "Không tìm thấy phương tiện hoặc trạm cứu hộ phù hợp.", "No suitable rescue vehicle or station found." },
        { "Không tìm thấy nhiệm vụ điều phối.", "Dispatch assignment not found." },
        { "Cập nhật trạng thái nhiệm vụ thành công.", "Mission status updated successfully." },
        { "Nhiệm vụ đã hoàn thành.", "Mission completed." },
        { "Không thể xóa hoặc tạm ngưng trạm đang có xe thực hiện nhiệm vụ cứu nạn.", "Cannot delete or suspend a station with vehicles on active rescue missions." },
        { "Không thể xóa xe đang trong nhiệm vụ cứu hộ khẩn cấp.", "Cannot delete a vehicle that is on an active rescue mission." },
        { "Biển số xe đã tồn tại trong hệ thống.", "License plate already exists in the system." },
        { "Trạm cứu hộ đã tồn tại.", "Station already exists." },
        { "Không tìm thấy trạm cứu hộ.", "Station not found." },
        { "Không tìm thấy phương tiện cứu hộ.", "Rescue vehicle not found." },

        // Trí tuệ nhân tạo (AI & Classification)
        { "Phân tích AI thành công.", "AI analysis completed successfully." },
        { "Mô hình AI đang khởi động, vui lòng thử lại sau giây lát.", "AI model is initializing, please try again shortly." },
        { "Không đọc được hình ảnh.", "Unable to read image." },
        { "Cần cung cấp file ảnh hoặc đường link ảnh.", "An image file or image URL must be provided." },
        { "Mô hình AI trả về nội dung trống.", "AI model returned empty content." },
        { "Mối nguy hỏa hoạn", "Fire Hazard" },
        { "Tai nạn giao thông", "Traffic Accident" },
        { "Cứu nạn y tế", "Medical Rescue" }
    };

    // Từ điển ngược: English -> Vietnamese (tự động đảo chiều)
    public static readonly Dictionary<string, string> EnToVi = new(StringComparer.OrdinalIgnoreCase);

    static LocalizationCatalog()
    {
        foreach (var kv in ViToEn)
        {
            if (!EnToVi.ContainsKey(kv.Value))
            {
                EnToVi[kv.Value] = kv.Key;
            }
        }

        // Bổ sung thêm các từ khóa tiếng Anh thông dụng
        AddPair("Success", "Thành công");
        AddPair("Failed", "Thất bại");
        AddPair("Error", "Lỗi");
        AddPair("Not Found", "Không tìm thấy");
        AddPair("Unauthorized", "Không có quyền truy cập");
        AddPair("Forbidden", "Bị từ chối truy cập");
        AddPair("Internal Server Error", "Lỗi máy chủ nội bộ");
        AddPair("Bad Request", "Yêu cầu không hợp lệ");
    }

    private static void AddPair(string en, string vi)
    {
        if (!EnToVi.ContainsKey(en)) EnToVi[en] = vi;
        if (!ViToEn.ContainsKey(vi)) ViToEn[vi] = en;
    }
}
