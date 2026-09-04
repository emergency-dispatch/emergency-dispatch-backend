using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// DTO trả về thông tin đầy đủ của một sự cố khẩn cấp
/// </summary>
public class IncidentResponseDto
{
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

    public List<IncidentMediaDto> MediaItems { get; set; } = new();
    public AiClassificationResultDto? AiClassification { get; set; }
}
