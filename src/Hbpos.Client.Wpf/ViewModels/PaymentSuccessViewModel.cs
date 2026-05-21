using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed partial class PaymentSuccessViewModel : ObservableObject
{
    private readonly ILocalOrderRepository? _orderRepository;

    [ObservableProperty]
    private Guid? _transactionId;

    [ObservableProperty]
    private decimal _totalAmountPaid;

    [ObservableProperty]
    private DateTimeOffset? _soldAt;

    [ObservableProperty]
    private string _storeCode = string.Empty;

    [ObservableProperty]
    private string _deviceCode = string.Empty;

    [ObservableProperty]
    private string _cashierName = string.Empty;

    public PaymentSuccessViewModel()
    {
        PrintReceiptCommand = new RelayCommand(() => PrintReceiptRequested?.Invoke(this, EventArgs.Empty), () => TransactionId is not null);
        NewTransactionCommand = new RelayCommand(() => NewTransactionRequested?.Invoke(this, EventArgs.Empty));
    }

    public PaymentSuccessViewModel(ILocalOrderRepository orderRepository)
        : this()
    {
        _orderRepository = orderRepository;
    }

    public event EventHandler? PrintReceiptRequested;

    public event EventHandler? NewTransactionRequested;

    public ObservableCollection<ReceiptPreviewLine> ReceiptLines { get; } = [];

    public ObservableCollection<ReceiptPaymentLine> Payments { get; } = [];

    public IRelayCommand PrintReceiptCommand { get; }

    public IRelayCommand NewTransactionCommand { get; }

    public string TitleText => "PaymentSuccessful";

    public string SubtitleText => "success.subtitle";

    public string TotalAmountPaidLabel => "success.totalPaid";

    public string TransactionIdLabel => "success.transactionId";

    public string ReceiptPreviewLabel => "success.receiptPreview";

    public string PrintReceiptLabel => "success.printReceipt";

    public string NewTransactionLabel => "success.newTransaction";

    public string TransactionIdDisplay => TransactionId is null
        ? "-"
        : $"#{TransactionId.Value.ToString("N")[..10].ToUpperInvariant()}";

    public string SoldAtDisplay => SoldAt?.ToLocalTime().ToString("MMM dd, yyyy HH:mm") ?? "-";

    public decimal Subtotal => ReceiptLines.Sum(line => line.ActualAmount);

    public decimal DiscountTotal => ReceiptLines.Sum(line => line.DiscountAmount);

    public async Task LoadAsync(Guid orderGuid, CancellationToken cancellationToken = default)
    {
        if (_orderRepository is null)
        {
            return;
        }

        var order = await _orderRepository.GetOrderAsync(orderGuid, cancellationToken);
        if (order is not null)
        {
            LoadFromOrder(order);
        }
    }

    public async Task LoadLatestAsync(CancellationToken cancellationToken = default)
    {
        if (_orderRepository is null)
        {
            return;
        }

        var latest = (await _orderRepository.GetRecentOrdersAsync(1, cancellationToken)).FirstOrDefault();
        if (latest is not null)
        {
            await LoadAsync(latest.OrderGuid, cancellationToken);
        }
    }

    public void LoadFromOrder(LocalOrder order)
    {
        TransactionId = order.OrderGuid;
        TotalAmountPaid = order.ActualAmount;
        SoldAt = order.SoldAt;
        StoreCode = order.StoreCode;
        DeviceCode = order.DeviceCode;
        CashierName = order.CashierName;

        ReceiptLines.ReplaceWith(order.Lines.Select(line => new ReceiptPreviewLine(
            line.DisplayName,
            line.LookupCode,
            line.Quantity,
            line.UnitPrice,
            line.DiscountAmount,
            line.ActualAmount)));
        Payments.ReplaceWith(order.Payments.Select(payment => new ReceiptPaymentLine(payment.Method, payment.Amount, payment.Reference)));

        OnPropertyChanged(nameof(TransactionIdDisplay));
        OnPropertyChanged(nameof(SoldAtDisplay));
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(DiscountTotal));
        PrintReceiptCommand.NotifyCanExecuteChanged();
    }
}
