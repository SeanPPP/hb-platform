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
    public void History_list_uses_two_line_summary_columns_and_compact_icon_actions()
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
        Assert.Equal("112", (string?)actionsColumn.Attribute("Width"));

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
                 })
        {
            AssertLocalizationKey(repoRoot, "Strings.resx", key);
            AssertLocalizationKey(repoRoot, "Strings.zh-CN.resx", key);
        }
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
