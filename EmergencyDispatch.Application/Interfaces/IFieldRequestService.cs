using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Yêu cầu từ hiện trường: đội cứu hộ xin chi viện (Backup) hoặc báo vấn đề khác (Other) về Operator.
/// Mọi thao tác ghi IncidentAuditTrail; quá ngưỡng chưa xử lý thì báo lên Admin.
/// </summary>
public interface IFieldRequestService
{
    Task<ApiResponseDto<FieldRequestDto>> CreateAsync(Guid assignmentId, CreateFieldRequestDto dto, Guid staffUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yêu cầu đang mở (Open / Acknowledged). Operator chỉ thấy yêu cầu thuộc trạm mình
    /// </summary>
    Task<ApiResponseDto<List<FieldRequestDto>>> GetOpenAsync(CancellationToken cancellationToken = default);

    Task<ApiResponseDto<List<FieldRequestDto>>> GetByIncidentAsync(Guid incidentId, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<ApiResponseDto<FieldRequestDto>> AcknowledgeAsync(Guid requestId, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<ApiResponseDto<FieldRequestDto>> FulfillAsync(Guid requestId, string? note, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<ApiResponseDto<FieldRequestDto>> DeclineAsync(Guid requestId, string? reason, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Staff tự hủy yêu cầu của đội mình (không cần nữa)
    /// </summary>
    Task<ApiResponseDto<FieldRequestDto>> CancelAsync(Guid requestId, string? reason, Guid staffUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra trước khi Operator giao thêm xe theo yêu cầu (dùng ở DispatchService). null = hợp lệ
    /// </summary>
    Task<string?> ValidateForDispatchAsync(Guid requestId, Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đánh dấu đã đáp ứng sau khi giao xe thành công theo yêu cầu
    /// </summary>
    Task MarkFulfilledByDispatchAsync(Guid requestId, IReadOnlyCollection<string> plates, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Escalation daemon: tự hủy yêu cầu của sự cố đã đóng; yêu cầu quá ngưỡng → báo Admin + Operator. Trả số yêu cầu được báo
    /// </summary>
    Task<int> ProcessOverdueAsync(CancellationToken cancellationToken = default);
}
