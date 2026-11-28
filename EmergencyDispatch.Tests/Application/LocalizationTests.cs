using EmergencyDispatch.Domain.Enums;
using System.Text.RegularExpressions;
using EmergencyDispatch.Application.Localization;

namespace EmergencyDispatch.Tests.Application;

public class LocalizationTests
{
    [Fact]
    public void Localize_English_TranslatesCatalogEntry()
    {
        var (vi, en) = LocalizationCatalog.ViToEn.First();

        Assert.Equal(en, TestHelpers.WithCulture("en-US", () => LocalizationHelper.Localize(vi)));
    }

    [Fact]
    public void Localize_Vietnamese_KeepsVietnameseText()
    {
        var (vi, _) = LocalizationCatalog.ViToEn.First();

        Assert.Equal(vi, TestHelpers.WithCulture("vi-VN", () => LocalizationHelper.Localize(vi)));
    }

    [Fact]
    public void Localize_FormatsArguments()
    {
        var s = TestHelpers.WithCulture("vi-VN", () => LocalizationHelper.Localize("Giá trị {0} và {1}", 1, "x"));

        Assert.Equal("Giá trị 1 và x", s);
    }

    [Theory]
    [InlineData("en-US", "Lỗi hệ thống: abc", "System error: abc")]
    [InlineData("en-US", "Lỗi: abc", "Error: abc")]
    [InlineData("vi-VN", "System error: abc", "Lỗi hệ thống: abc")]
    [InlineData("vi-VN", "Error: abc", "Lỗi: abc")]
    public void Localize_TranslatesErrorPrefixes(string culture, string input, string expected)
    {
        Assert.Equal(expected, TestHelpers.WithCulture(culture, () => LocalizationHelper.Localize(input)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Localize_Empty_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, LocalizationHelper.Localize(input));
    }

    [Fact]
    public void Localize_UnknownText_IsReturnedUnchanged()
    {
        Assert.Equal("zzz-not-in-catalog", TestHelpers.WithCulture("en-US", () => LocalizationHelper.Localize("zzz-not-in-catalog")));
    }

    [Fact]
    public void LocalizedText_RendersBothLanguages_WithNestedAndDataArgs()
    {
        var text = new LocalizedText("[{0}] Xe {1} từ chối sự cố \"{2}\" ({3}). {4}", "SC-261118-0001", "51B-003.14", "Kho A", RejectionReason.TooFar,
            new LocalizedText("Còn {0} xe đang xử lý.", 2));

        Assert.Equal("[SC-261118-0001] Xe 51B-003.14 từ chối sự cố \"Kho A\" (TooFar). Còn 2 xe đang xử lý.", text.Vi);
        Assert.Equal("[SC-261118-0001] Vehicle 51B-003.14 rejected incident \"Kho A\" (TooFar). 2 vehicle(s) still on the incident.", text.En);
        Assert.Equal(text.Vi, text.ToString());
    }

    [Fact]
    public void LocalizedText_KeepsUserTextAndPadding_TranslatesCatalogArgs()
    {
        var text = new LocalizedText("chi viện {0} xe{1}{2}", 2, " FireTruck", " — cần thêm nước");
        var fromOther = new LocalizedText("{0} — {1} (từ {2}). Lý do: {3}", "Kho A", "Q7", "trạm khác", "đường cấm");

        Assert.Equal("backup of 2 vehicle(s) FireTruck — cần thêm nước", text.En);
        Assert.Equal("chi viện 2 xe FireTruck — cần thêm nước", text.Vi);
        Assert.Equal("Kho A — Q7 (from another station). Reason: đường cấm", fromOther.En);
        Assert.Equal("Plain text", ((LocalizedText)"Plain text").En);
    }

    /// <summary>
    /// Notification templates (new LocalizedText("...") and literal Title / Message of NotificationRequest) need an English entry,
    /// otherwise English users get the Vietnamese text.
    /// </summary>
    [Fact]
    public void EveryNotificationTemplate_HasEnglishTranslation()
    {
        var services = Path.Combine(TestHelpers.SolutionDir(), "EmergencyDispatch.Application", "Services");
        var templates = new Regex("new LocalizedText\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        var requestLiterals = new Regex("(?:Title|Message)\\s*=\\s*(?:[^;\\n]*?\\?\\s*)?\"((?:[^\"\\\\]|\\\\.)*)\"(?:\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\")?", RegexOptions.Compiled);
        var helperLiterals = new Regex("Notify(?:Requester|CommandCenter)Async\\([^,]+,\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        var missing = new SortedSet<string>();

        foreach (var file in Directory.EnumerateFiles(services, "*.cs"))
        {
            var source = File.ReadAllText(file);
            var found = new List<string>();
            found.AddRange(templates.Matches(source).Select(m => m.Groups[1].Value));
            found.AddRange(helperLiterals.Matches(source).Select(m => m.Groups[1].Value));
            // Title / Message literal trong khối new NotificationRequest { ... }
            foreach (Match block in Regex.Matches(source, "new NotificationRequest\\s*\\{(.*?)\\n\\s*\\}", RegexOptions.Singleline))
            {
                foreach (Match m in requestLiterals.Matches(block.Groups[1].Value))
                {
                    found.Add(m.Groups[1].Value);
                    if (m.Groups[2].Success) found.Add(m.Groups[2].Value);
                }
            }

            foreach (var raw in found)
            {
                var text = Regex.Unescape(raw).Trim();
                if (text.Length == 0 || !Regex.IsMatch(text, "[a-zA-Zàáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]", RegexOptions.IgnoreCase))
                {
                    continue; // mẫu chỉ có tham số, ví dụ "{0}: {1}"
                }
                if (!LocalizationCatalog.ViToEn.ContainsKey(text))
                {
                    missing.Add($"{Path.GetFileName(file)}: {text}");
                }
            }
        }

        Assert.True(missing.Count == 0, $"{missing.Count} notification templates have no English entry:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// Project rule: every user-facing message passed to LocalizationHelper.Localize must have an English entry.
    /// Scans the source code for Localize("...") literals and checks them against the catalog.
    /// </summary>
    [Fact]
    public void EveryLocalizedLiteral_HasEnglishTranslation()
    {
        var root = TestHelpers.SolutionDir();
        var literal = new Regex("Localize\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        var missing = new SortedSet<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains("EmergencyDispatch.Tests") || file.Contains("LocalizationCatalog"))
            {
                continue;
            }

            foreach (Match m in literal.Matches(File.ReadAllText(file)))
            {
                var text = Regex.Unescape(m.Groups[1].Value).Trim();
                if (text.Length == 0 || !Regex.IsMatch(text, "[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]", RegexOptions.IgnoreCase))
                {
                    continue; // only Vietnamese source texts need an English entry
                }
                if (!LocalizationCatalog.ViToEn.ContainsKey(text))
                {
                    missing.Add($"{Path.GetFileName(file)}: {text}");
                }
            }
        }

        Assert.True(missing.Count == 0, $"{missing.Count} Vietnamese messages have no English entry:\n" + string.Join("\n", missing));
    }

    [Theory]
    [InlineData("vi-VN", "Thời gian chờ giữa hai lần gửi SOS phải từ 0 đến 60 phút.")]
    [InlineData("en-US", "The waiting time between SOS reports must be between 0 and 60 minutes.")]
    public void ReportCooldownValidationMessage_IsLocalized(string culture, string expected)
        => Assert.Equal(expected, TestHelpers.WithCulture(culture, () => LocalizationHelper.Localize(
            "Thời gian chờ giữa hai lần gửi SOS phải từ {0} đến {1} phút.", 0, 60)));

    [Theory]
    [InlineData("vi-VN", "Bạn vừa gửi một yêu cầu cứu hộ đang được xử lý. Vui lòng chờ 10 phút hoặc gọi trực tiếp đường dây nóng nếu cần hỗ trợ thêm.")]
    [InlineData("en-US", "You just sent a rescue request that is being processed. Please wait 10 minutes or call hotline directly if you need more help.")]
    public void ReportCooldownRejectionMessage_FormatsConfiguredMinutes(string culture, string expected)
        => Assert.Equal(expected, TestHelpers.WithCulture(culture, () => LocalizationHelper.Localize(
            "Bạn vừa gửi một yêu cầu cứu hộ đang được xử lý. Vui lòng chờ {0} phút hoặc gọi trực tiếp đường dây nóng nếu cần hỗ trợ thêm.", 10)));

}
