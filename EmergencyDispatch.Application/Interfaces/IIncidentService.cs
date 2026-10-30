using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Interfaces;

public interface IIncidentService
{
    /// <summary>
    /// Tạo sự cố mới từ báo cáo người dân, kích hoạt phân tích AI tự động và lưu trữ
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> CreateIncidentAsync(CreateIncidentDto dto, Guid? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator tự tạo sự cố khi biết tin từ bên ngoài (chưa có báo cáo qua app); sự cố được xác minh ngay
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> CreateByOperatorAsync(CreateOperatorIncidentDto dto, Guid operatorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết sự cố theo ID
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> GetIncidentByIdAsync(Guid id, Guid? viewerUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách sự cố có phân trang và bộ lọc
    /// </summary>
    Task<ApiResponseDto<PaginatedResultDto<IncidentResponseDto>>> GetIncidentsAsync(IncidentFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách hàng đợi sự cố cần Operator xác minh (sắp xếp ưu tiên theo Severity)
    /// </summary>
    Task<ApiResponseDto<List<IncidentResponseDto>>> GetQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator xác minh sự cố (Human-in-the-loop: điều chỉnh hoặc xác nhận mức độ từ AI)
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> VerifyIncidentAsync(Guid id, VerifyIncidentDto dto, Guid operatorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hủy sự cố (báo sai, báo khống, trùng lặp hoặc người dân/khách vãng lai tự hủy SOS)
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> CancelIncidentAsync(Guid id, string reason, Guid? cancelledByUserId = null, CancellationToken cancellationToken = default, string? guestCancellationToken = null, CancellationReason? reasonCode = null);

    /// <summary>
    /// Tự hủy sự cố chưa được xác minh sau maxAge (CancellationReason.AutoExpired). Trả số sự cố đã hủy.
    /// </summary>
    Task<int> AutoExpireStaleIncidentsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Giữ / gia hạn khóa mềm 2 phút khi Operator mở sự cố (FR-OP-02, BR-15). Người khác đang giữ → CONFLICT (MSG32)
    /// </summary>
    Task<ApiResponseDto<IncidentLockDto>> LockIncidentAsync(Guid id, Guid operatorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhả khóa mềm (Admin được nhả khóa của người khác)
    /// </summary>
    Task<ApiResponseDto<bool>> UnlockIncidentAsync(Guid id, Guid operatorId, bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-32: trả số điện thoại đầy đủ của người báo và ghi vết người xem (Operator/Admin, hoặc đội cứu hộ đang làm nhiệm vụ)
    /// </summary>
    Task<ApiResponseDto<EmergencyDispatch.Application.DTOs.Security.RevealPhoneDto>> RevealReporterPhoneAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-OP-03 / BR-05: đưa số điện thoại báo khống của sự cố vào danh sách chặn
    /// </summary>
    Task<ApiResponseDto<EmergencyDispatch.Application.DTOs.Security.BlockedNumberDto>> BlockReporterAsync(Guid id, EmergencyDispatch.Application.DTOs.Security.BlockReporterDto dto, Guid operatorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-CIT-03 / BR-CIT-05: người dùng có được xem vị trí xe / theo dõi sự cố này không
    /// </summary>
    Task<bool> CanAccessIncidentAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-CIT-05: lịch sử báo cáo của người dân kèm mốc thời gian xử lý
    /// </summary>
    Task<ApiResponseDto<PaginatedResultDto<MyReportDto>>> GetMyReportsAsync(Guid userId, MyReportFilter filter, int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhật ký kiểm toán bất biến (BR-03, BR-AD-06)
    /// </summary>
    Task<ApiResponseDto<PaginatedResultDto<AuditTrailEntryDto>>> GetAuditTrailAsync(AuditTrailFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xử lý một công việc phân tích AI từ hàng đợi nền: gọi AI, lưu kết quả, ghi Telemetry và đẩy realtime tới Operator
    /// </summary>
    Task ProcessAiAnalysisAsync(AiAnalysisJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuyển sự cố sang trạm khác phụ trách (sự cố sát ranh giới / trạm hiện tại quá tải)
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> TransferIncidentAsync(Guid id, TransferIncidentDto dto, Guid actorUserId, CancellationToken cancellationToken = default);
}
