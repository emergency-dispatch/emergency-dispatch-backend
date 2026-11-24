using EmergencyDispatch.Application.Services;

namespace EmergencyDispatch.Application.Localization;

public static partial class LocalizationCatalog
{
    private static readonly Dictionary<string, string> CitizenIncidentAlertViToEn = new()
    {
        [CitizenIncidentAlertMessages.Title] = "INCIDENT NOTICE",
        [CitizenIncidentAlertMessages.Body] = "An incident has been reported at {0}.",
        [CitizenIncidentAlertMessages.SettingsSaved] = "Citizen incident announcement settings saved.",
        [CitizenIncidentAlertMessages.NotFound] = "Incident announcement not found."
    };
}
