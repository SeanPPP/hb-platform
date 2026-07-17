using System.Xml.Linq;

namespace Hbpos.Client.Tests;

public sealed class PaymentViewLayoutTests
{
    [Fact]
    public void Installment_toggle_is_compact_accessible_and_next_to_back_button()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Views",
            "Screens",
            "PaymentView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var elements = document.Descendants().ToArray();
        var backButton = Assert.Single(elements.Where(element =>
            element.Name == presentation + "Button" &&
            (string?)element.Attribute("Command") == "{Binding BackToPosCommand}"));
        var installmentToggle = Assert.Single(elements.Where(element =>
            element.Name == presentation + "ToggleButton" &&
            (string?)element.Attribute("AutomationProperties.AutomationId") == "InstallmentPaymentToggle"));
        var orderSummary = Assert.Single(elements.Where(element =>
            element.Name == presentation + "TextBlock" &&
            (string?)element.Attribute("Text") == "{Binding OrderSummaryText}"));

        Assert.True(Array.IndexOf(elements, backButton) < Array.IndexOf(elements, installmentToggle));
        Assert.True(Array.IndexOf(elements, installmentToggle) < Array.IndexOf(elements, orderSummary));
        Assert.Same(backButton.Parent, installmentToggle.Parent);
        Assert.Same(installmentToggle.Parent, orderSummary.Parent?.Parent);
        Assert.Equal("0", (string?)installmentToggle.Attribute("Grid.Row"));
        Assert.Equal("1", (string?)installmentToggle.Attribute("Grid.Column"));
        Assert.Equal("1", (string?)orderSummary.Parent?.Attribute("Grid.Row"));
        Assert.Equal("2", (string?)orderSummary.Parent?.Attribute("Grid.ColumnSpan"));

        Assert.Equal(
            "{Binding IsInstallmentPaymentEnabled, Mode=TwoWay}",
            (string?)installmentToggle.Attribute("IsChecked"));
        Assert.Single(elements.SelectMany(element => element.Attributes()).Where(attribute =>
            attribute.Value == "{Binding IsInstallmentPaymentEnabled, Mode=TwoWay}"));
        Assert.Equal(
            "{Binding InstallmentMethodText}",
            (string?)installmentToggle.Attribute("AutomationProperties.Name"));
        Assert.Equal(
            "{loc:Loc payment.installment.switchHelp}",
            (string?)installmentToggle.Attribute("ToolTip"));
        Assert.Equal("True", (string?)installmentToggle.Attribute("ToolTipService.ShowOnDisabled"));

        var localStyle = Assert.Single(installmentToggle
            .Elements(presentation + "ToggleButton.Style")
            .Elements(presentation + "Style"));
        Assert.Contains(localStyle.Descendants(presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsInstallmentEntryVisible}" &&
            (string?)trigger.Attribute("Value") == "False" &&
            HasSetter(trigger, "Visibility", "Collapsed"));
        Assert.Contains(localStyle.Descendants(presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsInstallmentSwitchLocked}" &&
            (string?)trigger.Attribute("Value") == "True" &&
            HasSetter(trigger, "IsEnabled", "False"));

        var content = Assert.Single(installmentToggle.Elements(presentation + "Grid"));
        Assert.Empty(content.Descendants().Where(element => element.Name.LocalName == "PackIcon"));
        Assert.Single(content.Descendants(presentation + "TextBlock").Where(text =>
            (string?)text.Attribute("Text") == "{Binding InstallmentMethodText}"));
        Assert.DoesNotContain(content.Descendants(presentation + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{loc:Loc payment.installment.switchHelp}");

        var sharedStyle = Assert.Single(elements.Where(element =>
            element.Name == presentation + "Style" &&
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key" &&
                attribute.Value == "InstallmentMethodToggleStyle")));
        Assert.Contains(sharedStyle.Descendants(presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver" &&
            (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(sharedStyle.Descendants(presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsChecked" &&
            (string?)trigger.Attribute("Value") == "True");
        Assert.Contains(sharedStyle.Descendants(presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("Property") == "IsEnabled" &&
            (string?)trigger.Attribute("Value") == "False");
    }

    private static bool HasSetter(XElement trigger, string property, string value)
    {
        return trigger.Elements().Any(element =>
            element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("Property") == property &&
            (string?)element.Attribute("Value") == value);
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
