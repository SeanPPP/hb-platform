using Hbpos.Client.Wpf.ViewModels;
using System.Xml.Linq;

namespace Hbpos.Client.Tests;

public sealed class TransactionHistoryViewLayoutTests
{
    [Fact]
    public void History_order_id_run_uses_one_way_binding_for_read_only_property()
    {
        var displayOrderId = typeof(HistoryOrderListItem).GetProperty(nameof(HistoryOrderListItem.DisplayOrderId));
        Assert.NotNull(displayOrderId);
        Assert.False(displayOrderId!.CanWrite);

        var view = XDocument.Load(Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "Screens",
            "TransactionHistoryView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var orderIdRun = Assert.Single(view.Descendants(presentation + "Run").Where(element =>
            ((string?)element.Attribute("Text"))?.Contains("Binding DisplayOrderId", StringComparison.Ordinal) == true));

        Assert.Equal("{Binding DisplayOrderId, Mode=OneWay}", (string?)orderIdRun.Attribute("Text"));
    }

    [Fact]
    public void History_list_uses_two_line_summary_columns_and_receipt_action()
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
            "TransactionHistoryView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var historyGrid = Assert.Single(view.Descendants(presentation + "DataGrid").Where(element =>
            (string?)element.Attribute(x + "Name") == "HistoryOrdersGrid"));
        Assert.Equal("Disabled", (string?)historyGrid.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));

        var rowStyle = FindStyle(view, x, "HistoryRowStyle");
        AssertSetter(rowStyle, "Height", "72");
        AssertTextStyleDoesNotWrap(view, presentation, x, "HistoryPrimaryTextStyle");
        AssertTextStyleDoesNotWrap(view, presentation, x, "HistorySecondaryTextStyle");

        var columns = Assert.Single(historyGrid.Elements(presentation + "DataGrid.Columns"));
        Assert.Empty(columns.Elements(presentation + "DataGridTextColumn"));
        var columnNames = columns.Elements()
            .Select(element => (string?)element.Attribute(x + "Name"))
            .Where(name => name is not null)
            .ToArray();
        Assert.Contains("OrderSummaryColumn", columnNames);
        Assert.Contains("StandardCashierSummaryColumn", columnNames);
        Assert.Contains("InstallmentCustomerSummaryColumn", columnNames);
        Assert.Contains("StandardAmountSummaryColumn", columnNames);
        Assert.Contains("InstallmentAmountSummaryColumn", columnNames);
        Assert.Contains("StatusSummaryColumn", columnNames);
        Assert.Contains("HistoryActionsColumn", columnNames);

        var actionsColumn = Assert.Single(columns.Elements(presentation + "DataGridTemplateColumn").Where(element =>
            (string?)element.Attribute(x + "Name") == "HistoryActionsColumn"));
        Assert.Equal("216", (string?)actionsColumn.Attribute("Width"));

        var receiptButton = Assert.Single(actionsColumn.Descendants(presentation + "Button").Where(element =>
            ((string?)element.Attribute("Command"))?.Contains("OpenReceiptPreviewCommand", StringComparison.Ordinal) == true));
        Assert.Equal("{Binding}", (string?)receiptButton.Attribute("CommandParameter"));
        Assert.Equal("{loc:Loc history.viewReceipt}", (string?)receiptButton.Attribute("ToolTip"));
        Assert.Equal("{loc:Loc history.viewReceipt}", (string?)receiptButton.Attribute("AutomationProperties.Name"));
        Assert.Contains(receiptButton.Descendants(presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{loc:Loc history.viewReceipt}");

        var recallButton = Assert.Single(actionsColumn.Descendants(presentation + "Button").Where(element =>
            ((string?)element.Attribute("Command"))?.Contains("RecallOrderCommand", StringComparison.Ordinal) == true));
        Assert.Equal("44", (string?)recallButton.Attribute("Width"));
        Assert.Equal("44", (string?)recallButton.Attribute("Height"));
        Assert.Empty(recallButton.Descendants(presentation + "TextBlock"));

        var continuePaymentButton = Assert.Single(actionsColumn.Descendants(presentation + "Button").Where(element =>
            ((string?)element.Attribute("Command"))?.Contains("ContinueInstallmentPaymentCommand", StringComparison.Ordinal) == true));
        Assert.Equal("44", (string?)continuePaymentButton.Attribute("Width"));
        Assert.Equal("44", (string?)continuePaymentButton.Attribute("Height"));
        Assert.Empty(continuePaymentButton.Descendants(presentation + "TextBlock"));

        var moreButton = Assert.Single(actionsColumn.Descendants(presentation + "Button").Where(element =>
            (string?)element.Attribute("Click") == "OpenHistoryRowActionsMenu"));
        Assert.Equal("44", (string?)moreButton.Attribute("Width"));
        Assert.Equal("44", (string?)moreButton.Attribute("Height"));
        Assert.Equal("{loc:Loc history.moreActions}", (string?)moreButton.Attribute("ToolTip"));
        Assert.Equal("{loc:Loc history.moreActions}", (string?)moreButton.Attribute("AutomationProperties.Name"));

        var menu = Assert.Single(moreButton.Descendants(presentation + "ContextMenu"));
        Assert.Equal(
            "{Binding PlacementTarget, RelativeSource={RelativeSource Self}}",
            (string?)menu.Attribute("DataContext"));
        var menuItems = menu.Elements(presentation + "MenuItem").ToArray();
        Assert.Equal(3, menuItems.Length);
        Assert.All(menuItems, item => Assert.Equal("{Binding DataContext}", (string?)item.Attribute("CommandParameter")));
        Assert.Contains(menuItems, item =>
            ((string?)item.Attribute("Command"))?.Contains("ShareHeldOrderCommand", StringComparison.Ordinal) == true);
        Assert.Contains(menuItems, item =>
            ((string?)item.Attribute("Command"))?.Contains("DeleteHeldOrderCommand", StringComparison.Ordinal) == true);
        Assert.Contains(menuItems, item =>
            ((string?)item.Attribute("Command"))?.Contains("ForceReleaseHeldOrderCommand", StringComparison.Ordinal) == true);

        foreach (var key in new[]
                 {
                     "history.orderTime",
                     "history.cashierTerminal",
                     "history.customerPhone",
                     "history.amountItems",
                     "history.totalOutstanding",
                     "history.moreActions",
                     "history.viewReceipt",
                 })
        {
            AssertLocalizationKey(repoRoot, "Strings.resx", key);
            AssertLocalizationKey(repoRoot, "Strings.zh-CN.resx", key);
        }
    }

    [Fact]
    public void History_receipt_preview_is_modal_and_table_uses_full_workspace_width()
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
            "TransactionHistoryView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var pageLayout = Assert.Single(view.Descendants(presentation + "Grid").Where(element =>
            (string?)element.Attribute(x + "Name") == "HistoryPageLayout"));
        Assert.Empty(pageLayout.Elements(presentation + "Grid.ColumnDefinitions"));

        var workspace = Assert.Single(pageLayout.Elements(presentation + "Grid").Where(element =>
            (string?)element.Attribute(x + "Name") == "HistoryWorkspace"));
        Assert.Null(workspace.Attribute("Grid.Column"));
        Assert.Equal("20,18", (string?)workspace.Attribute("Margin"));

        var overlay = Assert.Single(pageLayout.Elements(presentation + "Grid").Where(element =>
            (string?)element.Attribute(x + "Name") == "ReceiptPreviewOverlay"));
        Assert.Equal("#660F172A", (string?)overlay.Attribute("Background"));
        Assert.Equal("True", (string?)overlay.Attribute("FocusManager.IsFocusScope"));
        Assert.Equal("Cycle", (string?)overlay.Attribute("KeyboardNavigation.TabNavigation"));
        Assert.Equal(
            "{Binding IsReceiptPreviewOpen, Converter={StaticResource BoolToVis}}",
            (string?)overlay.Attribute("Visibility"));

        var dialog = Assert.Single(overlay.Elements(presentation + "Border").Where(element =>
            (string?)element.Attribute(x + "Name") == "ReceiptPreviewDialog"));
        Assert.Equal("680", (string?)dialog.Attribute("Width"));
        Assert.Equal("820", (string?)dialog.Attribute("MaxHeight"));

        var receiptRows = Assert.Single(dialog.Descendants(presentation + "ItemsControl").Where(element =>
            (string?)element.Attribute("ItemsSource") == "{Binding ReceiptPreviewRows}"));
        Assert.Equal("{StaticResource ReceiptPreviewRowTemplate}", (string?)receiptRows.Attribute("ItemTemplate"));

        Assert.Equal(2, dialog.Descendants(presentation + "Button").Count(element =>
            (string?)element.Attribute("Command") == "{Binding CloseReceiptPreviewCommand}"));
        Assert.Single(dialog.Descendants(presentation + "Button").Where(element =>
            (string?)element.Attribute("Command") == "{Binding ReprintCommand}"));

        var escapeBinding = Assert.Single(view.Descendants(presentation + "KeyBinding").Where(element =>
            (string?)element.Attribute("Key") == "Escape"));
        Assert.Equal("{Binding CloseReceiptPreviewCommand}", (string?)escapeBinding.Attribute("Command"));

        foreach (var key in new[]
                 {
                     "history.viewReceipt",
                     "history.closeReceiptPreview",
                     "history.receiptLoading",
                 })
        {
            AssertLocalizationKey(repoRoot, "Strings.resx", key);
            AssertLocalizationKey(repoRoot, "Strings.zh-CN.resx", key);
        }
    }

    [Fact]
    public void History_filters_use_two_rows_and_date_picker_inherits_dynamic_language()
    {
        var view = XDocument.Load(Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "Screens",
            "TransactionHistoryView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.Equal("{Binding CurrentUiLanguage}", (string?)view.Root?.Attribute("Language"));

        var header = Assert.Single(view.Descendants(presentation + "Grid").Where(element =>
            (string?)element.Attribute(x + "Name") == "HistoryHeaderRegion"));
        var headerRows = Assert.Single(header.Elements(presentation + "Grid.RowDefinitions"));
        Assert.Equal(2, headerRows.Elements(presentation + "RowDefinition").Count());

        var sourceNavigation = Assert.Single(header.Descendants(presentation + "Border").Where(element =>
            (string?)element.Attribute(x + "Name") == "HistorySourceNavigation"));
        var sourceTabs = sourceNavigation.Descendants(presentation + "RadioButton")
            .Where(element => (string?)element.Attribute("GroupName") == "HistorySource")
            .ToArray();
        Assert.Equal(4, sourceTabs.Length);
        Assert.All(sourceTabs, tab =>
        {
            Assert.Single(tab.Descendants().Where(element => element.Name.LocalName == "PackIcon"));
            Assert.Single(tab.Descendants(presentation + "TextBlock"));
        });

        var selectedTrigger = Assert.Single(
            FindStyle(view, x, "HistorySegmentRadioStyle")
                .Descendants(presentation + "Trigger")
                .Where(element =>
                    (string?)element.Attribute("Property") == "IsChecked" &&
                    (string?)element.Attribute("Value") == "True"));
        Assert.Contains(selectedTrigger.Elements(presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "Root" &&
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{StaticResource PosPrimaryBrush}");
        Assert.Contains(selectedTrigger.Elements(presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "White");

        var filterPanel = Assert.Single(header.Descendants(presentation + "Border").Where(element =>
            (string?)element.Attribute(x + "Name") == "HistoryFilterPanel"));
        Assert.Equal("1", (string?)filterPanel.Attribute("Grid.Row"));
        Assert.Equal("0,10,0,0", (string?)filterPanel.Attribute("Margin"));

        foreach (var filterName in new[]
                 {
                     "HistoryDateRangeFilter",
                     "HistoryTerminalFilter",
                     "HistorySearchFilter",
                 })
        {
            Assert.Single(filterPanel.Descendants().Where(element =>
                (string?)element.Attribute(x + "Name") == filterName));
        }

        var datePickers = filterPanel.Descendants(presentation + "DatePicker").ToArray();
        Assert.Equal(2, datePickers.Length);
        Assert.All(datePickers, picker =>
            Assert.Equal("Short", (string?)picker.Attribute("SelectedDateFormat")));
        Assert.Contains(datePickers, picker =>
            (string?)picker.Attribute("AutomationProperties.AutomationId") == "TransactionHistoryDateFrom");
        Assert.Contains(datePickers, picker =>
            (string?)picker.Attribute("AutomationProperties.AutomationId") == "TransactionHistoryDateTo");

        var loadButton = Assert.Single(filterPanel.Descendants(presentation + "Button").Where(element =>
            (string?)element.Attribute("Command") == "{Binding LoadCommand}"));
        Assert.Equal("{StaticResource PosPrimaryButtonStyle}", (string?)loadButton.Attribute("Style"));
    }

    private static void AssertTextStyleDoesNotWrap(
        XDocument document,
        XNamespace presentation,
        XNamespace x,
        string key)
    {
        var style = FindStyle(document, x, key);
        AssertSetter(style, "TextWrapping", "NoWrap");
        AssertSetter(style, "TextTrimming", "CharacterEllipsis");
        Assert.Equal("TextBlock", (string?)style.Attribute("TargetType"));
        Assert.NotEmpty(style.Elements(presentation + "Setter"));
    }

    private static XElement FindStyle(XDocument document, XNamespace x, string key) =>
        Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute(x + "Key") == key));

    private static void AssertSetter(XElement style, string property, string value) =>
        Assert.Contains(style.Elements().Where(element => element.Name.LocalName == "Setter"), setter =>
            (string?)setter.Attribute("Property") == property && (string?)setter.Attribute("Value") == value);

    private static void AssertLocalizationKey(string repoRoot, string fileName, string key)
    {
        var resource = XDocument.Load(Path.Combine(
            repoRoot,
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Resources",
            fileName));
        Assert.Contains(resource.Descendants("data"), element =>
            (string?)element.Attribute("name") == key);
    }

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
