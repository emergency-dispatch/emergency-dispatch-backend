using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// DTO trả về thông tin đầy đủ của một sự cố khẩn cấp
/// </summary>
public class IncidentResponseDto
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? GuestCancellationToken { get; set; }

    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string LocationAddress { get; set; } = string.Empty;
    public IncidentStatus Status { get; set; }
    public SeverityLevel Severity { get; set; }

    public Guid? ReportedByUserId { get; set; }
    public string? ReporterName { get; set; }
    public string? ReporterPhone { get; set; }

    public Guid? VerifiedByUserId { get; set; }
    public string? VerifiedByUserName { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? OperatorNotes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Khóa mềm 2 phút (FR-OP-02, BR-15): Operator đang xử lý sự cố; null khi chưa ai khóa hoặc khóa đã hết hạn
    /// </summary>
    public Guid? LockedByOperatorId { get; set; }
    public string? LockedByOperatorName { get; set; }
    public DateTime? LockExpiresAt { get; set; }

    public List<IncidentMediaDto> MediaItems { get; set; } = new();
    public AiClassificationResultDto? AiClassification { get; set; }

    /// <summary>
    /// Hồ sơ y tế, thông tin cứu hộ căn hộ và người liên hệ khẩn cấp của người báo (nếu có tài khoản)
    /// </summary>
    public IncidentReporterMedicalDto? ReporterMedicalProfile { get; set; }

}
