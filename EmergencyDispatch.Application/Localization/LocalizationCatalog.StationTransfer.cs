using EmergencyDispatch.Application.Services;

namespace EmergencyDispatch.Application.Localization;

public static partial class LocalizationCatalog
{
    private static readonly Dictionary<string, string> StationTransferViToEn = new()
    {
        [StationTransferMessages.Acknowledged] = "Station acknowledged the incident.",
        [StationTransferMessages.Changed] = "The incident station or status has changed. Please reload.",
        [StationTransferMessages.Closed] = "Closed or linked incidents cannot be acknowledged or transferred.",
        [StationTransferMessages.NeedOperator] = "The receiving station must have at least one active Operator.",
        [StationTransferMessages.ActiveWork] = "Rescue assignments already exist. Only an Admin may explicitly confirm their handover.",
        [StationTransferMessages.Locked] = "Another Operator is handling this incident.",
        [StationTransferMessages.Transferred] = "Incident ownership transferred to the receiving station.",
        [StationTransferMessages.NoOperator] = "The responsible station has no active Operator.",
        [StationTransferMessages.NoResponse] = "The responsible station did not acknowledge the incident before its deadline.",
        [StationTransferMessages.Unavailable] = "The responsible station is no longer operational.",
        [StationTransferMessages.NoTarget] = "No suitable receiving station is available. An Admin must handle the incident.",
        [StationTransferMessages.Limit] = "The automatic station transfer limit has been reached. An Admin must take over.",
        [StationTransferMessages.AlertTitle] = "ADMIN ACTION REQUIRED FOR STATION HANDOVER",
        [StationTransferMessages.TransferTitle] = "INCIDENT STATION TRANSFERRED",
        [StationTransferMessages.TransferBody] = "Incident {0} was transferred from {1} to {2}.",
        [StationTransferMessages.NoStationName] = "unassigned station",
        [StationTransferMessages.SettingsSaved] = "Automatic station transfer settings saved.",
        [StationTransferMessages.SettingsInvalid] = "Invalid station transfer settings.",
        [StationTransferMessages.InvalidMultiplier] = "The acknowledgement timeout multiplier must be between 1 and 10.",
        [StationTransferMessages.InvalidLimit] = "The automatic station transfer limit must be between 1 and 5."
    };
}
