namespace EmergencyDispatch.API.Realtime;

/// <summary>
/// Tên nhóm và tên sự kiện SignalR dùng chung giữa Backend và các Client (Web Operator / Mobile)
/// </summary>
public static class RealtimeContract
{
    // ===== Groups =====
    public const string OperatorsGroup = "operators";
    public const string RescuersGroup = "rescuers";
    public const string AdminsGroup = "admins";

    public static string IncidentGroup(Guid incidentId) => $"incident:{incidentId}";
    public static string StationGroup(string stationId) => $"station:{stationId}";

    /// <summary>Operator phụ trách một trạm (mỗi Operator quản lý 1 trạm)</summary>
    public static string OperatorStationGroup(Guid stationId) => $"operators:station:{stationId}";

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

    /// <summary>Cảnh báo khẩn cấp leo thang sự cố quá hạn phản hồi (gửi tới Operator và Admin kèm âm thanh báo động)</summary>
    public const string ReceiveEscalationAlert = "ReceiveEscalationAlert";

    /// <summary>Thông báo cá nhân mới (gửi riêng tới từng người dùng theo userId)</summary>
    public const string ReceiveNotification = "ReceiveNotification";

    /// <summary>Tài khoản bị khóa / xóa / đổi quyền / đổi trạm: client phải đăng xuất ngay. Payload: { reason }</summary>
    public const string ForceLogout = "ForceLogout";

    /// <summary>SOS nằm ngoài khu vực phục vụ (gửi Operator trạm gần nhất + Admin)</summary>
    public const string ReceiveOutOfAreaAlert = "ReceiveOutOfAreaAlert";

    /// <summary>Xe của trạm được điều đi hỗ trợ sự cố của trạm khác (gửi Operator trạm có xe)</summary>
    public const string ReceiveSupportDispatched = "ReceiveSupportDispatched";

    /// <summary>Sự cố được chuyển trạm phụ trách (gửi Operator trạm cũ, trạm mới, Admin)</summary>
    public const string ReceiveIncidentTransferred = "ReceiveIncidentTransferred";

    /// <summary>Xe đổi trạng thái (Available, Dispatched, CoolDown, Maintenance...)</summary>
    public const string ReceiveRescueUnitStatus = "ReceiveRescueUnitStatus";

    /// <summary>Khóa mềm sự cố bị lấy / gia hạn / nhả (lockedByOperatorId = null → đã nhả)</summary>
    public const string ReceiveIncidentLock = "ReceiveIncidentLock";
}
