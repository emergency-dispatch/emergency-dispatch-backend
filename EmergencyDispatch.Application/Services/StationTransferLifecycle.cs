using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Services;

/// <summary>Pure lifecycle rules shared by manual and automatic operations.</summary>
public static class StationTransferLifecycle
{
    public static bool IsWaitingStatus(IncidentStatus status) =>
        status is IncidentStatus.Pending or IncidentStatus.AiProcessed or IncidentStatus.Verified or IncidentStatus.Escalated;

    public static bool HasDispatchedWork(Incident incident) =>
        incident.Status is IncidentStatus.Dispatched or IncidentStatus.InProgress
        || incident.DispatchAssignments.Any(a => a.IsActive || a.Status == DispatchAssignmentStatus.Completed);

    public static void Acknowledge(Incident incident, Guid actorUserId, DateTime nowUtc)
    {
        if (!incident.ResponsibleStationId.HasValue || incident.StationAcknowledgedAt.HasValue) return;
        incident.StationAssignedAt ??= nowUtc;
        incident.StationTransferChainId ??= Guid.NewGuid();
        incident.StationAssignmentVersion = Math.Max(1, incident.StationAssignmentVersion);
        incident.StationAcknowledgedAt = nowUtc;
        incident.StationAcknowledgedByUserId = actorUserId;
        incident.UpdatedAt = nowUtc;
    }

    public static DateTime Deadline(DateTime assignedAtUtc, EscalationSettingsDto escalation, StationTransferSettingsDto settings) =>
        assignedAtUtc.AddMinutes(EscalationTiming.GetThresholdMinutes(assignedAtUtc, escalation) * settings.ResponseTimeoutMultiplier);

    public static string? AutomaticReason(Incident incident, Station? source, bool hasActiveOperator, DateTime nowUtc)
    {
        // Deadline is set only after AI/auto-dispatch finishes. Do not transfer legacy rows,
        // linked reports, incidents on a mission, or those still being analysed.
        if (!incident.StationAssignedAt.HasValue || !incident.StationResponseDueAt.HasValue
            || !incident.ResponsibleStationId.HasValue || !IsWaitingStatus(incident.Status)
            || incident.DuplicateOfIncidentId.HasValue || HasDispatchedWork(incident)) return null;
        if (source == null || source.IsDeleted || !source.IsActive) return "StationUnavailable";
        if (!hasActiveOperator) return "NoOperator";
        if (incident.StationAcknowledgedAt.HasValue || incident.LockedByOperatorId.HasValue && incident.LockExpiresAt > nowUtc)
            return null;
        return incident.StationResponseDueAt <= nowUtc ? "NoResponse" : null;
    }
}
