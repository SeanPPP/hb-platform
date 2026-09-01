using System.Xml;
using System.Xml.Linq;

namespace Hbpos.Client.Tests;

public sealed class WpfDataGridTouchPanningTests
{
    private const string TouchDataGridStyleKey = "PosCartDataGridStyle";
    private const string StaticResourcePrefix = "{StaticResource ";
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Services = "clr-namespace:Hbpos.Client.Wpf.Services";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void All_wpf_data_grids_support_vertical_touch_dragging()
    {
        var repoRoot = FindRepoRoot();
        var sourceRoot = Path.Combine(repoRoot, "apps", "pos-wpf", "src", "Hbpos.Client.Wpf");
        var documents = Directory
            .EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path, sourceRoot))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new XamlDocument(
                path,
                XDocument.Load(path, LoadOptions.SetLineInfo)))
            .ToArray();

        var styles = documents
            .SelectMany(document => document.Document
                .Descendants(Presentation + "Style")
                .Select(style => new KeyValuePair<string?, XElement>(
                    (string?)style.Attribute(Xaml + "Key"),
                    style)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .ToLookup(entry => entry.Key!, entry => entry.Value, StringComparer.Ordinal);

        var touchStyle = Assert.Single(styles[TouchDataGridStyleKey]);
        AssertSetter(touchStyle, "ScrollViewer.PanningMode", "VerticalOnly");
        AssertSetter(touchStyle, "ScrollViewer.PanningDeceleration", "0.0008");
        AssertSetter(touchStyle, "ScrollViewer.PanningRatio", "1.0");
        AssertSetter(touchStyle, "ScrollViewer.CanContentScroll", "True");
        AssertSetter(touchStyle, "VirtualizingPanel.ScrollUnit", "Pixel");
        AssertSetter(touchStyle, "services:TouchScrollFeedback.IsEnabled", "True");

        var dataGrids = documents
            .SelectMany(document => document.Document
                .Descendants(Presentation + "DataGrid")
                .Select(dataGrid => new XamlElement(document.Path, dataGrid)))
            .ToArray();

        Assert.NotEmpty(dataGrids);

        var failures = new List<string>();
        foreach (var dataGrid in dataGrids)
        {
            VerifyTouchOwnership(dataGrid, styles, failures);
        }

        Assert.True(
            failures.Count == 0,
            "以下 WPF 表格未完整支持手指纵向拖动：" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Daily_close_detail_grids_delegate_inertia_and_feedback_to_one_outer_scroll_viewer()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "Screens",
            "DailyCloseView.xaml"));

        var delegatedSources = new HashSet<string>(StringComparer.Ordinal)
        {
            "{Binding SelectedArchiveNoteCounts}",
            "{Binding SelectedArchiveCoinCounts}"
        };
        var innerDataGrids = document
            .Descendants(Presentation + "DataGrid")
            .Where(dataGrid => delegatedSources.Contains((string?)dataGrid.Attribute("ItemsSource") ?? string.Empty))
            .ToArray();

        Assert.Equal(2, innerDataGrids.Length);
        Assert.All(innerDataGrids, dataGrid =>
        {
            Assert.Equal("None", (string?)dataGrid.Attribute("ScrollViewer.PanningMode"));
            Assert.Equal("False", (string?)dataGrid.Attribute(Services + "TouchScrollFeedback.IsEnabled"));
        });

        var outerScrollViewer = Assert.Single(innerDataGrids
            .Select(dataGrid => dataGrid.Ancestors(Presentation + "ScrollViewer").FirstOrDefault())
            .OfType<XElement>()
            .Distinct());
        Assert.Equal("Disabled", (string?)outerScrollViewer.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("False", (string?)outerScrollViewer.Attribute("CanContentScroll"));
        Assert.Equal("VerticalOnly", (string?)outerScrollViewer.Attribute("PanningMode"));
        Assert.Equal("0.0008", (string?)outerScrollViewer.Attribute("PanningDeceleration"));
        Assert.Equal("1.0", (string?)outerScrollViewer.Attribute("PanningRatio"));
        Assert.Equal("True", (string?)outerScrollViewer.Attribute(Services + "TouchScrollFeedback.IsEnabled"));
    }

    private static void VerifyTouchOwnership(
        XamlElement dataGrid,
        ILookup<string, XElement> styles,
        ICollection<string> failures)
    {
        var location = FormatLocation(dataGrid);
        var localPanningMode = (string?)dataGrid.Element.Attribute("ScrollViewer.PanningMode");
        var verticalScrollBarVisibility = (string?)dataGrid.Element.Attribute("ScrollViewer.VerticalScrollBarVisibility");
        var swipeRevealEnabled = string.Equals(
            (string?)dataGrid.Element.Attribute(Services + "CartSwipeRevealBehavior.IsEnabled"),
            "True",
            StringComparison.Ordinal);
        var styleKey = ParseStaticResource((string?)dataGrid.Element.Attribute("Style"));
        var inheritsTouchStyle = styleKey is not null &&
                                 StyleInheritsFrom(
                                     styleKey,
                                     TouchDataGridStyleKey,
                                     styles,
                                     new HashSet<string>(StringComparer.Ordinal));

        if (string.Equals(verticalScrollBarVisibility, "Disabled", StringComparison.Ordinal))
        {
            failures.Add($"{location} 禁用了纵向滚动。触屏拖动无法移动表格内容。");
        }

        if (string.Equals(localPanningMode, "None", StringComparison.Ordinal))
        {
            // 嵌套表格没有独立滚动范围时，必须把手势明确交给最近的外层 ScrollViewer。
            if (!string.Equals(
                    (string?)dataGrid.Element.Attribute(Services + "TouchScrollFeedback.IsEnabled"),
                    "False",
                    StringComparison.Ordinal))
            {
                failures.Add($"{location} 已把滚动交给外层，但没有关闭自身回弹反馈。");
            }

            VerifyOuterScrollViewerOwnsTouch(dataGrid, failures);
            return;
        }

        VerifyPixelScrollingAndFeedback(dataGrid, inheritsTouchStyle, failures);
        VerifyOptionalPanningOverrides(dataGrid, failures);

        if (swipeRevealEnabled)
        {
            if (!string.Equals(localPanningMode, "VerticalFirst", StringComparison.Ordinal))
            {
                failures.Add($"{location} 启用了左滑操作，但未使用 VerticalFirst 区分横向手势与纵向滚动。");
                return;
            }

            VerifyPanningParameters(dataGrid.Element, location, failures, attachedProperty: true);
            return;
        }

        if (localPanningMode is not null &&
            !string.Equals(localPanningMode, "VerticalOnly", StringComparison.Ordinal))
        {
            failures.Add($"{location} 使用了不受支持的触屏模式 {localPanningMode}。");
            return;
        }

        if (string.Equals(localPanningMode, "VerticalOnly", StringComparison.Ordinal))
        {
            VerifyPanningParameters(dataGrid.Element, location, failures, attachedProperty: true);
            return;
        }

        if (!inheritsTouchStyle)
        {
            failures.Add($"{location} 既未显式启用 VerticalOnly，也未继承 {TouchDataGridStyleKey}。");
        }
    }

    private static void VerifyPixelScrollingAndFeedback(
        XamlElement dataGrid,
        bool inheritsTouchStyle,
        ICollection<string> failures)
    {
        VerifyEffectiveCapability(
            dataGrid,
            "ScrollViewer.CanContentScroll",
            dataGrid.Element.Attribute("ScrollViewer.CanContentScroll"),
            "True",
            inheritsTouchStyle,
            failures);
        VerifyEffectiveCapability(
            dataGrid,
            "VirtualizingPanel.ScrollUnit",
            dataGrid.Element.Attribute("VirtualizingPanel.ScrollUnit"),
            "Pixel",
            inheritsTouchStyle,
            failures);
        VerifyEffectiveCapability(
            dataGrid,
            "services:TouchScrollFeedback.IsEnabled",
            dataGrid.Element.Attribute(Services + "TouchScrollFeedback.IsEnabled"),
            "True",
            inheritsTouchStyle,
            failures);
    }

    private static void VerifyEffectiveCapability(
        XamlElement dataGrid,
        string property,
        XAttribute? localAttribute,
        string expectedValue,
        bool inheritsTouchStyle,
        ICollection<string> failures)
    {
        var localValue = (string?)localAttribute;
        if (localValue is not null && !string.Equals(localValue, expectedValue, StringComparison.Ordinal))
        {
            failures.Add($"{FormatLocation(dataGrid)} 局部覆盖了 {property}={localValue}，预期为 {expectedValue}。");
            return;
        }

        if (localValue is null && !inheritsTouchStyle)
        {
            failures.Add($"{FormatLocation(dataGrid)} 未通过局部设置或 {TouchDataGridStyleKey} 提供 {property}={expectedValue}。");
        }
    }

    private static void VerifyOptionalPanningOverrides(
        XamlElement dataGrid,
        ICollection<string> failures)
    {
        var location = FormatLocation(dataGrid);
        VerifyOptionalOverride(dataGrid.Element, location, "ScrollViewer.PanningDeceleration", "0.0008", failures);
        VerifyOptionalOverride(dataGrid.Element, location, "ScrollViewer.PanningRatio", "1.0", failures);
    }

    private static void VerifyOptionalOverride(
        XElement element,
        string location,
        string property,
        string expectedValue,
        ICollection<string> failures)
    {
        var localValue = (string?)element.Attribute(property);
        if (localValue is not null && !string.Equals(localValue, expectedValue, StringComparison.Ordinal))
        {
            failures.Add($"{location} 局部覆盖了 {property}={localValue}，预期为 {expectedValue}。");
        }
    }

    private static void VerifyOuterScrollViewerOwnsTouch(
        XamlElement dataGrid,
        ICollection<string> failures)
    {
        var location = FormatLocation(dataGrid);
        var scrollViewer = dataGrid.Element
            .Ancestors(Presentation + "ScrollViewer")
            .FirstOrDefault();

        if (scrollViewer is null)
        {
            failures.Add($"{location} 关闭了自身触屏拖动，但没有外层 ScrollViewer 接管手势。");
            return;
        }

        var scrollViewerLocation = $"{dataGrid.Path}:{GetLineNumber(scrollViewer)}";
        if (!string.Equals((string?)scrollViewer.Attribute("PanningMode"), "VerticalOnly", StringComparison.Ordinal))
        {
            failures.Add($"{scrollViewerLocation} 未使用 VerticalOnly 接管嵌套表格 {location} 的手势。");
        }

        if (!string.Equals((string?)scrollViewer.Attribute("HorizontalScrollBarVisibility"), "Disabled", StringComparison.Ordinal))
        {
            failures.Add($"{scrollViewerLocation} 未禁用横向滚动。");
        }

        if (!string.Equals((string?)scrollViewer.Attribute("CanContentScroll"), "False", StringComparison.Ordinal))
        {
            failures.Add($"{scrollViewerLocation} 必须使用像素滚动以接管嵌套表格手势。");
        }

        if (!string.Equals(
                (string?)scrollViewer.Attribute(Services + "TouchScrollFeedback.IsEnabled"),
                "True",
                StringComparison.Ordinal))
        {
            failures.Add($"{scrollViewerLocation} 未开启外层回弹反馈。");
        }

        VerifyPanningParameters(scrollViewer, scrollViewerLocation, failures, attachedProperty: false);
    }

    private static void VerifyPanningParameters(
        XElement element,
        string location,
        ICollection<string> failures,
        bool attachedProperty)
    {
        var prefix = attachedProperty ? "ScrollViewer." : string.Empty;
        if (!string.Equals((string?)element.Attribute(prefix + "PanningDeceleration"), "0.0008", StringComparison.Ordinal))
        {
            failures.Add($"{location} 缺少 {prefix}PanningDeceleration=0.0008。");
        }

        if (!string.Equals((string?)element.Attribute(prefix + "PanningRatio"), "1.0", StringComparison.Ordinal))
        {
            failures.Add($"{location} 缺少 {prefix}PanningRatio=1.0。");
        }
    }

    private static bool StyleInheritsFrom(
        string styleKey,
        string requiredBaseStyleKey,
        ILookup<string, XElement> styles,
        ISet<string> visited)
    {
        if (string.Equals(styleKey, requiredBaseStyleKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (!visited.Add(styleKey))
        {
            return false;
        }

        var style = styles[styleKey].SingleOrDefault();
        var baseStyleKey = ParseStaticResource((string?)style?.Attribute("BasedOn"));
        return baseStyleKey is not null &&
               StyleInheritsFrom(baseStyleKey, requiredBaseStyleKey, styles, visited);
    }

    private static string? ParseStaticResource(string? value)
    {
        if (value is null ||
            !value.StartsWith(StaticResourcePrefix, StringComparison.Ordinal) ||
            !value.EndsWith('}'))
        {
            return null;
        }

        return value[StaticResourcePrefix.Length..^1].Trim();
    }

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

    private static string FormatLocation(XamlElement element) =>
        $"{element.Path}:{GetLineNumber(element.Element)}";

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

    private sealed record XamlDocument(string Path, XDocument Document);

    private sealed record XamlElement(string Path, XElement Element);
}
