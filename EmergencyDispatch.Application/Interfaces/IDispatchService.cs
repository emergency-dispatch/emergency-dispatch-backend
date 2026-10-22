using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Dịch vụ Điều phối &amp; Tối ưu hóa phân bổ nguồn lực cứu hộ (Research RQ2: Nearest-Available vs Load-Aware)
/// </summary>
public interface IDispatchService
{
    /// <summary>
    /// Đề xuất danh sách phương tiện cứu hộ tối ưu cho sự cố (Hỗ trợ 2 thuật toán: Nearest-Available và Load-Aware)
    /// </summary>
    Task<ApiResponseDto<List<RecommendedUnitResponseDto>>> GetRecommendationsAsync(
        Guid incidentId,
        DispatchAlgorithm algorithm = DispatchAlgorithm.NearestAvailable,
        RescueUnitType? unitType = null,
        double? maxDistanceKm = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các phương tiện cứu hộ đang khả dụng trong bán kính R km
    /// </summary>
    Task<ApiResponseDto<List<RecommendedUnitResponseDto>>> GetAvailableUnitsNearbyAsync(
        Guid incidentId,
        double? maxDistanceKm = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Điều phối xe cứu hộ tới hiện trường sự cố
    /// </summary>
    /// <summary>
    /// Mọi lượt phân công đang chạy (Dispatched / Accepted / EnRoute / OnScene). Operator: sự cố trạm mình hoặc xe trạm mình
    /// </summary>
    Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> GetActiveAssignmentsAsync(CancellationToken cancellationToken = default);

    Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> AssignUnitAsync(
        AssignDispatchDto dto,
        Guid operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết thông tin lượt phân công
    /// </summary>
    Task<ApiResponseDto<DispatchAssignmentResponseDto>> GetAssignmentByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các lượt điều phối của một sự cố
    /// </summary>
    Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> GetAssignmentsByIncidentIdAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách nhiệm vụ phân công cho nhân viên / trạm của người dùng đang đăng nhập
    /// </summary>
    Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> GetMyAssignmentsAsync(
        Guid rescuerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật tiến độ nhiệm vụ (Accepted -> EnRoute -> OnScene)
    /// </summary>
    Task<ApiResponseDto<DispatchAssignmentResponseDto>> UpdateAssignmentStatusAsync(
        Guid id,
        UpdateAssignmentStatusDto dto,
        Guid rescuerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Nghiệm thu hoàn thành nhiệm vụ và đóng hồ sơ sự cố (Resolution Report &amp; Telemetry KPI T_Response)
    /// </summary>
    /// <summary>
    /// Đội cứu hộ từ chối nhiệm vụ kèm lý do bắt buộc (FR-RS-01, BR-22)
    /// </summary>
    Task<ApiResponseDto<DispatchAssignmentResponseDto>> RejectAssignmentAsync(
        Guid id,
        RejectAssignmentDto dto,
        Guid rescuerUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponseDto<DispatchAssignmentResponseDto>> ResolveAssignmentAsync(
        Guid id,
        ResolveDispatchDto dto,
        Guid rescuerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thông tin lộ trình đường di chuyển cứu hộ và vị trí xe thời gian thực dành cho Người dân (Citizen Live Tracking)
    /// </summary>
    Task<ApiResponseDto<CitizenTrackingResponseDto>> GetCitizenTrackingAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default);
}
