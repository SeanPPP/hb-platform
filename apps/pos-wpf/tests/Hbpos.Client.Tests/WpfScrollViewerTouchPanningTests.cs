using System.Xml;
using System.Xml.Linq;

namespace Hbpos.Client.Tests;

public sealed class WpfScrollViewerTouchPanningTests
{
    private const string VerticalTouchStyleKey = "PosVerticalTouchScrollViewerStyle";
    private const string HorizontalTouchStyleKey = "PosHorizontalTouchScrollViewerStyle";

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Shared_touch_scroll_viewer_styles_define_axis_specific_native_panning()
    {
        var theme = XDocument.Load(Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Themes",
            "PosTheme.xaml"));

        var verticalStyle = FindStyle(theme, VerticalTouchStyleKey);
        Assert.Equal("ScrollViewer", (string?)verticalStyle.Attribute("TargetType"));
        Assert.Equal(
            "{StaticResource {x:Type ScrollViewer}}",
            (string?)verticalStyle.Attribute("BasedOn"));
        AssertSetter(verticalStyle, "PanningMode", "VerticalOnly");
        AssertSetter(verticalStyle, "PanningDeceleration", "0.0008");
        AssertSetter(verticalStyle, "PanningRatio", "1.0");
        AssertSetter(verticalStyle, "CanContentScroll", "False");
        AssertSetter(verticalStyle, "services:TouchScrollFeedback.IsEnabled", "True");

        var horizontalStyle = FindStyle(theme, HorizontalTouchStyleKey);
        Assert.Equal("ScrollViewer", (string?)horizontalStyle.Attribute("TargetType"));
        Assert.Equal(
            "{StaticResource {x:Type ScrollViewer}}",
            (string?)horizontalStyle.Attribute("BasedOn"));
        AssertSetter(horizontalStyle, "PanningMode", "HorizontalOnly");
        AssertSetter(horizontalStyle, "PanningDeceleration", "0.0008");
        AssertSetter(horizontalStyle, "PanningRatio", "1.0");
        AssertSetter(horizontalStyle, "CanContentScroll", "False");
        Assert.DoesNotContain(
            horizontalStyle.Elements(Presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "services:TouchScrollFeedback.IsEnabled");
    }

    [Fact]
    public void All_explicit_wpf_scroll_viewers_use_the_touch_style_for_their_scroll_axis()
    {
        var sourceRoot = Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf");
        var failures = new List<string>();
        var foundHorizontalScroller = false;

        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !IsBuildOutput(path, sourceRoot)))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var scrollViewer in document.Descendants(Presentation + "ScrollViewer"))
            {
                var isHorizontalOnly = string.Equals(
                    (string?)scrollViewer.Attribute("VerticalScrollBarVisibility"),
                    "Disabled",
                    StringComparison.Ordinal);
                var expectedStyle = isHorizontalOnly ? HorizontalTouchStyleKey : VerticalTouchStyleKey;
                foundHorizontalScroller |= isHorizontalOnly;

                if (!string.Equals(
                        (string?)scrollViewer.Attribute("Style"),
                        $"{{StaticResource {expectedStyle}}}",
                        StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{Path.GetRelativePath(sourceRoot, path)}:{GetLineNumber(scrollViewer)} " +
                        $"应使用 {expectedStyle}。");
                }
            }
        }

        Assert.True(foundHorizontalScroller, "测试样本中至少应包含一个仅横向滚动的页面区域。");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Horizontal_page_scrollers_own_touch_instead_of_nested_list_controls()
    {
        var sourceRoot = Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf");
        var horizontalScrollers = Directory
            .EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path, sourceRoot))
            .Select(path => XDocument.Load(path, LoadOptions.SetLineInfo))
            .SelectMany(document => document.Descendants(Presentation + "ScrollViewer"))
            .Where(scrollViewer =>
                (string?)scrollViewer.Attribute("Style") ==
                $"{{StaticResource {HorizontalTouchStyleKey}}}")
            .ToArray();

        Assert.NotEmpty(horizontalScrollers);
        Assert.All(
            horizontalScrollers.SelectMany(scrollViewer => scrollViewer.Descendants(Presentation + "ListBox")),
            listBox => Assert.Equal("None", (string?)listBox.Attribute("ScrollViewer.PanningMode")));
    }

    private static XElement FindStyle(XDocument document, string key) =>
        Assert.Single(document.Descendants(Presentation + "Style").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == key));

    private static void AssertSetter(XElement style, string property, string value) =>
        Assert.Contains(
            style.Elements(Presentation + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == property &&
                (string?)setter.Attribute("Value") == value);

    private static bool IsBuildOutput(string path, string sourceRoot)
    {
        var relativePath = Path.GetRelativePath(sourceRoot, path);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static int GetLineNumber(XElement element) =>
        element is IXmlLineInfo lineInfo && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "apps", "pos-wpf")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Unable to find repository root.");
    }
}
