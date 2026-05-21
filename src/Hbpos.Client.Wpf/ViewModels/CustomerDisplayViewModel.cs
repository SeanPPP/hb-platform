using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hbpos.Client.Wpf.Models;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed partial class CustomerDisplayViewModel : ObservableObject
{
    [ObservableProperty]
    private decimal _subtotal;

    [ObservableProperty]
    private decimal _taxAmount;

    [ObservableProperty]
    private decimal _savingsAmount;

    [ObservableProperty]
    private decimal _totalToPay;

    [ObservableProperty]
    private string _terminalName = "Terminal 01";

    [ObservableProperty]
    private string _promotionTitle = "customer.promotionTitle";

    [ObservableProperty]
    private string _promotionSubtitle = "customer.promotionSubtitle";

    [ObservableProperty]
    private string _promotionBody = "customer.promotionBody";

    [ObservableProperty]
    private bool _isReadyForPayment;

    public ObservableCollection<CustomerDisplayLine> Lines { get; } = [];

    public string TotalToPayLabel => "customer.totalToPay";

    public string ReadyForPaymentLabel => "customer.readyForPayment";

    public string InsertOrTapLabel => "customer.insertOrTap";

    public string SubtotalLabel => "Subtotal";

    public string TaxLabel => "Tax";

    public string SavingsLabel => "Savings";

    public void LoadLines(IEnumerable<CustomerDisplayLine> lines, decimal subtotal, decimal taxAmount, decimal savingsAmount)
    {
        Lines.ReplaceWith(lines);
        Subtotal = subtotal;
        TaxAmount = taxAmount;
        SavingsAmount = savingsAmount;
        TotalToPay = subtotal + taxAmount - savingsAmount;
        IsReadyForPayment = TotalToPay > 0m;
    }
}
