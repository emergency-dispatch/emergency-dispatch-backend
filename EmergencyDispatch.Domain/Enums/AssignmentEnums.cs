namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Trạng thái của một phân công nhiệm vụ cứu hộ
/// </summary>
public enum AssignmentStatus
{
    Pending = 0,   // Chờ staff tiếp nhận
    Accepted = 1,  // Staff đã nhận nhiệm vụ
    EnRoute = 2,   // Đang trên đường tới hiện trường
    OnScene = 3,   // Đã đến hiện trường
    Completed = 4, // Hoàn thành nhiệm vụ
    Rejected = 5   // Staff từ chối nhiệm vụ
}

/// <summary>
/// Lý do nhân viên từ chối tiếp nhận nhiệm vụ
/// </summary>
public enum RejectionReason
{
    VehicleBreakdown = 0,  // Xe/phương tiện hỏng hóc
    AlreadyOnMission = 1,  // Đang bận nhiệm vụ khẩn cấp khác
    TooFar = 2,            // Quá xa vùng tiếp cận
    MedicalLeave = 3,      // Lý do sức khỏe / nghỉ ốm
    NoResponse = 4,        // Không phản hồi (hệ thống tự động timeout)
    Other = 5              // Lý do khác
}
