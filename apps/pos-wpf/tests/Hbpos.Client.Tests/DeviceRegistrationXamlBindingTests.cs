using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Hbpos.Client.Tests;

public sealed class DeviceRegistrationXamlBindingTests
{
    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("Views/Screens/DeviceRegistrationView.xaml")]
    public void Activation_expiry_run_uses_one_way_binding(string relativePath)
    {
        var pathSegments = relativePath.Split('/');
        var xamlPath = Path.Combine(
            [
                FindRepoRoot(),
                "apps",
                "pos-wpf",
                "src",
                "Hbpos.Client.Wpf",
                .. pathSegments
            ]);
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var expiryRun = Assert.Single(document.Descendants(presentation + "Run").Where(element =>
            ((string?)element.Attribute("Text"))?.Contains("PreviewExpiryText", StringComparison.Ordinal) == true));

        Assert.Equal(
            "{Binding PreviewExpiryText, Mode=OneWay}",
            (string?)expiryRun.Attribute("Text"));
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
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
