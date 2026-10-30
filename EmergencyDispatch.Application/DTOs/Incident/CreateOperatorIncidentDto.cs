using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// Operator tự tạo sự cố khi biết tin từ bên ngoài (điện thoại, bộ đàm, camera, quan sát trực tiếp)
/// mà chưa có ai báo qua ứng dụng. Sự cố được xác minh ngay; có thể điều xe luôn trong cùng request.
/// </summary>
public class CreateOperatorIncidentDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string LocationAddress { get; set; } = string.Empty;

    /// <summary>
    /// Kênh biết tin (không được là CitizenApp)
    /// </summary>
    public IncidentSource Source { get; set; }

    /// <summary>
    /// FireExplosion hoặc TrafficAccident — quyết định loại xe được gợi ý
    /// </summary>
    public IncidentEventType EventType { get; set; }

    /// <summary>
    /// Level1–Level3, do Operator đánh giá
    /// </summary>
    public SeverityLevel Severity { get; set; }

    /// <summary>
    /// Căn cứ đánh giá / nội dung tin báo (bắt buộc, giống khi Operator tự đánh giá ca chưa phân loại)
    /// </summary>
    public string OperatorNotes { get; set; } = string.Empty;

    /// <summary>
    /// Người báo tin qua điện thoại / bộ đàm (nếu có)
    /// </summary>
    public string? ReporterName { get; set; }
    public string? ReporterPhone { get; set; }

    public List<string> MediaUrls { get; set; } = new();

    /// <summary>
    /// Điều xe luôn khi tạo (tùy chọn). Bỏ trống thì điều xe sau qua POST /api/dispatch/assign
    /// </summary>
    public List<Guid>? RescueUnitIds { get; set; }
    public DispatchAlgorithm? Algorithm { get; set; }
    public string? DispatchNotes { get; set; }
}

/// <summary>
/// Kết quả tạo sự cố bởi Operator: sự cố đã xác minh + kết quả điều xe (nếu có yêu cầu)
/// </summary>
public class OperatorIncidentResultDto
{
    public IncidentResponseDto Incident { get; set; } = new();
    public List<DispatchAssignmentResponseDto> Assignments { get; set; } = new();

    /// <summary>
    /// null = không yêu cầu điều xe; true/false = kết quả điều xe
    /// </summary>
    public bool? DispatchSucceeded { get; set; }

    /// <summary>
    /// Lý do điều xe thất bại (VD xe vừa được giao cho sự cố khác). Sự cố vẫn được tạo
    /// </summary>
    public string? DispatchMessage { get; set; }
}
