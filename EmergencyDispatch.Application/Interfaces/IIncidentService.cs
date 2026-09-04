using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;

namespace EmergencyDispatch.Application.Interfaces;

public interface IIncidentService
{
    /// <summary>
    /// Tạo sự cố mới từ báo cáo người dân, kích hoạt phân tích AI tự động và lưu trữ
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> CreateIncidentAsync(CreateIncidentDto dto, Guid? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy chi tiết sự cố theo ID
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> GetIncidentByIdAsync(Guid id, CancellationToken cancellationToken = default);

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
    /// Hủy sự cố (báo sai, báo khống, trùng lặp)
    /// </summary>
    Task<ApiResponseDto<IncidentResponseDto>> CancelIncidentAsync(Guid id, string reason, Guid operatorId, CancellationToken cancellationToken = default);
}
