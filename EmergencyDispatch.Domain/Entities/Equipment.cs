using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Thiết bị cứu hộ thuộc một trạm, có thể đang được trang bị trên một xe của trạm đó
/// </summary>
public class Equipment : BaseEntity
{
    /// <summary>
    /// Tên thiết bị (vd: "Bình chữa cháy CO2 5kg", "Máy AED")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public EquipmentCategory Category { get; set; } = EquipmentCategory.Other;

    public EquipmentStatus Status { get; set; } = EquipmentStatus.Available;

    /// <summary>
    /// Số lượng cùng loại trong bản ghi (thiết bị tiêu hao quản lý theo lô)
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Trạm sở hữu thiết bị
    /// </summary>
    public Guid StationId { get; set; }
    public Station? Station { get; set; }

    /// <summary>
    /// Xe đang được trang bị thiết bị này (cùng trạm); null = cất tại trạm
    /// </summary>
    public Guid? RescueUnitId { get; set; }
    public RescueUnit? RescueUnit { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Lý do / ghi chú của lần đổi trạng thái gần nhất (vd: lý do đưa đi bảo trì)
    /// </summary>
    public string? StatusNote { get; set; }

    public DateTime? StatusChangedAt { get; set; }
}
