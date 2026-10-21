using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.Escalation;
using EmergencyDispatch.Application.DTOs.Incident;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Kênh đẩy sự kiện thời gian thực (triển khai bằng SignalR ở tầng API).
/// Application chỉ phụ thuộc interface này để giữ đúng Clean Architecture.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>
    /// Sự cố SOS mới được tạo → đẩy tới nhóm Operator (Live Map / Queue)
    /// </summary>
    Task IncidentCreatedAsync(IncidentResponseDto incident, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sự cố được cập nhật (AI phân tích xong, Operator xác minh, hủy...) → đẩy tới Operator và người báo đang theo dõi
    /// </summary>
    Task IncidentUpdatedAsync(IncidentResponseDto incident, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lệnh điều phối mới được phát → đẩy tới đội xe cứu hộ và trạm trực thuộc
    /// </summary>
    Task JobAssignedAsync(DispatchAssignmentResponseDto assignment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tiến độ phân công cứu hộ thay đổi → đẩy tới Operator và người báo đang theo dõi
    /// </summary>
    Task AssignmentStatusChangedAsync(DispatchAssignmentResponseDto assignment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Điểm nghẽn giao thông mới được báo cáo → phát tới Operators và Rescuers (để điều hướng né kẹt xe)
    /// </summary>
    Task TrafficHotspotReportedAsync(EmergencyDispatch.Application.DTOs.Traffic.TrafficHotspotResponseDto hotspot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cảnh báo leo thang khẩn cấp (Sự cố Level 3/4 chưa được điều phối hoặc tiếp nhận sau thời gian quy định)
    /// </summary>
    Task EscalationAlertAsync(EscalationAlertDto alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thông báo cá nhân vừa được tạo (gửi riêng tới người dùng)
    /// </summary>
    Task NotificationCreatedAsync(Guid userId, EmergencyDispatch.Application.DTOs.Notification.NotificationDto notification, CancellationToken cancellationToken = default);
}
