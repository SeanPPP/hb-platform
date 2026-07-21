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
