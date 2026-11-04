namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Nhóm thiết bị cứu hộ
/// </summary>
public enum EquipmentCategory
{
    /// <summary>
    /// Chữa cháy: bình chữa cháy, lăng phun, vòi chữa cháy
    /// </summary>
    FireFighting = 0,

    /// <summary>
    /// Y tế: túi sơ cứu, máy AED, cáng
    /// </summary>
    Medical = 1,

    /// <summary>
    /// Cứu nạn: kìm cắt thủy lực, kích nâng, dây cứu sinh
    /// </summary>
    Rescue = 2,

    /// <summary>
    /// Bảo hộ: quần áo chống cháy, mặt nạ phòng độc, bình dưỡng khí
    /// </summary>
    Protective = 3,

    /// <summary>
    /// Liên lạc: bộ đàm, loa
    /// </summary>
    Communication = 4,

    Other = 5
}

/// <summary>
/// Trạng thái thiết bị (theo Phiếu đăng ký: Available / Maintenance)
/// </summary>
public enum EquipmentStatus
{
    /// <summary>
    /// Sẵn sàng sử dụng
    /// </summary>
    Available = 0,

    /// <summary>
    /// Đang bảo trì / hỏng, không mang theo nhiệm vụ
    /// </summary>
    Maintenance = 1
}
