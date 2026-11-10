namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Mã các loại phương tiện hệ thống (seed sẵn trong bảng RescueUnitTypes, không xóa được).
/// Loại xe là danh mục do Admin quản lý: RescueUnit.UnitType lưu mã (Code) của một dòng RescueUnitTypes,
/// Admin có thể thêm mã mới ngoài các mã dưới đây. Giữ đúng tên enum cũ để JSON API không đổi.
/// </summary>
public static class RescueUnitType
{
    /// <summary>Xe cấp cứu y tế 115</summary>
    public const string Ambulance = "Ambulance";

    /// <summary>Xe chữa cháy tiêu chuẩn</summary>
    public const string FireTruck = "FireTruck";

    /// <summary>Xe thang chữa cháy cứu nạn nhà cao tầng</summary>
    public const string LadderTruck = "LadderTruck";

    /// <summary>Cano / Thuyền cứu nạn đường thủy</summary>
    public const string RescueBoat = "RescueBoat";

    /// <summary>Xe cứu nạn, cứu hộ đặc chủng (cắt kim loại, cẩu nâng)</summary>
    public const string HeavyRescueVehicle = "HeavyRescueVehicle";

    /// <summary>Xe tuần tra phản ứng nhanh</summary>
    public const string PolicePatrol = "PolicePatrol";

    /// <summary>Mã số cũ (enum int trước khi chuyển sang danh mục) → mã chuỗi; dùng cho migration</summary>
    public static readonly IReadOnlyDictionary<int, string> LegacyValues = new Dictionary<int, string>
    {
        [1] = Ambulance, [2] = FireTruck, [3] = LadderTruck, [4] = RescueBoat, [5] = HeavyRescueVehicle, [6] = PolicePatrol
    };
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
    Maintenance = 4,

    /// <summary>
    /// Hồi sức 5 phút sau khi nghiệm thu nhiệm vụ (BR-27), chưa nhận lệnh mới
    /// </summary>
    CoolDown = 5,

    /// <summary>
    /// Ngừng phục vụ do trạm trực thuộc bị xóa (BR-AD-02)
    /// </summary>
    OutOfService = 6
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
    Cancelled = 5,

    /// <summary>
    /// Đội xe từ chối nhận lệnh kèm lý do (BR-22)
    /// </summary>
    Rejected = 6
}
