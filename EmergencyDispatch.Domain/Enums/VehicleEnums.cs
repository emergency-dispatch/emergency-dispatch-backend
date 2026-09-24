namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Loại phương tiện cứu hộ
/// </summary>
public enum VehicleType
{
    Ambulance = 0,     // Xe cấp cứu / cứu thương
    FireTruck = 1,     // Xe chữa cháy
    PoliceVehicle = 2, // Xe tuần tra / cảnh sát
    RescueBoat = 3,    // Ca nô / tàu cứu hộ đường thủy
    Other = 4          // Phương tiện chuyên dụng khác
}

/// <summary>
/// Trạng thái sẵn sàng hoạt động của phương tiện
/// </summary>
public enum VehicleStatus
{
    Available = 0,    // Sẵn sàng nhận nhiệm vụ
    OnMission = 1,    // Đang thực hiện nhiệm vụ
    Maintenance = 2,  // Đang bảo dưỡng / sửa chữa
    OutOfService = 3  // Tạm dừng hoạt động
}
