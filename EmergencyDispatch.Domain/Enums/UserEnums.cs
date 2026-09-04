namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Vai trò người dùng trong hệ thống
/// </summary>
public enum UserRole
{
    Citizen = 0,      // Người dân báo SOS
    Operator = 1,     // Điều phối viên trung tâm chỉ huy
    RescueStaff = 2,  // Nhân viên đội cứu hộ cơ động
    Admin = 3         // Quản trị viên hệ thống
}

/// <summary>
/// Trạng thái hoạt động của tài khoản
/// </summary>
public enum UserStatus
{
    Active = 0,       // Đang hoạt động
    Inactive = 1,     // Chưa kích hoạt / Tạm ngưng
    Suspended = 2     // Bị khóa tài khoản
}
