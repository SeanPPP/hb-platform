using System.Xml.Linq;

namespace Hbpos.Client.Tests;

public sealed class PosTerminalViewLayoutTests
{
    [Fact]
    public void Pos_terminal_middle_controls_use_consistent_touch_and_brand_styles()
    {
        var repoRoot = FindRepoRoot();
        var view = XDocument.Load(Path.Combine(
            repoRoot,
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "Screens",
            "PosTerminalView.xaml"));
        var theme = XDocument.Load(Path.Combine(
            repoRoot,
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Themes",
            "PosTheme.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var backspaceButton = Assert.Single(view.Descendants(presentation + "Button").Where(button =>
            (string?)button.Attribute("CommandParameter") == "Back" &&
            ((string?)button.Attribute("Command"))?.Contains("KeypadInputCommand", StringComparison.Ordinal) == true));
        Assert.Equal("48", (string?)backspaceButton.Attribute("Height"));

        var inputBorder = Assert.Single(view.Descendants(presentation + "Border").Where(border =>
            border.Descendants(presentation + "TextBlock").Any(text =>
                ((string?)text.Attribute("Text"))?.Contains("pos.terminal.inputBuffer", StringComparison.Ordinal) == true)));
        Assert.Equal("10,4", (string?)inputBorder.Attribute("Padding"));
        Assert.Equal("12,8,12,6", (string?)inputBorder.Attribute("Margin"));

        var keypad = Assert.Single(view.Descendants(presentation + "UniformGrid").Where(grid =>
            grid.Descendants(presentation + "Button").Any(button =>
                (string?)button.Attribute("CommandParameter") == "QuickHalf")));
        Assert.Equal("10,0,10,6", (string?)keypad.Attribute("Margin"));
        AssertStyleSetter(theme, x, "PosMainNumberButtonStyle", "Margin", "2");

        var middleActionGrid = Assert.Single(view.Descendants(presentation + "Grid").Where(
            element => (string?)element.Attribute("Grid.Row") == "3" &&
                       element.Descendants(presentation + "Button").Any(button =>
                           ((string?)button.Attribute("Command"))?.Contains("ModifySelectedLineQuantityCommand", StringComparison.Ordinal) == true)));
        Assert.Equal("9,0,9,0", (string?)middleActionGrid.Attribute("Margin"));

        AssertStyleSetter(theme, x, "PosMainNumberButtonStyle", "MinHeight", "48");
        AssertStyleSetter(theme, x, "PosMainQuickButtonStyle", "Background", "#FFE8F0FE");
        AssertStyleSetter(theme, x, "PosMainQuickButtonStyle", "BorderBrush", "#FF8AB4F8");
        AssertStyleSetter(theme, x, "PosMainQuickButtonStyle", "Foreground", "#FF003C9A");
        AssertStyleSetter(theme, x, "PosMainSwitchButtonStyle", "MinHeight", "48");
        AssertStyleSetter(theme, x, "PosMainSwitchButtonStyle", "Background", "White");
        AssertStyleSetter(theme, x, "PosMainSwitchButtonStyle", "BorderBrush", "{StaticResource PosPrimaryBrush}");
        AssertStyleSetter(theme, x, "PosMainSwitchButtonStyle", "Foreground", "{StaticResource PosPrimaryDarkBrush}");

        var wholeOrderStyle = FindStyle(theme, x, "PosMainSwitchButtonStyle");
        var checkedTrigger = Assert.Single(wholeOrderStyle.Descendants(presentation + "Trigger").Where(trigger =>
            (string?)trigger.Attribute("Property") == "IsChecked" && (string?)trigger.Attribute("Value") == "True"));
        AssertTriggerSetter(checkedTrigger, "Background", "{StaticResource PosPrimaryBrush}");
        AssertTriggerSetter(checkedTrigger, "BorderBrush", "{StaticResource PosPrimaryBrush}");
        AssertTriggerSetter(checkedTrigger, "Foreground", "White");

        var functionStyle = FindStyle(theme, x, "PosStitchFunctionButtonStyle");
        AssertSetter(functionStyle, "Background", "White");
        AssertSetter(functionStyle, "BorderBrush", "{StaticResource PosBorderBrush}");
        AssertSetter(functionStyle, "Foreground", "{StaticResource PosPrimaryDarkBrush}");
        AssertSetter(functionStyle, "Margin", "3");
        var functionTemplateBorder = Assert.Single(functionStyle.Descendants(presentation + "Border"));
        Assert.Equal("12", (string?)functionTemplateBorder.Attribute("CornerRadius"));
        Assert.Equal("{TemplateBinding Background}", (string?)functionTemplateBorder.Attribute("Background"));
        Assert.Equal("{TemplateBinding BorderBrush}", (string?)functionTemplateBorder.Attribute("BorderBrush"));
        var functionPressedTrigger = Assert.Single(functionStyle.Descendants(presentation + "Trigger").Where(trigger =>
            (string?)trigger.Attribute("Property") == "IsPressed" && (string?)trigger.Attribute("Value") == "True"));
        AssertTriggerSetter(functionPressedTrigger, "Background", "#FFD6E4FA");

        var discountStyle = FindStyle(theme, x, "PosQuickDiscountButtonStyle");
        AssertSetter(discountStyle, "Height", "56");
        AssertSetter(discountStyle, "Margin", "0");
        AssertSetter(discountStyle, "Background", "Transparent");
        AssertSetter(discountStyle, "BorderBrush", "{StaticResource PosBorderBrush}");
        AssertSetter(discountStyle, "BorderThickness", "0,0,1,0");
        var discountTemplateBorder = Assert.Single(discountStyle.Descendants(presentation + "Border"));
        Assert.Equal("0", (string?)discountTemplateBorder.Attribute("CornerRadius"));
        Assert.Equal("{TemplateBinding BorderBrush}", (string?)discountTemplateBorder.Attribute("BorderBrush"));
        var discountTemplateText = Assert.Single(discountStyle.Descendants(presentation + "TextBlock"));
        Assert.Equal("{TemplateBinding Foreground}", (string?)discountTemplateText.Attribute("Foreground"));
        Assert.Contains(discountStyle.Descendants(presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver" &&
            trigger.Descendants(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "BorderBrush" &&
                (string?)setter.Attribute("Value") == "{StaticResource PosPrimaryBrush}"));
        var discountPressedTrigger = Assert.Single(discountStyle.Descendants(presentation + "Trigger").Where(trigger =>
            (string?)trigger.Attribute("Property") == "IsPressed" && (string?)trigger.Attribute("Value") == "True"));
        AssertTriggerSetter(discountPressedTrigger, "Background", "{StaticResource PosPrimaryBrush}");
        AssertTriggerSetter(discountPressedTrigger, "BorderBrush", "{StaticResource PosPrimaryBrush}");
        AssertTriggerSetter(discountPressedTrigger, "Foreground", "White");

        var quickDiscountButtons = view.Descendants(presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("ApplyQuickDiscountPercentCommand", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(5, quickDiscountButtons.Length);
        Assert.Equal(["10", "20", "30", "40", "50"],
            quickDiscountButtons.Select(button => (string?)button.Attribute("CommandParameter")));
        Assert.All(quickDiscountButtons, button =>
        {
            Assert.Null(button.Attribute("Background"));
            Assert.Null(button.Attribute("Foreground"));
            Assert.Null(button.Attribute("IsChecked"));
        });
        Assert.Equal("0", (string?)quickDiscountButtons[^1].Attribute("BorderThickness"));

        var discountSegment = Assert.Single(view.Descendants(presentation + "Border").Where(border =>
            (string?)border.Attribute(x + "Name") == "QuickDiscountSegment"));
        Assert.Equal("3,4,3,0", (string?)discountSegment.Attribute("Margin"));
        Assert.Equal("12", (string?)discountSegment.Attribute("CornerRadius"));
        Assert.Null(discountSegment.Attribute("ClipToBounds"));
        Assert.Equal("{StaticResource PosBorderBrush}", (string?)discountSegment.Attribute("BorderBrush"));
        Assert.Equal("1", (string?)discountSegment.Attribute("BorderThickness"));
        var opacityMask = Assert.Single(discountSegment.Elements(presentation + "Border.OpacityMask"));
        var visualBrush = Assert.Single(opacityMask.Elements(presentation + "VisualBrush"));
        var maskVisual = Assert.Single(visualBrush.Elements(presentation + "VisualBrush.Visual"));
        var maskBorder = Assert.Single(maskVisual.Elements(presentation + "Border"));
        Assert.Equal("12", (string?)maskBorder.Attribute("CornerRadius"));
        Assert.Equal("Black", (string?)maskBorder.Attribute("Background"));

        var noBarcodeButton = Assert.Single(view.Descendants(presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("AddOpenItemCommand", StringComparison.Ordinal) == true));
        Assert.Equal("12,0,12,6", (string?)noBarcodeButton.Attribute("Margin"));
        const string ancestorForeground = "{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}";
        Assert.All(noBarcodeButton.Descendants().Where(element =>
            element.Name.LocalName is "PackIcon" or "TextBlock"), element =>
            Assert.Equal(ancestorForeground, (string?)element.Attribute("Foreground")));

        var noBarcodeStyle = FindStyle(theme, x, "PosNoBarcodeButtonStyle");
        Assert.Equal("{StaticResource PosBlueGradientButtonStyle}", (string?)noBarcodeStyle.Attribute("BasedOn"));
        var disabledTrigger = Assert.Single(noBarcodeStyle.Descendants(presentation + "Trigger").Where(trigger =>
            (string?)trigger.Attribute("Property") == "IsEnabled" && (string?)trigger.Attribute("Value") == "False"));
        AssertTriggerSetter(disabledTrigger, "Background", "#FF9CA3AF");
        AssertTriggerSetter(disabledTrigger, "BorderBrush", "#FF6B7280");
        AssertTriggerSetter(disabledTrigger, "Foreground", "White");

        var functionCommands = new[]
        {
            "ModifySelectedLineQuantityCommand",
            "ModifySelectedLinePriceCommand",
            "ApplySelectedLineDiscountAmountCommand",
            "ApplySelectedLineDiscountPercentCommand"
        };
        Assert.All(functionCommands, command =>
        {
            var button = Assert.Single(view.Descendants(presentation + "Button").Where(element =>
                ((string?)element.Attribute("Command"))?.Contains(command, StringComparison.Ordinal) == true));
            Assert.Equal("{StaticResource PosStitchFunctionButtonStyle}", (string?)button.Attribute("Style"));
            var icon = Assert.Single(button.Descendants().Where(element => element.Name.LocalName == "PackIcon"));
            Assert.Equal("{StaticResource PosPrimaryBrush}", (string?)icon.Attribute("Foreground"));
        });
        var removedFunctionStyleKeys = new[]
        {
            "PosStitchQuantityButtonStyle",
            "PosStitchPriceButtonStyle",
            "PosStitchDiscountAmountButtonStyle",
            "PosStitchDiscountPercentButtonStyle"
        };
        Assert.All(removedFunctionStyleKeys, key =>
            Assert.DoesNotContain(theme.Descendants(), element =>
                element.Name.LocalName == "Style" && (string?)element.Attribute(x + "Key") == key));
    }

    [Theory]
    [InlineData(1080, 720)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    public void Attendance_sidebar_keeps_five_action_rows_touchable_at_supported_sizes(int width, int height)
    {
        var repoRoot = FindRepoRoot();
        var mainWindow = XDocument.Load(Path.Combine(repoRoot, "apps", "pos-wpf", "src", "Hbpos.Client.Wpf", "MainWindow.xaml"));
        Assert.True(width >= (double)mainWindow.Root!.Attribute("MinWidth")!);
        Assert.True(height >= (double)mainWindow.Root.Attribute("MinHeight")!);

        var view = XDocument.Load(Path.Combine(repoRoot, "apps", "pos-wpf", "src", "Hbpos.Client.Wpf", "Views", "Screens", "PosTerminalView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var rootColumns = Assert.Single(view.Root!.Elements(presentation + "Grid"))
            .Element(presentation + "Grid.ColumnDefinitions")!
            .Elements(presentation + "ColumnDefinition")
            .ToArray();
        Assert.Equal(3, rootColumns.Length);
        Assert.True(rootColumns.Sum(column => (double)column.Attribute("MinWidth")!) <= (double)mainWindow.Root.Attribute("MinWidth")!);

        var theme = XDocument.Load(Path.Combine(repoRoot, "apps", "pos-wpf", "src", "Hbpos.Client.Wpf", "Themes", "PosTheme.xaml"));
        AssertStyleSetter(theme, x, "PosStitchActionButtonStyle", "MinHeight", "48");
        var actionStyle = FindStyle(theme, x, "PosStitchActionButtonStyle");
        var actionButtonMargin = ParseVerticalMargin((string?)Assert.Single(actionStyle.Elements().Where(element =>
            element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "Margin")).Attribute("Value"));

        var sidebar = Assert.Single(view.Descendants().Where(element => (string?)element.Attribute(x + "Name") == "AttendanceQrSidebar"));
        var actionGrid = Assert.Single(sidebar.Elements(presentation + "UniformGrid"));
        Assert.Equal(10, actionGrid.Elements(presentation + "Button").Count());
        var status = Assert.Single(sidebar.Elements(presentation + "ContentControl"));
        var launcher = Assert.Single(sidebar.Elements(presentation + "Button").Where(element =>
            (string?)element.Attribute(x + "Name") == "AttendanceQrLauncher"));
        Assert.True((double)launcher.Attribute("Height")! <= 56);
        var requiredHeight = (5 * (48 + actionButtonMargin))
            + ParseVerticalMargin((string?)actionGrid.Attribute("Margin"))
            + (double)status.Attribute("MaxHeight")!
            + ParseVerticalMargin((string?)status.Attribute("Margin"))
            + (double)launcher.Attribute("Height")!
            + ParseVerticalMargin((string?)launcher.Attribute("Margin"));
        Assert.True(requiredHeight <= 720 - 48 - 32);
    }

    private static double ParseVerticalMargin(string? value)
    {
        var parts = value!.Split(',').Select(double.Parse).ToArray();
        return parts.Length switch
        {
            1 => parts[0] * 2,
            2 => parts[1] * 2,
            4 => parts[1] + parts[3],
            _ => throw new FormatException("无效的 Thickness。"),
        };
    }

    [Fact]
    public void Attendance_qr_uses_compact_launcher_and_native_overlay_dialog()
    {
        var repoRoot = FindRepoRoot();
        var view = XDocument.Load(Path.Combine(repoRoot, "apps", "pos-wpf", "src", "Hbpos.Client.Wpf", "Views", "Screens", "PosTerminalView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var sidebar = Assert.Single(view.Descendants().Where(element =>
            (string?)element.Attribute(x + "Name") == "AttendanceQrSidebar"));
        var rows = Assert.Single(sidebar.Elements().Where(element => element.Name.LocalName == "Grid.RowDefinitions")).Elements().ToArray();
        Assert.Equal(["*", "Auto", "Auto"], rows.Select(row => (string?)row.Attribute("Height")));

        Assert.DoesNotContain(sidebar.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "AttendanceQrCard");
        var launcher = Assert.Single(sidebar.Elements().Where(element =>
            (string?)element.Attribute(x + "Name") == "AttendanceQrLauncher"));
        Assert.Equal("56", (string?)launcher.Attribute("Height"));
        Assert.Equal("AttendanceQrLauncher_Click", (string?)launcher.Attribute("Click"));

        var overlay = Assert.Single(view.Descendants().Where(element =>
            (string?)element.Attribute(x + "Name") == "AttendanceQrOverlay"));
        Assert.Equal("0", (string?)overlay.Attribute("Grid.Column"));
        Assert.Equal("3", (string?)overlay.Attribute("Grid.ColumnSpan"));
        Assert.Equal("AttendanceQrOverlay_Click", (string?)overlay.Attribute("MouseLeftButtonDown"));
        Assert.Equal("{loc:Loc attendance.qr.title}", (string?)overlay.Attribute("AutomationProperties.Name"));
        Assert.Equal("{loc:Loc attendance.qr.scanHint}", (string?)overlay.Attribute("AutomationProperties.HelpText"));
        var dialog = Assert.Single(overlay.Descendants().Where(element =>
            (string?)element.Attribute(x + "Name") == "AttendanceQrDialog"));
        Assert.Equal("360", (string?)dialog.Attribute("Width"));
        Assert.Equal("430", (string?)dialog.Attribute("Height"));

        var image = Assert.Single(dialog.Descendants().Where(element =>
            element.Name.LocalName == "Image"
            && ((string?)element.Attribute("Source"))?.Contains("AttendanceQrPanel.QrImage", StringComparison.Ordinal) == true));
        Assert.Equal("260", (string?)image.Attribute("Width"));
        Assert.Equal("260", (string?)image.Attribute("Height"));
        Assert.Equal("None", (string?)image.Attribute("Stretch"));
        Assert.Equal("NearestNeighbor", (string?)image.Attribute("RenderOptions.BitmapScalingMode"));
        var closeButton = Assert.Single(dialog.Descendants().Where(element => element.Name.LocalName == "Button"
            && (string?)element.Attribute("Click") == "AttendanceQrCloseButton_Click"));
        Assert.Equal("{loc:Loc attendance.qr.closeHelp}", (string?)closeButton.Attribute("AutomationProperties.HelpText"));
        Assert.Equal("AttendanceQrView_KeyDown", (string?)view.Root!.Attribute("KeyDown"));

        var panelRunBindings = view.Descendants(presentation + "Run")
            .Select(run => (string?)run.Attribute("Text"))
            .Where(text => text?.StartsWith("{Binding AttendanceQrPanel.", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(
            [
                "{Binding AttendanceQrPanel.VerificationStatusText, Mode=OneWay}",
                "{Binding AttendanceQrPanel.MessageText, Mode=OneWay}",
                "{Binding AttendanceQrPanel.DeviceText, Mode=OneWay}",
            ],
            panelRunBindings);

        var english = XDocument.Load(Path.Combine(repoRoot, "apps", "pos-wpf", "src", "Hbpos.Client.Wpf", "Resources", "Strings.resx"));
        var chinese = XDocument.Load(Path.Combine(repoRoot, "apps", "pos-wpf", "src", "Hbpos.Client.Wpf", "Resources", "Strings.zh-CN.resx"));
        Assert.Contains(english.Descendants("data"), element => (string?)element.Attribute("name") == "attendance.qr.closeHelp");
        Assert.Contains(chinese.Descendants("data"), element => (string?)element.Attribute("name") == "attendance.qr.closeHelp");
    }

    private static XElement FindStyle(XDocument document, XNamespace x, string key) =>
        Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute(x + "Key") == key));

    private static void AssertStyleSetter(XDocument document, XNamespace x, string key, string property, string value) =>
        AssertSetter(FindStyle(document, x, key), property, value);

    private static void AssertSetter(XElement style, string property, string value) =>
        Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == property && (string?)setter.Attribute("Value") == value);

    private static void AssertTriggerSetter(XElement trigger, string property, string value) =>
        Assert.Contains(trigger.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == property && (string?)setter.Attribute("Value") == value);

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
