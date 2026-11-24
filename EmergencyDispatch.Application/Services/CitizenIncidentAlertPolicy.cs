using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Services;

public static class CitizenIncidentAlertPolicy
{
    public static bool IsCurrentIncident(Incident i) => !i.IsDeleted && !i.DuplicateOfIncidentId.HasValue
        && i.Status is IncidentStatus.Verified or IncidentStatus.Dispatched or IncidentStatus.InProgress or IncidentStatus.Escalated
        && i.Severity is >= SeverityLevel.Level1 and <= SeverityLevel.Level3 && i.EventType != IncidentEventType.None;

    public static (string Kind, DateTime At)? Trigger(Incident i, DateTime enabledSinceUtc)
    {
        if (!IsCurrentIncident(i)) return null;
        // These fields are written only by the authenticated Operator/Admin verification flow.
        if (i.VerifiedByUserId.HasValue && i.VerifiedAt >= enabledSinceUtc)
            return ("OperatorVerified", i.VerifiedAt.Value);
        if (i.AutoDispatchState != AutoDispatchState.Dispatched) return null;
        var accepted = i.DispatchAssignments.Where(a => !a.IsDeleted && a.AssignedByUserId == null
                && a.AcceptedAt >= enabledSinceUtc
                && a.Status is DispatchAssignmentStatus.Accepted or DispatchAssignmentStatus.EnRoute or DispatchAssignmentStatus.OnScene
                && a.StatusHistories.Any(h => !h.IsDeleted && h.NewStatus == DispatchAssignmentStatus.Accepted
                    && h.ChangedByUserId.HasValue && h.ChangedByUser != null && h.ChangedByUser.Role == UserRole.RescueStaff))
            .OrderBy(a => a.AcceptedAt).FirstOrDefault();
        return accepted == null ? null : ("AiDispatchedStaffAccepted", accepted.AcceptedAt!.Value);
    }

    public static (string Vi, string En) Area(Incident incident)
    {
        // Only the reported location is used, never the incident title/description or reporter profile.
        var text = WebUtility.HtmlDecode(incident.LocationAddress ?? string.Empty);
        text = Regex.Replace(text, "<[^>]*>", " ");
        text = Regex.Replace(text, @"https?://\S+|[\w.+-]+@[\w.-]+\.\w+", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?<!\d)(?:\+?84|0)(?:[ .()\-]*\d){8,10}(?!\d)", " ");
        text = Regex.Replace(text, @"[\p{Cc}\p{Cf}]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', ';', '-');
        if (text.Length >= 3)
        {
            text = text[..Math.Min(text.Length, 300)];
            return (text, text); // Street/place names supplied by the report are preserved.
        }
        var lat = incident.Latitude.ToString("F3", CultureInfo.InvariantCulture);
        var lng = incident.Longitude.ToString("F3", CultureInfo.InvariantCulture);
        return ($"khu vực tọa độ {lat}, {lng}", $"the area near coordinates {lat}, {lng}");
    }
}
