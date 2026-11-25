using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Models;

/// <summary>Minimal query projection; never loads reporter, media, medical or assignment details.</summary>
public sealed record CitizenMapIncident(
    Guid Id, double Latitude, double Longitude, string LocationAddress,
    IncidentEventType EventType, SeverityLevel Severity, IncidentStatus Status,
    DateTime ReportedAt, DateTime? UpdatedAt);

public sealed record CitizenMapBounds(double South, double West, double North, double East);
