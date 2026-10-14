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
}
