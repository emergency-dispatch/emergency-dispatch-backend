using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

public sealed class CitizenMapQueryDto
{
    public double? South { get; set; }
    public double? West { get; set; }
    public double? North { get; set; }
    public double? East { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

/// <summary>Public marker. Id identifies this marker, not permission to read a private incident.</summary>
public sealed class CitizenMapIncidentDto
{
    public Guid Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Area { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IncidentEventType EventType { get; set; }
    public SeverityLevel Severity { get; set; }
    public IncidentStatus Status { get; set; }
    public DateTime ReportedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Invalidation only. Clients refetch the authorized API, including after reconnect.</summary>
public sealed record CitizenMapChangedDto(string Revision, DateTime ChangedAt);
