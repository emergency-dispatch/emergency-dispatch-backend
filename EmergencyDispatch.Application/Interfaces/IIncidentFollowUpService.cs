using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Xử lý sau khi tiếp nhận sự cố: đổi mức độ sau xác minh, gom / tách báo cáo trùng, người báo bổ sung thông tin.
/// Mọi thao tác đều ghi IncidentAuditTrail.
/// </summary>
public interface IIncidentFollowUpService
{
    // ---------- B. Đổi mức độ sau khi xác minh ----------
    Task<ApiResponseDto<IncidentResponseDto>> ChangeSeverityAsync(Guid incidentId, ChangeSeverityDto dto, Guid actorUserId, CancellationToken cancellationToken = default);

    // ---------- C. Gom / tách báo cáo trùng ----------

    /// <summary>
    /// Tìm sự cố gốc để gom SOS mới (chưa lưu). null = không gom (tắt tính năng / không có ứng viên phù hợp)
    /// </summary>
    Task<(Incident Parent, double DistanceMeters)?> FindLinkParentAsync(Incident newReport, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sau khi báo cáo đã lưu ở trạng thái Linked: tăng bộ đếm sự cố gốc, ghi audit, đẩy realtime, trả tóm tắt sự cố gốc cho người báo
    /// </summary>
    Task<LinkedToIncidentDto> OnReportLinkedAsync(Incident report, Incident parent, double distanceMeters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra nhanh ảnh của báo cáo được gom (YOLO); khác loại sự cố gốc → tự tách và chạy AI đầy đủ
    /// </summary>
    Task HandleQuickCheckAsync(AiAnalysisJob job, CancellationToken cancellationToken = default);

    Task<ApiResponseDto<List<LinkedReportDto>>> GetLinkedReportsAsync(Guid parentIncidentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tách báo cáo khỏi sự cố gốc: Operator / Admin, người báo (tài khoản) hoặc khách (mã guestCancellationToken)
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> UnlinkAsync(Guid reportId, UnlinkIncidentDto dto, Guid? actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator xác nhận báo cáo trùng: đóng báo cáo (Cancelled, lý do Duplicate), giữ liên kết tới sự cố gốc
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> ConfirmDuplicateAsync(Guid reportId, string? note, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sự cố gốc bị hủy → các báo cáo đang gom quay lại hàng đợi (giữ thời điểm báo gốc)
    /// </summary>
    Task<int> RestoreLinkedReportsAsync(Guid parentIncidentId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sự cố gốc hoàn tất → đóng các báo cáo đang gom (Cancelled, lý do Duplicate, ghi chú "đã xử lý cùng sự cố gốc")
    /// </summary>
    Task<int> CloseLinkedReportsAsync(Guid parentIncidentId, Guid? actorUserId, CancellationToken cancellationToken = default);

    // ---------- D. Người báo bổ sung thông tin ----------
    Task<ApiResponseDto<IncidentSupplementDto>> AddSupplementAsync(Guid incidentId, AddSupplementDto dto, Guid? actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thông tin bổ sung của sự cố và của các báo cáo đã gom vào nó
    /// </summary>
    Task<ApiResponseDto<List<IncidentSupplementDto>>> GetSupplementsAsync(Guid incidentId, Guid? actorUserId, CancellationToken cancellationToken = default);
}
