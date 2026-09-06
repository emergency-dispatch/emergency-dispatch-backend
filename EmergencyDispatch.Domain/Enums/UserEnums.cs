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

/// <summary>
/// Giới tính
/// </summary>
public enum Gender
{
    Male = 0,         // Nam
    Female = 1,       // Nữ
    Other = 2         // Khác
}

/// <summary>
/// Nhóm máu y tế phục vụ cấp cứu
/// </summary>
public enum BloodType
{
    Unknown = 0,      // Chưa xác định
    A_Positive = 1,   // A+
    A_Negative = 2,   // A-
    B_Positive = 3,   // B+
    B_Negative = 4,   // B-
    AB_Positive = 5,  // AB+
    AB_Negative = 6,  // AB-
    O_Positive = 7,   // O+
    O_Negative = 8    // O-
}
