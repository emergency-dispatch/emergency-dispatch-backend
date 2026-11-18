namespace EmergencyDispatch.Application.Localization;

/// <summary>
/// Văn bản song ngữ dựng muộn: mẫu tiếng Việt có {0}, {1}... (khóa trong LocalizationCatalog) + tham số.
/// Dùng khi người đọc khác người tạo (thông báo, push): lúc tạo dựng cả bản vi và en, người nhận xem bản theo ngôn ngữ của mình.
/// Tham số là chuỗi có trong catalog / LocalizedText lồng nhau cũng được dịch; dữ liệu người dùng (tiêu đề, ghi chú) giữ nguyên.
/// </summary>
public sealed class LocalizedText
{
    public string Template { get; }
    public IReadOnlyList<object?> Args { get; }

    public LocalizedText(string? template, params object?[] args)
    {
        Template = template ?? string.Empty;
        Args = args ?? Array.Empty<object?>();
    }

    public static implicit operator LocalizedText(string? text) => new(text);

    public string Render(string language)
    {
        if (Args.Count == 0)
        {
            return LocalizationHelper.LocalizeForLanguage(Template, language);
        }
        var rendered = Args.Select(a => RenderArg(a, language)).ToArray();
        return LocalizationHelper.LocalizeForLanguage(Template, language, rendered);
    }

    public string Vi => Render("vi");
    public string En => Render("en");

    /// <summary>
    /// Bản tiếng Việt (dùng khi nội suy vào nhật ký / audit vốn ghi tiếng Việt)
    /// </summary>
    public override string ToString() => Vi;

    private static object RenderArg(object? arg, string language)
    {
        switch (arg)
        {
            case null:
                return string.Empty;
            case LocalizedText nested:
                return nested.Render(language);
            case string s:
                // Chỉ thay khi chuỗi có trong catalog; không thì giữ nguyên (kể cả khoảng trắng đầu / cuối dùng để ghép câu)
                var localized = LocalizationHelper.LocalizeForLanguage(s, language);
                return localized == s.Trim() ? s : localized;
            default:
                return arg;
        }
    }
}
