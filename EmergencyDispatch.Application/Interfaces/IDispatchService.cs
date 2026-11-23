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
        string? unitType = null,
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

    /// <summary>
    /// AI tự điều phối: phát lệnh không có Operator thao tác (sự cố đã được hệ thống xác minh)
    /// </summary>
    Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> AutoAssignAsync(AssignDispatchDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quy tắc loại xe áp dụng cho loại sự cố của sự cố này (rỗng = chưa có quy tắc)
    /// </summary>
    Task<IReadOnlyList<EmergencyDispatch.Domain.Entities.IncidentTypeUnitRule>> GetUnitRulesAsync(EmergencyDispatch.Domain.Entities.Incident incident, CancellationToken cancellationToken);

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
    /// Nhân viên cứu hộ thuộc tổ nhận lượt này (lượt không có tổ: nhân viên trạm của xe) hoặc đã nộp báo cáo nghiệm thu của lượt
    /// — được xem lại lượt của chính mình bất cứ lúc nào (khớp my-assignments)
    /// </summary>
    /// <summary>
    /// Tuyến đường bộ + các bước rẽ từ vị trí xe (hoặc fromLat/fromLng do app gửi) tới hiện trường của lượt điều phối.
    /// Backend gọi OSRM nội bộ; máy chủ chỉ đường lỗi → đoạn thẳng ước lượng (IsApproximate). Quyền xem do controller kiểm tra
    /// </summary>
    Task<ApiResponseDto<AssignmentRouteDto>> GetAssignmentRouteAsync(Guid assignmentId, double? fromLat, double? fromLng, CancellationToken cancellationToken = default);

    Task<bool> IsCrewOfAssignmentAsync(Guid assignmentId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các lượt điều phối của một sự cố
    /// </summary>
    Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> GetAssignmentsByIncidentIdAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhiệm vụ của đội người dùng đang đăng nhập (tổ nhận lệnh lưu trên lượt, kể cả xe / tổ đã xóa mềm).
    /// scope: active = đang chạy (nhẹ, dùng khi polling), history = đã kết thúc, all/null = cả hai.
    /// Danh sách có lịch sử giới hạn 50 lượt mới nhất; xem đầy đủ qua GetMyAssignmentHistoryAsync
    /// </summary>
    Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> GetMyAssignmentsAsync(
        Guid rescuerUserId,
        string? scope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lịch sử nhiệm vụ đã kết thúc của đội (phân trang, mới nhất trước, lọc theo thời điểm điều phối)
    /// </summary>
    Task<ApiResponseDto<PaginatedResultDto<DispatchAssignmentResponseDto>>> GetMyAssignmentHistoryAsync(
        Guid rescuerUserId,
        MyAssignmentHistoryFilterDto filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lịch sử báo cáo nghiệm thu: Admin toàn bộ (lọc trạm tùy chọn), Operator trạm mình, nhân viên cứu hộ tổ mình / do mình nộp
    /// </summary>
    Task<ApiResponseDto<PaginatedResultDto<CompletionReportHistoryItemDto>>> GetCompletionReportsAsync(
        CompletionReportFilterDto filter,
        Guid viewerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chi tiết một báo cáo nghiệm thu (cùng phạm vi xem với lịch sử)
    /// </summary>
    Task<ApiResponseDto<CompletionReportHistoryItemDto>> GetCompletionReportByIdAsync(
        Guid id,
        Guid viewerUserId,
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

    /// <summary>
    /// onlyAcceptedUnits = true (người dân): bỏ các xe tổ chưa nhận lệnh (Dispatched) vì tổ có thể từ chối
    /// </summary>
    Task<ApiResponseDto<CitizenTrackingResponseDto>> GetCitizenTrackingAsync(
        Guid incidentId,
        bool onlyAcceptedUnits,
        CancellationToken cancellationToken = default);
}
