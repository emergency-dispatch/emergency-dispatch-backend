using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Phương tiện / Đội cứu hộ (Xe cấp cứu, Xe chữa cháy, Cano...)
/// </summary>
public class RescueUnit : BaseEntity
{
    /// <summary>
    /// Biển số xe hoặc số hiệu định danh phương tiện (vd: 51A-115.89, CH-01)
    /// </summary>
    public string PlateNumber { get; set; } = string.Empty;

    /// <summary>
    /// Loại phương tiện / đội cứu hộ
    /// </summary>
    public RescueUnitType UnitType { get; set; } = RescueUnitType.Ambulance;

    /// <summary>
    /// Trạng thái hoạt động hiện tại
    /// </summary>
    public RescueUnitStatus Status { get; set; } = RescueUnitStatus.Available;

    /// <summary>
    /// Vĩ độ GPS thời gian thực hiện tại của xe
    /// </summary>
    public double CurrentLat { get; set; }

    /// <summary>
    /// Kinh độ GPS thời gian thực hiện tại của xe
    /// </summary>
    public double CurrentLng { get; set; }

    /// <summary>
    /// Thời điểm cập nhật tọa độ GPS gần nhất
    /// </summary>
    public DateTime? LastLocationUpdateAt { get; set; }

    /// <summary>
    /// Trạm cứu hộ trực thuộc
    /// </summary>
    public Guid StationId { get; set; }
    public Station? Station { get; set; }

    /// <summary>
    /// Danh sách các lượt phân công điều động qua các ca sự cố
    /// </summary>
    public ICollection<DispatchAssignment> DispatchAssignments { get; set; } = new List<DispatchAssignment>();

    /// <summary>
    /// Cập nhật tọa độ GPS trực tiếp của xe (phục vụ luồng WebSocket / SignalR)
    /// </summary>
    public void UpdateLocation(double lat, double lng)
    {
        CurrentLat = lat;
        CurrentLng = lng;
        LastLocationUpdateAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Cập nhật trạng thái hoạt động của xe
    /// </summary>
    public void UpdateStatus(RescueUnitStatus newStatus)
    {
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }
}
