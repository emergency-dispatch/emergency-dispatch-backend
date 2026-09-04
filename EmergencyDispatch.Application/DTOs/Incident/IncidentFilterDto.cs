using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

public class IncidentFilterDto : PaginationParamsDto
{
    public IncidentStatus? Status { get; set; }
    public SeverityLevel? Severity { get; set; }
    public string? SearchTerm { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
