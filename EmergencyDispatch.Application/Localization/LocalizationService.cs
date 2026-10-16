using System.Globalization;
using EmergencyDispatch.Application.Interfaces;

namespace EmergencyDispatch.Application.Localization;

/// <summary>
/// Dịch vụ chuyển ngữ linh hoạt hai chiều dựa trên CultureInfo.CurrentUICulture
/// </summary>
public class LocalizationService : ILocalizationService
{
    public string CurrentCulture =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();

    public bool IsEnglish => CurrentCulture == "en";

    public string this[string key] => Get(key);

    public string Get(string key, params object[] args)
    {
        return LocalizationHelper.Localize(key, args);
    }
}

/// <summary>
/// Helper tĩnh hỗ trợ dịch nhanh cho các DTO, ApiResponseDto và Exception Filters
/// </summary>
public static class LocalizationHelper
{
    public static string CurrentLanguage =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();

    public static bool IsEnglish => CurrentLanguage == "en";

    /// <summary>
    /// Chuyển ngữ chuỗi văn bản theo ngôn ngữ hiện tại của request
    /// </summary>
    public static string Localize(string? text, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        string localized = trimmed;

        if (IsEnglish)
        {
            // Muốn tiếng Anh: nếu có trong từ điển ViToEn thì dịch sang tiếng Anh
            if (LocalizationCatalog.ViToEn.TryGetValue(trimmed, out var enVal))
            {
                localized = enVal;
            }
            else if (trimmed.StartsWith("Lỗi hệ thống:", StringComparison.OrdinalIgnoreCase))
            {
                localized = "System error:" + trimmed.Substring("Lỗi hệ thống:".Length);
            }
            else if (trimmed.StartsWith("Lỗi:", StringComparison.OrdinalIgnoreCase))
            {
                localized = "Error:" + trimmed.Substring("Lỗi:".Length);
            }
        }
        else
        {
            // Mặc định hoặc Tiếng Việt: nếu có trong từ điển EnToVi thì dịch sang tiếng Việt
            if (LocalizationCatalog.EnToVi.TryGetValue(trimmed, out var viVal))
            {
                localized = viVal;
            }
            else if (trimmed.StartsWith("System error:", StringComparison.OrdinalIgnoreCase))
            {
                localized = "Lỗi hệ thống:" + trimmed.Substring("System error:".Length);
            }
            else if (trimmed.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            {
                localized = "Lỗi:" + trimmed.Substring("Error:".Length);
            }
        }

        if (args != null && args.Length > 0)
        {
            try
            {
                localized = string.Format(localized, args);
            }
            catch
            {
                // Giữ nguyên chuỗi nếu định dạng không khớp
            }
        }

        return localized;
    }
}
