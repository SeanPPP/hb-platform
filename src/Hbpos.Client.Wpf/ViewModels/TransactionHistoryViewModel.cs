using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed partial class TransactionHistoryViewModel : ObservableObject
{
    private readonly ILocalOrderRepository? _orderRepository;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _dateFilterText = "Today";

    [ObservableProperty]
    private string _storeFilterText = "All Stores";

    [ObservableProperty]
    private string _terminalFilterText = "All Terminals";

    [ObservableProperty]
    private LocalOrderSummary? _selectedOrder;

    [ObservableProperty]
    private decimal _previewSubtotal;

    [ObservableProperty]
    private decimal _previewDiscount;

    [ObservableProperty]
    private decimal _previewTotal;

    [ObservableProperty]
    private string _previewOrderId = "-";

    [ObservableProperty]
    private string _previewSoldAt = "-";

    public TransactionHistoryViewModel()
    {
        LoadCommand = new AsyncRelayCommand(() => LoadAsync());
        ReprintCommand = new RelayCommand(() => ReprintRequested?.Invoke(this, EventArgs.Empty), () => SelectedOrder is not null);
        RefundCommand = new RelayCommand(() => { }, () => false);
    }

    public TransactionHistoryViewModel(ILocalOrderRepository orderRepository)
        : this()
    {
        _orderRepository = orderRepository;
    }

    public event EventHandler? ReprintRequested;

    public ObservableCollection<LocalOrderSummary> Orders { get; } = [];

    public ObservableCollection<ReceiptPreviewLine> ReceiptLines { get; } = [];

    public ObservableCollection<ReceiptPaymentLine> Payments { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    public IRelayCommand ReprintCommand { get; }

    public IRelayCommand RefundCommand { get; }

    public string TitleText => "TransactionHistory";

    public string SearchHintText => "history.search";

    public string ReceiptPreviewLabel => "success.receiptPreview";

    public string ReprintLabel => "history.reprint";

    public string RefundLabel => "history.refund";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_orderRepository is null)
        {
            return;
        }

        var orders = await _orderRepository.GetRecentOrdersAsync(50, cancellationToken);
        Orders.ReplaceWith(orders);
        SelectedOrder = Orders.FirstOrDefault();
        await LoadSelectedReceiptAsync(cancellationToken);
    }

    partial void OnSelectedOrderChanged(LocalOrderSummary? value)
    {
        ReprintCommand.NotifyCanExecuteChanged();
        _ = LoadSelectedReceiptAsync(CancellationToken.None);
    }

    private async Task LoadSelectedReceiptAsync(CancellationToken cancellationToken)
    {
        if (_orderRepository is null || SelectedOrder is null)
        {
            ReceiptLines.Clear();
            Payments.Clear();
            PreviewSubtotal = 0m;
            PreviewDiscount = 0m;
            PreviewTotal = 0m;
            PreviewOrderId = "-";
            PreviewSoldAt = "-";
            return;
        }

        var order = await _orderRepository.GetOrderAsync(SelectedOrder.OrderGuid, cancellationToken);
        if (order is null)
        {
            return;
        }

        ReceiptLines.ReplaceWith(order.Lines.Select(line => new ReceiptPreviewLine(
            line.DisplayName,
            line.LookupCode,
            line.Quantity,
            line.UnitPrice,
            line.DiscountAmount,
            line.ActualAmount)));
        Payments.ReplaceWith(order.Payments.Select(payment => new ReceiptPaymentLine(payment.Method, payment.Amount, payment.Reference)));
        PreviewSubtotal = order.TotalAmount;
        PreviewDiscount = order.DiscountAmount;
        PreviewTotal = order.ActualAmount;
        PreviewOrderId = $"#{order.OrderGuid.ToString("N")[..10].ToUpperInvariant()}";
        PreviewSoldAt = order.SoldAt.ToLocalTime().ToString("MMM dd, yyyy HH:mm");
    }
}
