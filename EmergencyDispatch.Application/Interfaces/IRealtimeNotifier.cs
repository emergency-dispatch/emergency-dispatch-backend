using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.Escalation;
using EmergencyDispatch.Application.DTOs.Incident;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Kênh đẩy sự kiện thời gian thực (triển khai bằng SignalR ở tầng API).
/// Application chỉ phụ thuộc interface này để giữ đúng Clean Architecture.
/// Sự cố có trạm phụ trách → gửi Operator trạm đó + Admin; chưa có trạm → gửi mọi Operator.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>
    /// Sự cố SOS mới được tạo → Operator trạm phụ trách + Admin (Live Map / Queue)
    /// </summary>
    Task IncidentCreatedAsync(IncidentResponseDto incident, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sự cố được cập nhật (AI phân tích xong, Operator xác minh, hủy...) → Operator trạm phụ trách + Admin và người đang theo dõi
    /// </summary>
    Task IncidentUpdatedAsync(IncidentResponseDto incident, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lệnh điều phối mới → nhân viên trạm có xe, Operator trạm có xe, Operator trạm phụ trách + Admin
    /// </summary>
    Task JobAssignedAsync(DispatchAssignmentResponseDto assignment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tiến độ phân công thay đổi → Operator liên quan, nhân viên trạm có xe và người đang theo dõi sự cố
    /// </summary>
    Task AssignmentStatusChangedAsync(DispatchAssignmentResponseDto assignment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Điểm nghẽn giao thông mới được báo cáo → mọi Operator và nhân viên cứu hộ (để né kẹt xe)
    /// </summary>
    Task TrafficHotspotReportedAsync(EmergencyDispatch.Application.DTOs.Traffic.TrafficHotspotResponseDto hotspot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cảnh báo leo thang: cấp 1 → Operator trạm phụ trách + Admin; cấp 2 → mọi Operator (BR-17)
    /// </summary>
    Task EscalationAlertAsync(EscalationAlertDto alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thông báo cá nhân vừa được tạo (gửi riêng tới người dùng)
    /// </summary>
    Task NotificationCreatedAsync(Guid userId, EmergencyDispatch.Application.DTOs.Notification.NotificationDto notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// SOS ngoài khu vực phục vụ → Operator trạm gần nhất + Admin
    /// </summary>
    Task OutOfAreaAlertAsync(OutOfAreaAlertDto alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xe của trạm được Operator trạm khác điều đi hỗ trợ → Operator trạm có xe
    /// </summary>
    Task SupportDispatchedAsync(Guid supportingStationId, DispatchAssignmentResponseDto assignment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sự cố chuyển trạm phụ trách → Operator trạm cũ, trạm mới và Admin
    /// </summary>
    Task IncidentTransferredAsync(IncidentTransferredDto transfer, IncidentResponseDto incident, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xe đổi trạng thái (giao xe, hoàn tất, hết CoolDown, đổi tay) → mọi Operator / Admin và nhân viên trạm có xe
    /// </summary>
    Task RescueUnitStatusChangedAsync(RescueUnitStatusEventDto unit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Khóa mềm sự cố bị lấy / gia hạn / nhả → Operator trạm phụ trách + Admin (FR-OP-02, BR-15)
    /// </summary>
    Task IncidentLockChangedAsync(IncidentLockEventDto lockEvent, Guid? responsibleStationId, CancellationToken cancellationToken = default);
}
