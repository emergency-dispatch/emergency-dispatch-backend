using System.Collections.Concurrent;

namespace EmergencyDispatch.Application.Localization;

/// <summary>
/// Từ điển từ vựng và câu thông báo song ngữ (Vietnamese / English)
/// </summary>
public static partial class LocalizationCatalog
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

        // Medical ID, Guest SOS and request validation (vi/en)
        { "Bạn cần đăng nhập để hủy sự cố.", "You must sign in to cancel an incident." },
        { "Bạn không có quyền hủy sự cố được báo cáo bởi người khác.", "You cannot cancel an incident reported by another user." },
        { "Đội cứu hộ đang trên đường tới vị trí của bạn. Bạn không thể tự hủy sự cố, vui lòng liên hệ Trung tâm điều phối qua số 115.", "Rescuers are on their way to your location. You can no longer cancel this incident yourself. Please contact the dispatch center at 115." },
        { "Sự cố đang được xử lý bởi {0}. Vui lòng chờ khóa hết hạn.", "This incident is being handled by {0}. Please wait for the lock to expire." },
        { "điều phối viên khác", "another operator" },
        { "Vĩ độ không hợp lệ", "Invalid latitude" },
        { "Kinh độ không hợp lệ", "Invalid longitude" },
        { "Địa chỉ hiện trường là bắt buộc", "Incident location address is required" },
        { "Có một hoặc nhiều lỗi xác thực dữ liệu.", "One or more validation errors occurred." },
        { "Không xác định được danh tính người dùng.", "Unable to identify the current user." },
        { "Lấy thông tin cá nhân thành công.", "Profile retrieved successfully." },
        { "Cập nhật thông tin cá nhân thành công.", "Profile updated successfully." },
        { "Không tìm thấy thông tin tài khoản.", "Account information not found." },
        { "Không thể cập nhật FCM Token.", "Unable to update the FCM token." },
        { "Cập nhật FCM Token thành công.", "FCM token updated successfully." },
        { "Không tìm thấy người dùng với Id = {0}", "User with Id = {0} was not found." },
        { "Không tìm thấy trạm cứu hộ với Id = {0}", "Rescue station with Id = {0} was not found." },
        { "Lấy danh sách người dùng thành công.", "Users retrieved successfully." },
        { "Lấy thông tin người dùng thành công.", "User information retrieved successfully." },
        { "Tạo người dùng thành công.", "User created successfully." },
        { "Cập nhật người dùng thành công.", "User updated successfully." },
        { "Không tìm thấy người dùng cần xóa.", "User to delete was not found." },
        { "Xóa người dùng thành công.", "User deleted successfully." },
        { "Lấy thông tin Tổ làm việc thành công.", "Team information retrieved successfully." },
        { "Phân công nhân viên vào trạm và tổ làm việc thành công.", "Staff member assigned to the station and team successfully." },
        { "Lấy danh sách nhân viên trạm thành công.", "Station staff retrieved successfully." },
        { "Họ và tên là bắt buộc", "Full name is required" },
        { "Họ và tên tối đa 100 ký tự", "Full name must not exceed 100 characters" },
        { "Số điện thoại không hợp lệ", "Invalid phone number" },
        { "Ngôn ngữ ưu tiên tối đa 50 ký tự", "Preferred language must not exceed 50 characters" },
        { "Ghi chú người phụ thuộc tối đa 1000 ký tự", "Dependents note must not exceed 1000 characters" },
        { "Số lượng thành viên trong gia đình không hợp lệ", "Household members count must be between 0 and 100" },
        { "Số điện thoại tối đa 20 ký tự", "Phone number must not exceed 20 characters" },
        { "Lý do hủy tối đa 500 ký tự", "Cancellation reason must not exceed 500 characters" },
        { "Mã hủy tối đa 2048 ký tự", "Cancellation token must not exceed 2048 characters" },
        { "Người dân/Khách báo hủy sự cố", "Citizen/Guest requested incident cancellation" },
        { "Người dùng tự hủy sự cố", "User requested incident cancellation" },
        { "Bạn chưa xác thực tài khoản.", "You have not authenticated your account." },
        { "Dữ liệu báo cáo sự cố không hợp lệ.", "Invalid incident report data." },
        { "Đã tiếp nhận SOS. AI đang phân tích mức độ nguy hiểm, kết quả sẽ được cập nhật realtime.", "SOS received. AI is assessing the severity; results will be updated in real time." },
        { "Đã tiếp nhận SOS. Điều phối viên sẽ đánh giá sự cố ngay.", "SOS received. An operator will assess the incident shortly." },
        { "Không tìm thấy sự cố với mã yêu cầu.", "No incident was found with the requested ID." },
        { "Lấy danh sách hàng đợi sự cố thành công.", "Incident queue retrieved successfully." },
        { "Không tìm thấy sự cố cần xác minh.", "Incident to verify was not found." },
        { "Sự cố đang ở trạng thái '{0}', không thể xác minh lại.", "Incident is in status '{0}' and cannot be verified again." },
        { "Xác minh sự cố thành công. Sự cố sẵn sàng để điều phối đội cứu hộ.", "Incident verified successfully. It is ready for rescue dispatch." },
        { "Không tìm thấy sự cố cần hủy.", "Incident to cancel was not found." },
        { "Không thể hủy sự cố đã hoàn tất xử lý hiện trường.", "An incident with completed on-site operations cannot be cancelled." },
        { "Sự cố này đã được hủy trước đó.", "This incident has already been cancelled." },
        { "Bạn không có quyền hủy sự cố này hoặc mã hủy đã hết hạn.", "You are not authorized to cancel this incident or the cancellation token has expired." },
        { "Đã hủy sự cố thành công.", "Incident cancelled successfully." },
        { "Đã hủy sự cố thành công và tự động phát lệnh thu hồi {0} xe cứu hộ quay về trạm.", "Incident cancelled successfully and {0} rescue vehicles were recalled to their stations." },
        { "Người dân/Khách vãng lai", "Citizen/Guest" },
        { "Người dùng {0}", "User {0}" },
        { "[THU HỒI XE]: Sự cố bị hủy bởi {0}. Lý do: {1}", "[VEHICLE RECALL]: Incident cancelled by {0}. Reason: {1}" },
        { "(Đã thu hồi {0} xe đang xuất phát)", "({0} dispatched vehicles recalled)" },
        { "[ĐÃ HỦY bởi {0}{1}]: {2}", "[CANCELLED by {0}{1}]: {2}" },
        { "Vĩ độ (Latitude) phải nằm trong khoảng -90 đến 90 độ.", "Latitude must be between -90 and 90 degrees." },
        { "Kinh độ (Longitude) phải nằm trong khoảng -180 đến 180 độ.", "Longitude must be between -180 and 180 degrees." },
        { "Địa chỉ hiện trường là bắt buộc.", "Incident location address is required." },
        { "Địa chỉ không được vượt quá 500 ký tự.", "Address must not exceed 500 characters." },
        { "Số điện thoại liên hệ không đúng định dạng Việt Nam.", "Contact phone number must be a valid Vietnamese phone number." },
        { "Không được đính kèm quá 5 tệp tin phương tiện cho một sự cố.", "An incident cannot have more than 5 media attachments." },
        { "Tiêu đề không được vượt quá 250 ký tự", "Title must not exceed 250 characters" },
        { "Mô tả không được vượt quá 2000 ký tự", "Description must not exceed 2000 characters" },
        { "Vĩ độ (Latitude) là bắt buộc", "Latitude is required" },
        { "Vĩ độ (Latitude) không hợp lệ", "Invalid latitude" },
        { "Kinh độ (Longitude) là bắt buộc", "Longitude is required" },
        { "Kinh độ (Longitude) không hợp lệ", "Invalid longitude" },
        { "Địa chỉ không được vượt quá 500 ký tự", "Address must not exceed 500 characters" },
        { "Tên người báo cáo không được vượt quá 100 ký tự", "Reporter name must not exceed 100 characters" },

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
        // Gộp từ điển các module vận hành (LocalizationCatalog.Operations.cs)
        foreach (var kv in OperationsViToEn)
        {
            ViToEn.TryAdd(kv.Key, kv.Value);
        }

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
