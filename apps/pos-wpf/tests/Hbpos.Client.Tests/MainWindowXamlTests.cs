using System.Xml.Linq;

namespace Hbpos.Client.Tests;

public sealed class MainWindowXamlTests
{
    [Fact]
    public void Sync_center_order_timestamp_runs_use_one_way_bindings()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var createdAtRun = Assert.Single(document.Descendants(presentation + "Run").Where(
            element => ((string?)element.Attribute("Text"))?.Contains("CreatedAtDisplay", StringComparison.Ordinal) == true));
        var lastTriedAtRun = Assert.Single(document.Descendants(presentation + "Run").Where(
            element => ((string?)element.Attribute("Text"))?.Contains("LastTriedAtDisplay", StringComparison.Ordinal) == true));

        Assert.Equal("{Binding CreatedAtDisplay, Mode=OneWay}", (string?)createdAtRun.Attribute("Text"));
        Assert.Equal("{Binding LastTriedAtDisplay, Mode=OneWay}", (string?)lastTriedAtRun.Attribute("Text"));
    }

    [Fact]
    public void Footer_versions_bind_current_and_conditionally_show_upgrade_or_rollback_target()
    {
        var repoRoot = FindRepoRoot();
        var document = XDocument.Load(Path.Combine(
            repoRoot,
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "MainWindow.xaml"));
        var attributeValues = document.Descendants()
            .Attributes()
            .Select(attribute => attribute.Value)
            .ToList();

        Assert.Contains("{Binding AppUpdate.CurrentVersion, Mode=OneWay}", attributeValues);
        Assert.Equal(
            2,
            attributeValues.Count(value =>
                string.Equals(value, "{Binding AppUpdate.TargetVersion, Mode=OneWay}", StringComparison.Ordinal)));
        Assert.Contains("{Binding AppUpdate.HasDifferentTargetVersion}", attributeValues);
        Assert.Contains("{Binding AppUpdate.IsRollbackTarget}", attributeValues);
        Assert.Contains("{loc:Loc shell.footer.currentVersion}", attributeValues);
        Assert.Contains("{loc:Loc shell.footer.latestVersion}", attributeValues);
        Assert.Contains("{loc:Loc shell.footer.targetVersion}", attributeValues);
        Assert.DoesNotContain("{Binding VersionStatusText}", attributeValues);

        foreach (var resourceName in new[] { "Strings.resx", "Strings.zh-CN.resx" })
        {
            var resource = XDocument.Load(Path.Combine(
                repoRoot,
                "apps",
                "pos-wpf",
                "src",
                "Hbpos.Client.Wpf",
                "Resources",
                resourceName));
            var keys = resource.Descendants("data")
                .Select(element => (string?)element.Attribute("name"))
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains("shell.footer.currentVersion", keys);
            Assert.Contains("shell.footer.latestVersion", keys);
            Assert.Contains("shell.footer.targetVersion", keys);
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "hb-platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to find repository root.");
    }
}
