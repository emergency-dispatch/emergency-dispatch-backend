using EmergencyDispatch.Application.Services;

namespace EmergencyDispatch.Application.Localization;

public static partial class LocalizationCatalog
{
    private static readonly Dictionary<string, string> CitizenMapViToEn = new()
    {
        [CitizenMapMessages.Forbidden] = "Only active citizen accounts can view the incident map.",
        [CitizenMapMessages.InvalidBounds] = "Provide all four valid south, west, north and east bounds; south must not exceed north.",
        [CitizenMapMessages.InvalidPaging] = "Page index must be at least 1 and page size must be between 1 and 500.",
        [CitizenMapMessages.NotFound] = "Active public incident not found."
    };
}
