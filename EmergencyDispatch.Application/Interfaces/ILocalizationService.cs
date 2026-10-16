namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Giao diện dịch vụ bản địa hóa (song ngữ vi/en) cho toàn bộ hệ thống
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Lấy văn bản theo ngôn ngữ hiện tại của request (vi hoặc en)
    /// </summary>
    string Get(string key, params object[] args);

    /// <summary>
    /// Lấy văn bản theo chỉ mục key
    /// </summary>
    string this[string key] { get; }

    /// <summary>
    /// Mã văn hóa hiện tại (ví dụ: "vi", "en")
    /// </summary>
    string CurrentCulture { get; }

    /// <summary>
    /// Kiểm tra ngôn ngữ hiện tại có phải là tiếng Anh hay không
    /// </summary>
    bool IsEnglish { get; }
}
