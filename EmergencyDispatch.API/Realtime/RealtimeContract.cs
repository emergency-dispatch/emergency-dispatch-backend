namespace EmergencyDispatch.API.Realtime;

/// <summary>
/// Tên nhóm và tên sự kiện SignalR dùng chung giữa Backend và các Client (Web Operator / Mobile)
/// </summary>
public static class RealtimeContract
{
    // ===== Groups =====
    public const string OperatorsGroup = "operators";
    public const string RescuersGroup = "rescuers";

    public static string IncidentGroup(Guid incidentId) => $"incident:{incidentId}";
    public static string StationGroup(string stationId) => $"station:{stationId}";

    // ===== Client events (server → client) =====
    /// <summary>Sự cố SOS mới (gửi tới Operator)</summary>
    public const string ReceiveNewIncident = "ReceiveNewIncident";

    /// <summary>Sự cố được cập nhật đầy đủ (gửi tới Operator)</summary>
    public const string ReceiveIncidentUpdate = "ReceiveIncidentUpdate";

    /// <summary>Trạng thái rút gọn của sự cố (gửi tới người báo đang theo dõi, không kèm dữ liệu y tế)</summary>
    public const string ReceiveIncidentStatus = "ReceiveIncidentStatus";

    /// <summary>Vị trí realtime của đội cứu hộ</summary>
    public const string ReceiveRescueUnitLocation = "ReceiveRescueUnitLocation";

    /// <summary>Lệnh phân công cứu hộ mới (gửi tới Rescuers / Station)</summary>
    public const string ReceiveJobAssigned = "ReceiveJobAssigned";

    /// <summary>Cập nhật trạng thái lượt phân công (Accepted, EnRoute, OnScene, Completed)</summary>
    public const string ReceiveAssignmentStatus = "ReceiveAssignmentStatus";

    /// <summary>Điểm nghẽn giao thông mới hoặc giải tỏa trên Live Map</summary>
    public const string ReceiveTrafficHotspot = "ReceiveTrafficHotspot";
}
