using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed partial class PaymentSuccessViewModel : ObservableObject
{
    private readonly IReceiptQueryService? _receiptQueryService;
    private readonly IReceiptTextFormatter _receiptTextFormatter;
    private readonly IReceiptPrinterSettingsStore? _receiptPrinterSettingsStore;

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
        : this(initialize: true, receiptQueryService: null, receiptTextFormatter: null, receiptPrinterSettingsStore: null)
    {
    }

    public PaymentSuccessViewModel(ILocalOrderRepository orderRepository)
        : this(initialize: true, receiptQueryService: new ReceiptQueryService(orderRepository), receiptTextFormatter: null, receiptPrinterSettingsStore: null)
    {
    }

    public PaymentSuccessViewModel(
        IReceiptQueryService receiptQueryService,
        IReceiptTextFormatter? receiptTextFormatter = null,
        IReceiptPrinterSettingsStore? receiptPrinterSettingsStore = null)
        : this(initialize: true, receiptQueryService, receiptTextFormatter, receiptPrinterSettingsStore)
    {
    }

    private PaymentSuccessViewModel(
        bool initialize,
        IReceiptQueryService? receiptQueryService,
        IReceiptTextFormatter? receiptTextFormatter,
        IReceiptPrinterSettingsStore? receiptPrinterSettingsStore)
    {
        _receiptQueryService = receiptQueryService;
        _receiptTextFormatter = receiptTextFormatter ?? new ReceiptTextFormatter();
        _receiptPrinterSettingsStore = receiptPrinterSettingsStore;
        PrintReceiptCommand = new RelayCommand(() => PrintReceiptRequested?.Invoke(this, EventArgs.Empty), () => TransactionId is not null);
        NewTransactionCommand = new RelayCommand(() => NewTransactionRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? PrintReceiptRequested;

    public event EventHandler? NewTransactionRequested;

    public ObservableCollection<ReceiptPreviewLine> ReceiptLines { get; } = [];

    public ObservableCollection<ReceiptPaymentLine> Payments { get; } = [];

    public ObservableCollection<ReceiptPreviewRow> ReceiptPreviewRows { get; } = [];

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
        if (_receiptQueryService is null)
        {
            return;
        }

        var receipt = await _receiptQueryService.GetReceiptAsync(orderGuid, cancellationToken);
        if (receipt is not null)
        {
            ApplyReceipt(receipt, await LoadPreviewSettingsAsync(cancellationToken));
        }
    }

    public async Task LoadLatestAsync(CancellationToken cancellationToken = default)
    {
        if (_receiptQueryService is null)
        {
            return;
        }

        var latest = await _receiptQueryService.GetLatestReceiptAsync(cancellationToken);
        if (latest is not null)
        {
            ApplyReceipt(latest, await LoadPreviewSettingsAsync(cancellationToken));
        }
    }

    public void LoadFromOrder(LocalOrder order)
    {
        ApplyReceipt(ReceiptQueryService.CreateReceipt(order), ReceiptPrinterSettings.Default);
    }

    public async Task LoadFromOrderAsync(LocalOrder order, CancellationToken cancellationToken = default)
    {
        ApplyReceipt(
            ReceiptQueryService.CreateReceipt(order),
            await LoadPreviewSettingsAsync(cancellationToken));
    }

    private void ApplyReceipt(ReceiptDetails receipt, ReceiptPrinterSettings settings)
    {
        TransactionId = receipt.OrderGuid;
        TotalAmountPaid = receipt.ActualAmount;
        SoldAt = receipt.SoldAt;
        StoreCode = receipt.StoreCode;
        DeviceCode = receipt.DeviceCode;
        CashierName = receipt.CashierName;

        ReceiptLines.ReplaceWith(receipt.Lines);
        Payments.ReplaceWith(receipt.Payments);
        ReceiptPreviewRows.ReplaceWith(BuildPreviewRows(receipt, settings));

        OnPropertyChanged(nameof(TransactionIdDisplay));
        OnPropertyChanged(nameof(SoldAtDisplay));
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(DiscountTotal));
        PrintReceiptCommand.NotifyCanExecuteChanged();
    }

    private async Task<ReceiptPrinterSettings> LoadPreviewSettingsAsync(CancellationToken cancellationToken)
    {
        if (_receiptPrinterSettingsStore is null)
        {
            return ReceiptPrinterSettings.Default;
        }

        try
        {
            return await _receiptPrinterSettingsStore.LoadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ReceiptPrinterSettings.Default;
        }
    }

    private IReadOnlyList<ReceiptPreviewRow> BuildPreviewRows(ReceiptDetails receipt, ReceiptPrinterSettings settings)
    {
        try
        {
            return _receiptTextFormatter.Build(receipt, settings, receipt.SoldAt).PreviewRows;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                return new ReceiptTextFormatter().Build(receipt, ReceiptPrinterSettings.Default, receipt.SoldAt).PreviewRows;
            }
            catch (Exception fallbackEx) when (fallbackEx is not OperationCanceledException)
            {
                return [];
            }
        }
    }
}
