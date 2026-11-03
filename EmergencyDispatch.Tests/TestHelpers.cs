using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace EmergencyDispatch.Tests;

internal static class TestHelpers
{
    public static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    /// <summary>Folder that contains EmergencyDispatch.slnx (used by source-scanning tests).</summary>
    public static string SolutionDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EmergencyDispatch.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("EmergencyDispatch.slnx not found");
    }

    /// <summary>Runs an action under a UI culture and restores the previous one.</summary>
    public static T WithCulture<T>(string culture, Func<T> action)
    {
        var old = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = old;
        }
    }
}
