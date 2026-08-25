using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Hbpos.Client.Tests;

public sealed class ReceiptReturnsViewLayoutTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace MaterialDesign = "http://materialdesigninxaml.net/winfx/xaml/themes";

    [Fact]
    public void Refund_summary_stacks_amount_above_full_width_confirm_button()
    {
        var view = LoadView();
        var totalLabel = Assert.Single(view.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") == "{loc:Loc returns.totalRefund}"));
        var summaryRow = Assert.IsType<XElement>(totalLabel.Parent);
        var actionGrid = Assert.IsType<XElement>(summaryRow.Parent);

        Assert.Equal(Presentation + "Grid", summaryRow.Name);
        Assert.Equal("0", (string?)summaryRow.Attribute("Grid.Row"));
        Assert.Equal(Presentation + "Grid", actionGrid.Name);

        var actionRows = Assert.Single(actionGrid.Elements(Presentation + "Grid.RowDefinitions"))
            .Elements(Presentation + "RowDefinition")
            .Select(row => (string?)row.Attribute("Height"))
            .ToArray();
        Assert.Equal(["Auto", "12", "56"], actionRows);

        var amount = Assert.Single(summaryRow.Elements(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") == "{Binding PendingTotal, StringFormat={}{0:C2}}"));
        Assert.Equal("1", (string?)amount.Attribute("Grid.Column"));
        Assert.Equal("Right", (string?)amount.Attribute("HorizontalAlignment"));
        Assert.Equal("Right", (string?)amount.Attribute("TextAlignment"));

        var confirmButton = Assert.Single(actionGrid.Elements(Presentation + "Button"));
        Assert.Equal("2", (string?)confirmButton.Attribute("Grid.Row"));
        Assert.Equal("Stretch", (string?)confirmButton.Attribute("HorizontalAlignment"));
        Assert.Equal("56", (string?)confirmButton.Attribute("Height"));
    }

    [Fact]
    public void Refund_summary_preserves_total_and_confirm_action_contracts()
    {
        var view = LoadView();
        var amount = Assert.Single(view.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute("Text") == "{Binding PendingTotal, StringFormat={}{0:C2}}"));
        var confirmButton = Assert.Single(view.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute("Command") == "{Binding ConfirmToCartCommand}"));

        Assert.Equal("{StaticResource ReturnDangerAmountStyle}", (string?)amount.Attribute("Style"));
        Assert.Equal("{StaticResource PosPrimaryButtonStyle}", (string?)confirmButton.Attribute("Style"));
        Assert.Equal("180", (string?)confirmButton.Attribute("MinWidth"));

        var cartIcon = Assert.Single(confirmButton.Descendants(MaterialDesign + "PackIcon"));
        var buttonLabel = Assert.Single(confirmButton.Descendants(Presentation + "TextBlock"));
        Assert.Equal("Cart", (string?)cartIcon.Attribute("Kind"));
        Assert.Equal("{loc:Loc returns.addToCart}", (string?)buttonLabel.Attribute("Text"));
    }

    private static XDocument LoadView() => XDocument.Load(Path.Combine(
        FindRepoRoot(),
        "apps",
        "pos-wpf",
        "src",
        "Hbpos.Client.Wpf",
        "Views",
        "Screens",
        "ReceiptReturnsView.xaml"));

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[]
                 {
                     Path.GetDirectoryName(sourceFilePath),
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                 })
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

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
