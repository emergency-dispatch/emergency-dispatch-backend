namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Loại phương tiện / đội cứu hộ
/// </summary>
public enum RescueUnitType
{
    /// <summary>
    /// Xe cấp cứu y tế 115
    /// </summary>
    Ambulance = 1,

    /// <summary>
    /// Xe chữa cháy tiêu chuẩn
    /// </summary>
    FireTruck = 2,

    /// <summary>
    /// Xe thang chữa cháy cứu nạn nhà cao tầng
    /// </summary>
    LadderTruck = 3,

    /// <summary>
    /// Cano / Thuyền cứu nạn đường thủy
    /// </summary>
    RescueBoat = 4,

    /// <summary>
    /// Xe cứu nạn, cứu hộ đặc chủng (cắt kim loại, cẩu nâng)
    /// </summary>
    HeavyRescueVehicle = 5,

    /// <summary>
    /// Xe tuần tra phản ứng nhanh
    /// </summary>
    PolicePatrol = 6
}

/// <summary>
/// Trạng thái hoạt động của phương tiện / đội cứu hộ
/// </summary>
public enum RescueUnitStatus
{
    /// <summary>
    /// Sẵn sàng nhận lệnh điều phối tại trạm hoặc đang tuần tra rảnh
    /// </summary>
    Available = 0,

    /// <summary>
    /// Đã nhận lệnh điều phối, đang trên đường đến hiện trường (En route)
    /// </summary>
    Dispatched = 1,

    /// <summary>
    /// Đã tới hiện trường và đang tiến hành cứu nạn (On scene)
    /// </summary>
    OnScene = 2,

    /// <summary>
    /// Đã xử lý xong, đang trên đường quay về trạm
    /// </summary>
    Returning = 3,

    /// <summary>
    /// Tạm ngưng hoạt động để bảo trì, tiếp nhiên liệu
    /// </summary>
    Maintenance = 4
}

/// <summary>
/// Trạng thái của một lượt phân công điều động xe vào sự cố
/// </summary>
public enum DispatchAssignmentStatus
{
    /// <summary>
    /// Đã phát lệnh điều phối từ trung tâm
    /// </summary>
    Dispatched = 0,

    /// <summary>
    /// Đội xe đã xác nhận tiếp nhận lệnh (Accepted)
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// Đội xe đang di chuyển tới hiện trường (En route)
    /// </summary>
    EnRoute = 2,

    /// <summary>
    /// Đội xe đã tiếp cận hiện trường (On scene)
    /// </summary>
    OnScene = 3,

    /// <summary>
    /// Đội xe đã hoàn tất nhiệm vụ
    /// </summary>
    Completed = 4,

    /// <summary>
    /// Lệnh điều phối bị hủy (do điều xe khác hoặc báo sai)
    /// </summary>
    Cancelled = 5
}
