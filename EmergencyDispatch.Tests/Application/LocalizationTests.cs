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
}
