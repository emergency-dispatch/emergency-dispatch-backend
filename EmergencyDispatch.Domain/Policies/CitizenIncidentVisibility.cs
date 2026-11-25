using System.Linq.Expressions;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Policies;

/// <summary>Shared publication gate for citizen announcements and public map queries.</summary>
public static class CitizenIncidentVisibility
{
    public static readonly DateTime AllTimeUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    public static Expression<Func<Incident, bool>> ConfirmedActiveSince(DateTime since) => i => !i.IsDeleted
        && i.DuplicateOfIncidentId == null
        && (i.Status == IncidentStatus.Verified || i.Status == IncidentStatus.Dispatched
            || i.Status == IncidentStatus.InProgress || i.Status == IncidentStatus.Escalated)
        && i.Severity >= SeverityLevel.Level1 && i.Severity <= SeverityLevel.Level3 && i.EventType != IncidentEventType.None
        && (i.VerifiedByUserId != null && i.VerifiedAt >= since
            || i.AutoDispatchState == AutoDispatchState.Dispatched && i.DispatchAssignments.Any(a => !a.IsDeleted
                && a.AssignedByUserId == null && a.AcceptedAt >= since
                && (a.Status == DispatchAssignmentStatus.Accepted || a.Status == DispatchAssignmentStatus.EnRoute || a.Status == DispatchAssignmentStatus.OnScene)
                && a.StatusHistories.Any(h => !h.IsDeleted && h.NewStatus == DispatchAssignmentStatus.Accepted
                    && h.ChangedByUserId != null && h.ChangedByUser != null && h.ChangedByUser.Role == UserRole.RescueStaff)));
}
