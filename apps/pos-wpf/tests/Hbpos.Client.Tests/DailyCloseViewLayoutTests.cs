using System.Xml.Linq;

namespace Hbpos.Client.Tests;

public sealed class DailyCloseViewLayoutTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Cash_count_panel_uses_denomination_buttons_without_inline_keypad_or_apply_buttons()
    {
        var view = LoadView();
        var cashPanel = FindNamedElement(view, "CashCountPanel");

        Assert.NotNull(FindNamedElement(view, "NoteDenominationList"));
        Assert.NotNull(FindNamedElement(view, "CoinDenominationList"));
        Assert.Empty(cashPanel.Descendants(Presentation + "DataGrid"));
        Assert.Empty(cashPanel.Descendants(Presentation + "UniformGrid"));

        var itemTemplate = Assert.Single(view.Descendants(Presentation + "DataTemplate").Where(element =>
            (string?)element.Attribute(Xaml + "Key") == "CashDenominationItemTemplate"));
        var denominationButton = Assert.Single(itemTemplate.Descendants(Presentation + "Button"));
        Assert.Contains("OpenCashCountDialogCommand", (string?)denominationButton.Attribute("Command"));
        Assert.Equal("{Binding}", (string?)denominationButton.Attribute("CommandParameter"));
        Assert.DoesNotContain("ApplyDenominationCommand", itemTemplate.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void Cash_count_dialog_uses_strict_nine_grid_and_separate_zero_clear_backspace_row()
    {
        var view = LoadView();
        var overlay = FindNamedElement(view, "CashCountDialogOverlay");
        Assert.Equal(
            "{Binding IsCashCountDialogOpen, Converter={StaticResource BoolToVis}}",
            (string?)overlay.Attribute("Visibility"));
        Assert.Equal("DailyCloseCashCountDialog", (string?)overlay.Attribute("AutomationProperties.AutomationId"));
        Assert.Equal("CashCountDialogOverlayIsVisibleChanged", (string?)overlay.Attribute("IsVisibleChanged"));
        Assert.Equal("CashCountDialogOverlayPreviewKeyDown", (string?)overlay.Attribute("PreviewKeyDown"));
        Assert.Equal("Cycle", (string?)overlay.Attribute("KeyboardNavigation.TabNavigation"));
        Assert.NotNull(FindNamedElement(view, "CashCountDialogCancelButton"));

        var nineGrid = FindNamedElement(view, "CashCountNineGrid");
        Assert.Equal("3", (string?)nineGrid.Attribute("Rows"));
        Assert.Equal("3", (string?)nineGrid.Attribute("Columns"));
        var digitButtons = nineGrid.Elements(Presentation + "Button").ToArray();
        Assert.Equal(9, digitButtons.Length);
        Assert.Equal(
            Enumerable.Range(1, 9).Select(value => value.ToString()).ToArray(),
            digitButtons.Select(button => (string?)button.Attribute("CommandParameter")).ToArray());
        Assert.All(digitButtons, button => Assert.Contains("KeypadInputCommand", (string?)button.Attribute("Command")));

        var utilityRow = FindNamedElement(view, "CashCountUtilityRow");
        Assert.Equal("1", (string?)utilityRow.Attribute("Rows"));
        Assert.Equal("3", (string?)utilityRow.Attribute("Columns"));
        var utilityButtons = utilityRow.Elements(Presentation + "Button").ToArray();
        Assert.Equal(3, utilityButtons.Length);
        Assert.Contains("KeypadClearCommand", (string?)utilityButtons[0].Attribute("Command"));
        Assert.Equal("0", (string?)utilityButtons[1].Attribute("CommandParameter"));
        Assert.Contains("KeypadBackspaceCommand", (string?)utilityButtons[2].Attribute("Command"));

        var applyButton = Assert.Single(overlay.Descendants(Presentation + "Button").Where(button =>
            ((string?)button.Attribute("Command"))?.Contains("ApplyDenominationCommand", StringComparison.Ordinal) == true));
        Assert.Equal("{Binding SelectedCashDenomination}", (string?)applyButton.Attribute("CommandParameter"));
    }

    [Fact]
    public void Cash_count_dialog_run_bindings_are_one_way_for_read_only_display_values()
    {
        var view = LoadView();
        var overlay = FindNamedElement(view, "CashCountDialogOverlay");

        var runBindings = overlay.Descendants(Presentation + "Run")
            .Select(run => (string?)run.Attribute("Text"))
            .Where(text => text?.StartsWith("{Binding ", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(
            [
                "{Binding SelectedCashDenomination.Label, Mode=OneWay}",
                "{Binding SelectedCashDenomination.Label, Mode=OneWay}",
                "{Binding CashCountDialogQuantity, Mode=OneWay}",
                "{Binding SelectedCashDenomination.Label, Mode=OneWay}",
            ],
            runBindings);
    }

    [Fact]
    public void Cash_count_dialog_text_is_available_in_english_and_chinese_resources()
    {
        var repoRoot = FindRepoRoot();
        var keys = new[]
        {
            "dailyClose.cashCountHint",
            "dailyClose.quantityShort",
            "dailyClose.cashCountDialog.automationName",
            "dailyClose.cashCountDialog.titlePrefix",
            "dailyClose.cashCountDialog.helperPrefix",
            "dailyClose.cashCountDialog.helperSuffix",
            "dailyClose.cashCountDialog.clear",
            "dailyClose.cashCountDialog.backspace",
            "dailyClose.cashCountDialog.apply"
        };

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
            var resourceKeys = resource.Descendants("data")
                .Select(element => (string?)element.Attribute("name"))
                .Where(key => key is not null)
                .ToHashSet(StringComparer.Ordinal);

            Assert.All(keys, key => Assert.Contains(key, resourceKeys));
        }
    }

    private static XDocument LoadView() => XDocument.Load(Path.Combine(
        FindRepoRoot(),
        "apps",
        "pos-wpf",
        "src",
        "Hbpos.Client.Wpf",
        "Views",
        "Screens",
        "DailyCloseView.xaml"));

    private static XElement FindNamedElement(XDocument document, string name) =>
        Assert.Single(document.Descendants().Where(element =>
            (string?)element.Attribute(Xaml + "Name") == name));

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
