using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Hbpos.Client.Wpf.Localization;

namespace Hbpos.Client.Tests;

public sealed class LinklyLineLocalizationTests
{
    [Fact]
    public void Line_resources_exist_in_both_languages_with_matching_format_arguments()
    {
        var manager = new ResourceManager("Hbpos.Client.Wpf.Resources.SettingsStrings", typeof(LocalizationService).Assembly);
        using var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        using var chinese = manager.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false)!;
        var keys = english.Cast<DictionaryEntry>().Select(entry => (string)entry.Key)
            .Where(key => key.StartsWith("settings.linkly.lines.", StringComparison.Ordinal)).OrderBy(key => key).ToArray();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            var en = Assert.IsType<string>(english.GetObject(key));
            var zh = Assert.IsType<string>(chinese.GetObject(key));
            Assert.False(string.IsNullOrWhiteSpace(en));
            Assert.False(string.IsNullOrWhiteSpace(zh));
            Assert.NotEqual(en, zh);
            Assert.Equal(FormatArguments(en), FormatArguments(zh));
        }
    }

    private static string[] FormatArguments(string value) =>
        Regex.Matches(value, @"\{\d+(?:[^}]*)\}").Select(match => match.Value).OrderBy(value => value).ToArray();
}
