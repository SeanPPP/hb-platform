using System.Collections.ObjectModel;
using System.Globalization;
using BlazorApp.Shared.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.ViewModels;

public enum TransactionHistorySource
{
    LocalOrders,
    RemoteOrders,
    InstallmentOrders
}

public sealed record HistorySourceOption(TransactionHistorySource Source, string Label);

public sealed record TerminalFilterOption(string? DeviceCode, string Label);

public sealed record HistoryOrderListItem(
    Guid OrderGuid,
    TransactionHistorySource Source,
    string StoreCode,
    string DeviceCode,
    string CashierName,
    DateTimeOffset OccurredAt,
    decimal TotalAmount,
    decimal DiscountAmount,
    decimal ActualAmount,
    int LineCount,
    string PaymentSummary,
    string StatusLabel,
    bool IsSuspendedOrder = false,
    bool CanRecall = false,
    InstallmentOrderSummary? InstallmentOrder = null,
    bool IsInstallmentOrder = false,
    bool CanContinueInstallmentPayment = false,
    bool CanConfirmInstallmentPickup = false,
    string CustomerPhone = "")
{
    public string ShortOrderId => OrderGuid.ToString("N")[..8].ToUpperInvariant();

    public string DisplayOrderId => InstallmentOrder?.OrderNumber ?? ShortOrderId;

    public string SoldAtDisplay => OccurredAt.ToLocalTime().ToString("MMM dd, yyyy HH:mm", CultureInfo.CurrentCulture);
}

public sealed partial class TransactionHistoryViewModel : ObservableObject, IDisposable
{
    private readonly IReceiptQueryService? _receiptQueryService;
    private readonly ISuspendedOrderService? _suspendedOrderService;
    private readonly IRemoteOrderHistoryService? _remoteOrderHistoryService;
    private readonly IInstallmentOrderService _installmentOrderService;
    private readonly IReceiptTextFormatter _receiptTextFormatter;
    private readonly IReceiptPrinterSettingsStore? _receiptPrinterSettingsStore;
    private readonly Func<Task>? _onSuspendedOrderRecalledAsync;
    private readonly Func<InstallmentOrderSummary, Task>? _continueInstallmentPaymentAsync;
    private readonly Action? _returnToPos;
    private readonly ILocalizationService? _localization;
    private readonly ICashierSessionContext _cashierSessionContext;
    private readonly bool _enforcePermissions;
    private readonly IOperationAuditLogger? _operationAuditLogger;
    private readonly IOperationAuthorizationService? _operationAuthorizationService;
    private bool _suppressSelectedOrderLoad;
    private bool _suppressSourceAutoLoad;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _dateFilterText = string.Empty;

    [ObservableProperty]
    private DateTime? _dateFrom = DateTime.Today;

    [ObservableProperty]
    private DateTime? _dateTo = DateTime.Today;

    [ObservableProperty]
    private string _storeFilterText = string.Empty;

    [ObservableProperty]
    private string _terminalFilterText = string.Empty;

    [ObservableProperty]
    private TerminalFilterOption? _selectedTerminalOption;

    [ObservableProperty]
    private HistorySourceOption? _selectedSourceOption;

    [ObservableProperty]
    private HistoryOrderListItem? _selectedOrder;

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

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private PosSessionState _session = new("HB POS", "1002", "Main Branch", "Terminal 04", "C001", "Alice", false, 0);

    public TransactionHistoryViewModel()
        : this(null, null, null, null, null, null, null, null, null, null, false, null, null, null, null, initialize: true)
    {
    }

    public TransactionHistoryViewModel(ILocalOrderRepository orderRepository)
        : this(new ReceiptQueryService(orderRepository), null, null, null, null, null, null, null, null, null, false, null, null, null, null, initialize: true)
    {
    }

    public TransactionHistoryViewModel(IReceiptQueryService receiptQueryService)
        : this(receiptQueryService, null, null, null, null, null, null, null, null, null, false, null, null, null, null, initialize: true)
    {
    }

    public TransactionHistoryViewModel(
        IReceiptQueryService receiptQueryService,
        ISuspendedOrderService? suspendedOrderService,
        IRemoteOrderHistoryService? remoteOrderHistoryService,
        PosSessionState session,
        Func<Task>? onSuspendedOrderRecalledAsync = null,
        Action? returnToPos = null,
        ILocalizationService? localization = null,
        IReceiptTextFormatter? receiptTextFormatter = null,
        IReceiptPrinterSettingsStore? receiptPrinterSettingsStore = null,
        ICashierSessionContext? cashierSessionContext = null,
        bool enforcePermissionsWhenNoCashier = false,
        IInstallmentOrderService? installmentOrderService = null,
        Func<InstallmentOrderSummary, Task>? continueInstallmentPaymentAsync = null,
        IOperationAuditLogger? operationAuditLogger = null,
        IOperationAuthorizationService? operationAuthorizationService = null)
        : this(receiptQueryService, suspendedOrderService, remoteOrderHistoryService, session, onSuspendedOrderRecalledAsync, returnToPos, localization, receiptTextFormatter, receiptPrinterSettingsStore, cashierSessionContext, enforcePermissionsWhenNoCashier, installmentOrderService, continueInstallmentPaymentAsync, operationAuditLogger, operationAuthorizationService, initialize: true)
    {
    }

    private TransactionHistoryViewModel(
        IReceiptQueryService? receiptQueryService,
        ISuspendedOrderService? suspendedOrderService,
        IRemoteOrderHistoryService? remoteOrderHistoryService,
        PosSessionState? session,
        Func<Task>? onSuspendedOrderRecalledAsync,
        Action? returnToPos,
        ILocalizationService? localization,
        IReceiptTextFormatter? receiptTextFormatter,
        IReceiptPrinterSettingsStore? receiptPrinterSettingsStore,
        ICashierSessionContext? cashierSessionContext,
        bool enforcePermissionsWhenNoCashier,
        IInstallmentOrderService? installmentOrderService,
        Func<InstallmentOrderSummary, Task>? continueInstallmentPaymentAsync,
        IOperationAuditLogger? operationAuditLogger,
        IOperationAuthorizationService? operationAuthorizationService,
        bool initialize)
    {
        _receiptQueryService = receiptQueryService;
        _suspendedOrderService = suspendedOrderService;
        _remoteOrderHistoryService = remoteOrderHistoryService;
        _installmentOrderService = installmentOrderService ?? NoopInstallmentOrderService.Instance;
        _onSuspendedOrderRecalledAsync = onSuspendedOrderRecalledAsync;
        _continueInstallmentPaymentAsync = continueInstallmentPaymentAsync;
        _returnToPos = returnToPos;
        _localization = localization;
        _receiptTextFormatter = receiptTextFormatter ?? new ReceiptTextFormatter();
        _receiptPrinterSettingsStore = receiptPrinterSettingsStore;
        _cashierSessionContext = cashierSessionContext ?? new CashierSessionContext();
        _enforcePermissions = enforcePermissionsWhenNoCashier;
        _operationAuditLogger = operationAuditLogger;
        _operationAuthorizationService = operationAuthorizationService;
        if (_localization is not null)
        {
            _localization.CultureChanged += OnCultureChanged;
        }

        if (session is not null)
        {
            Session = session;
            if (session.CashierSession is not null)
            {
                _cashierSessionContext.SetCurrent(session.CashierSession);
            }

            StoreFilterText = $"{session.StoreName} ({session.StoreCode})";
            TerminalFilterText = session.DeviceCode;
        }

        RefreshTerminalOptions(selectAllTerminals: session is null);

        RefreshSourceOptions(TransactionHistorySource.LocalOrders);

        LoadCommand = new AsyncRelayCommand(() => LoadAsync());
        ReturnToPosCommand = new RelayCommand(ReturnToPos, CanReturnToPos);
        RecallSelectedCommand = new AsyncRelayCommand(RecallSelectedAsync, CanRecallSelected);
        RecallOrderCommand = new AsyncRelayCommand<HistoryOrderListItem>(RecallOrderAsync, CanRecallOrder);
        ContinueInstallmentPaymentCommand = new AsyncRelayCommand<HistoryOrderListItem>(ContinueInstallmentPaymentAsync, CanContinueInstallmentPayment);
        ConfirmInstallmentPickupCommand = new AsyncRelayCommand<HistoryOrderListItem>(ConfirmInstallmentPickupAsync, CanConfirmInstallmentPickup);
        ReprintCommand = new AsyncRelayCommand(ReprintSelectedAsync, CanReprintSelected);
        RefundCommand = new RelayCommand(() => { }, () => false);
    }

    public event EventHandler? ReprintRequested;

    public ObservableCollection<HistorySourceOption> SourceOptions { get; } = [];

    public ObservableCollection<TerminalFilterOption> TerminalOptions { get; } = [];

    public ObservableCollection<HistoryOrderListItem> Orders { get; } = [];

    public ObservableCollection<ReceiptPreviewLine> ReceiptLines { get; } = [];

    public ObservableCollection<ReceiptPaymentLine> Payments { get; } = [];

    public ObservableCollection<ReceiptPreviewRow> ReceiptPreviewRows { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }

    public IRelayCommand ReturnToPosCommand { get; }

    public IAsyncRelayCommand RecallSelectedCommand { get; }

    public IAsyncRelayCommand<HistoryOrderListItem> RecallOrderCommand { get; }

    public IAsyncRelayCommand<HistoryOrderListItem> ContinueInstallmentPaymentCommand { get; }

    public IAsyncRelayCommand<HistoryOrderListItem> ConfirmInstallmentPickupCommand { get; }

    public IRelayCommand ReprintCommand { get; }

    public IRelayCommand RefundCommand { get; }

    public TransactionHistorySource SelectedSource => SelectedSourceOption?.Source ?? TransactionHistorySource.LocalOrders;

    public bool IsRecallVisible => SelectedOrder?.CanRecall == true;

    public bool IsReprintVisible => CanReprintSelected();

    public bool IsContinueInstallmentPaymentVisible => CanContinueInstallmentPayment(SelectedOrder);

    public bool IsConfirmInstallmentPickupVisible => CanConfirmInstallmentPickup(SelectedOrder);

    public bool IsLocalSourceSelected
    {
        get => SelectedSource == TransactionHistorySource.LocalOrders;
        set
        {
            if (value)
            {
                SetSelectedSource(TransactionHistorySource.LocalOrders);
            }
        }
    }

    public bool IsOnlineSourceSelected
    {
        get => SelectedSource == TransactionHistorySource.RemoteOrders;
        set
        {
            if (value)
            {
                SetSelectedSource(TransactionHistorySource.RemoteOrders);
            }
        }
    }

    public bool IsInstallmentSourceSelected
    {
        get => SelectedSource == TransactionHistorySource.InstallmentOrders;
        set
        {
            if (value)
            {
                SetSelectedSource(TransactionHistorySource.InstallmentOrders);
            }
        }
    }

    public bool IsStandardSourceSelected => !IsInstallmentSourceSelected;

    public string TitleText => T("TransactionHistory");

    public string SearchHintText => T("history.search");

    public string ReceiptPreviewLabel => T("success.receiptPreview");

    public string ReprintLabel => T("history.reprint");

    public string RefundLabel => T("history.refund");

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        StatusMessage = string.Empty;
        try
        {
            var orders = SelectedSource switch
            {
                TransactionHistorySource.RemoteOrders => await LoadRemoteOrdersAsync(cancellationToken),
                TransactionHistorySource.InstallmentOrders => await LoadInstallmentOrdersAsync(cancellationToken),
                _ => await LoadLocalAndSuspendedOrdersAsync(cancellationToken)
            };

            Orders.ReplaceWith(orders);
            _suppressSelectedOrderLoad = true;
            SelectedOrder = Orders.FirstOrDefault();
            _suppressSelectedOrderLoad = false;

            if (SelectedOrder is null)
            {
                ClearReceiptPreview();
                return;
            }

            await LoadSelectedReceiptAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Orders.Clear();
            ClearReceiptPreview();
            StatusMessage = ex.Message;
        }
    }

    public Task ShowSuspendedOrdersAsync(CancellationToken cancellationToken = default)
    {
        _suppressSourceAutoLoad = true;
        SelectedSourceOption = SourceOptions.First(x => x.Source == TransactionHistorySource.LocalOrders);
        _suppressSourceAutoLoad = false;
        return LoadAsync(cancellationToken);
    }

    partial void OnSelectedSourceOptionChanged(HistorySourceOption? value)
    {
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(IsRecallVisible));
        OnPropertyChanged(nameof(IsReprintVisible));
        OnPropertyChanged(nameof(IsLocalSourceSelected));
        OnPropertyChanged(nameof(IsOnlineSourceSelected));
        OnPropertyChanged(nameof(IsInstallmentSourceSelected));
        OnPropertyChanged(nameof(IsStandardSourceSelected));
        OnPropertyChanged(nameof(IsContinueInstallmentPaymentVisible));
        OnPropertyChanged(nameof(IsConfirmInstallmentPickupVisible));
        ReprintCommand?.NotifyCanExecuteChanged();
        RecallSelectedCommand?.NotifyCanExecuteChanged();
        RecallOrderCommand?.NotifyCanExecuteChanged();
        ContinueInstallmentPaymentCommand?.NotifyCanExecuteChanged();
        ConfirmInstallmentPickupCommand?.NotifyCanExecuteChanged();
        if (!_suppressSourceAutoLoad)
        {
            _ = LoadAsync(CancellationToken.None);
        }
    }

    partial void OnSelectedOrderChanged(HistoryOrderListItem? value)
    {
        ReprintCommand?.NotifyCanExecuteChanged();
        RecallSelectedCommand?.NotifyCanExecuteChanged();
        RecallOrderCommand?.NotifyCanExecuteChanged();
        ContinueInstallmentPaymentCommand?.NotifyCanExecuteChanged();
        ConfirmInstallmentPickupCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsRecallVisible));
        OnPropertyChanged(nameof(IsReprintVisible));
        OnPropertyChanged(nameof(IsContinueInstallmentPaymentVisible));
        OnPropertyChanged(nameof(IsConfirmInstallmentPickupVisible));

        if (_suppressSelectedOrderLoad)
        {
            return;
        }

        _ = LoadSelectedReceiptAsync(CancellationToken.None);
    }

    partial void OnSessionChanged(PosSessionState value)
    {
        if (value.CashierSession is not null)
        {
            _cashierSessionContext.SetCurrent(value.CashierSession);
        }

        StoreFilterText = $"{value.StoreName} ({value.StoreCode})";
        RefreshTerminalOptions(SelectedTerminalOption?.DeviceCode is null);
        ConfirmInstallmentPickupCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsConfirmInstallmentPickupVisible));
    }

    partial void OnSelectedTerminalOptionChanged(TerminalFilterOption? value)
    {
        TerminalFilterText = value?.DeviceCode ?? T("history.allTerminals");
    }

    private async Task<IReadOnlyList<HistoryOrderListItem>> LoadLocalAndSuspendedOrdersAsync(CancellationToken cancellationToken)
    {
        var localOrdersTask = LoadLocalOrdersAsync(cancellationToken);
        var suspendedOrdersTask = LoadSuspendedOrdersAsync(cancellationToken);
        await Task.WhenAll(localOrdersTask, suspendedOrdersTask);

        return localOrdersTask.Result
            .Concat(suspendedOrdersTask.Result)
            .OrderByDescending(order => order.OccurredAt)
            .ToList();
    }

    private async Task<IReadOnlyList<HistoryOrderListItem>> LoadLocalOrdersAsync(CancellationToken cancellationToken)
    {
        if (_receiptQueryService is null)
        {
            return [];
        }

        var query = new LocalOrderHistoryQuery(
            ParseDateFrom(DateFrom),
            ParseDateTo(DateTo),
            SelectedTerminalDeviceCode,
            NormalizeKeyword(SearchText));
        var orders = await _receiptQueryService.GetRecentOrdersAsync(query, 100, cancellationToken);
        return orders
            .Select(order => new HistoryOrderListItem(
                order.OrderGuid,
                TransactionHistorySource.LocalOrders,
                order.StoreCode,
                order.DeviceCode,
                order.CashierName,
                order.SoldAt,
                order.TotalAmount,
                order.DiscountAmount,
                order.ActualAmount,
                order.LineCount,
                order.PaymentSummary,
                order.StatusLabel))
            .ToList();
    }

    private async Task<IReadOnlyList<HistoryOrderListItem>> LoadSuspendedOrdersAsync(CancellationToken cancellationToken)
    {
        if (_suspendedOrderService is null)
        {
            return [];
        }

        var orders = await _suspendedOrderService.GetPendingOrdersAsync(
            Session.StoreCode,
            SelectedTerminalDeviceCode,
            NormalizeKeyword(SearchText),
            100,
            cancellationToken);
        var from = ParseDateFrom(DateFrom);
        var to = ParseDateTo(DateTo);
        return orders
            .Where(order => from is null || order.SuspendedAt >= from.Value)
            .Where(order => to is null || order.SuspendedAt <= to.Value)
            .Select(order => new HistoryOrderListItem(
                order.SuspendedOrderGuid,
                TransactionHistorySource.LocalOrders,
                order.StoreCode,
                order.DeviceCode,
                order.CashierName,
                order.SuspendedAt,
                order.TotalAmount,
                order.DiscountAmount,
                order.ActualAmount,
                order.LineCount,
                T("history.payment.suspended"),
                T("history.status.pendingRecall"),
                IsSuspendedOrder: true,
                CanRecall: true))
            .ToList();
    }

    private async Task<IReadOnlyList<HistoryOrderListItem>> LoadRemoteOrdersAsync(CancellationToken cancellationToken)
    {
        if (_remoteOrderHistoryService is null)
        {
            return [];
        }

        var result = await _remoteOrderHistoryService.QueryAsync(
            new RemoteOrderHistoryQuery(
                Session.StoreCode,
                ParseDateFrom(DateFrom),
                ParseDateTo(DateTo),
                SelectedTerminalDeviceCode,
                NormalizeKeyword(SearchText),
                100),
            cancellationToken);
        return result.Orders.Select(order => new HistoryOrderListItem(
            order.OrderGuid,
            TransactionHistorySource.RemoteOrders,
            order.StoreCode,
            order.DeviceCode,
            order.CashierName,
            order.SoldAt,
            order.TotalAmount,
            order.DiscountAmount,
            order.ActualAmount,
            order.LineCount,
            order.PaymentSummary,
            order.StatusLabel)).ToList();
    }

    private async Task<IReadOnlyList<HistoryOrderListItem>> LoadInstallmentOrdersAsync(CancellationToken cancellationToken)
    {
        var orders = await _installmentOrderService.SearchAsync(
            Session,
            NormalizeKeyword(SearchText),
            cancellationToken);
        var from = ParseDateFrom(DateFrom);
        var to = ParseDateTo(DateTo);
        return orders
            .Where(order => SelectedTerminalDeviceCode is null ||
                string.Equals(order.DeviceCode, SelectedTerminalDeviceCode, StringComparison.OrdinalIgnoreCase))
            .Where(order => from is null || order.UpdatedAt >= from.Value)
            .Where(order => to is null || order.UpdatedAt <= to.Value)
            .OrderByDescending(order => order.UpdatedAt)
            .Select(order => new HistoryOrderListItem(
                order.OrderId,
                TransactionHistorySource.InstallmentOrders,
                Session.StoreCode,
                order.DeviceCode,
                order.CustomerName,
                order.UpdatedAt,
                order.TotalAmount,
                0m,
                order.OutstandingAmount,
                0,
                $"{T("history.installment.paid")}: {order.PaidAmount:C2}",
                order.Status,
                InstallmentOrder: order,
                IsInstallmentOrder: true,
                CanContinueInstallmentPayment: order.CanAddRepayment,
                CanConfirmInstallmentPickup: order.CanConfirmPickup,
                CustomerPhone: order.CustomerPhone))
            .ToList();
    }

    private async Task LoadSelectedReceiptAsync(CancellationToken cancellationToken)
    {
        if (SelectedOrder is null)
        {
            ClearReceiptPreview();
            return;
        }

        if (SelectedOrder.IsInstallmentOrder)
        {
            // 中文注释：分期历史只更新屏幕预览，不触发实际打印。
            var installmentDetails = await LoadInstallmentPreviewDetailsAsync(SelectedOrder.OrderGuid, cancellationToken);
            if (installmentDetails is not null)
            {
                // 中文注释：有本地分期快照时复用正式小票映射，右侧预览才能显示正常抬头和提货信息。
                var installmentReceipt = InstallmentReceiptMapper.CreateReceipt(installmentDetails);
                ReceiptLines.ReplaceWith(installmentReceipt.Lines);
                Payments.ReplaceWith(installmentReceipt.Payments);
                ReceiptPreviewRows.ReplaceWith(BuildPreviewRows(
                    installmentReceipt,
                    await LoadPreviewSettingsAsync(cancellationToken)));
                PreviewSubtotal = installmentReceipt.TotalAmount;
                PreviewDiscount = installmentReceipt.DiscountAmount;
                PreviewTotal = installmentReceipt.ActualAmount;
                PreviewOrderId = installmentReceipt.TransactionIdDisplay;
                PreviewSoldAt = installmentReceipt.SoldAtDisplay;
                return;
            }

            ReceiptLines.Clear();
            Payments.Clear();
            ReceiptPreviewRows.ReplaceWith(BuildInstallmentPreviewRows(SelectedOrder, installmentDetails));
            PreviewSubtotal = SelectedOrder.TotalAmount;
            PreviewDiscount = SelectedOrder.InstallmentOrder?.PaidAmount ?? 0m;
            PreviewTotal = SelectedOrder.ActualAmount;
            PreviewOrderId = SelectedOrder.DisplayOrderId;
            PreviewSoldAt = SelectedOrder.SoldAtDisplay;
            return;
        }

        ReceiptDetails? receipt = SelectedOrder.IsSuspendedOrder
            ? await GetSuspendedReceiptAsync(SelectedOrder.OrderGuid, cancellationToken)
            : SelectedOrder.Source switch
            {
                TransactionHistorySource.RemoteOrders => _remoteOrderHistoryService is null
                    ? null
                    : await _remoteOrderHistoryService.GetDetailsAsync(SelectedOrder.OrderGuid, cancellationToken),
                _ => _receiptQueryService is null ? null : await _receiptQueryService.GetReceiptAsync(SelectedOrder.OrderGuid, cancellationToken)
            };

        if (receipt is null)
        {
            ClearReceiptPreview();
            return;
        }

        ReceiptLines.ReplaceWith(receipt.Lines);
        Payments.ReplaceWith(receipt.Payments);
        ReceiptPreviewRows.ReplaceWith(BuildPreviewRows(
            receipt,
            await LoadPreviewSettingsAsync(cancellationToken)));
        PreviewSubtotal = receipt.TotalAmount;
        PreviewDiscount = receipt.DiscountAmount;
        PreviewTotal = receipt.ActualAmount;
        PreviewOrderId = receipt.TransactionIdDisplay;
        PreviewSoldAt = receipt.SoldAtDisplay;
    }

    private async Task<ReceiptDetails?> GetSuspendedReceiptAsync(Guid orderGuid, CancellationToken cancellationToken)
    {
        if (_suspendedOrderService is null)
        {
            return null;
        }

        var details = await _suspendedOrderService.GetOrderAsync(orderGuid, cancellationToken);
        return details is null ? null : CreateSuspendedReceipt(details);
    }

    private bool CanRecallSelected()
    {
        return CanRecallOrder(SelectedOrder);
    }

    private bool CanRecallOrder(HistoryOrderListItem? order)
    {
        return order?.CanRecall == true;
    }

    private bool CanContinueInstallmentPayment(HistoryOrderListItem? order)
    {
        return _continueInstallmentPaymentAsync is not null &&
            order?.InstallmentOrder is not null &&
            order.CanContinueInstallmentPayment;
    }

    private bool CanConfirmInstallmentPickup(HistoryOrderListItem? order)
    {
        return Session.IsOnline &&
            order?.InstallmentOrder is not null &&
            order.CanConfirmInstallmentPickup;
    }

    private bool CanReprintSelected()
    {
        return SelectedOrder is { IsSuspendedOrder: false, Source: TransactionHistorySource.LocalOrders };
    }

    private async Task RecallSelectedAsync()
    {
        await RecallOrderAsync(SelectedOrder);
    }

    private async Task RecallOrderAsync(HistoryOrderListItem? order)
    {
        var orderSnapshot = order;
        using var authorization = await AuthorizeAsync(Permissions.PosTerminal.History.Recall, "recall-order");
        if (authorization is null)
        {
            return;
        }
        using var authorizationActivation = authorization.Activate();

        if (!CanRecallOrder(orderSnapshot) || _suspendedOrderService is null)
        {
            return;
        }

        var correlation = OperationAuditEvents.CreateCorrelation();
        var recallCompleted = false;
        try
        {
            var recalledOrder = await _suspendedOrderService.RecallOrderAsync(orderSnapshot!.OrderGuid);
            OperationAuditEvents.RecordCartChange(
                _operationAuditLogger,
                OperationAuditTypes.OrderRecall,
                Session,
                new OperationAuditCartSnapshot(0m, 0m, 0m, []),
                OperationAuditEvents.CaptureSuspendedOrder(recalledOrder),
                reasonCode: "SUSPENDED_ORDER",
                orderGuid: orderSnapshot.OrderGuid.ToString("D"),
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            recallCompleted = true;
            if (_onSuspendedOrderRecalledAsync is not null)
            {
                await _onSuspendedOrderRecalledAsync();
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            if (!recallCompleted)
            {
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    OperationAuditTypes.OrderRecall,
                    "Failed",
                    Session,
                    reasonCode: "SUSPENDED_ORDER",
                    safeMessage: ex.GetType().Name,
                    orderGuid: orderSnapshot?.OrderGuid.ToString("D"),
                    correlationId: correlation.CorrelationId,
                    traceId: correlation.TraceId);
            }

            ConsoleLog.WriteError(
                "OperationAudit",
                $"order recall failed error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            StatusMessage = ex.Message;
        }
    }

    private async Task<LocalInstallmentOrder?> LoadInstallmentPreviewDetailsAsync(Guid installmentGuid, CancellationToken cancellationToken)
    {
        try
        {
            return await _installmentOrderService.GetLocalOrderAsync(installmentGuid, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private IReadOnlyList<ReceiptPreviewRow> BuildInstallmentPreviewRows(HistoryOrderListItem order, LocalInstallmentOrder? details)
    {
        var summary = order.InstallmentOrder;
        var totalAmount = details?.TotalAmount ?? summary?.TotalAmount ?? order.TotalAmount;
        var paidAmount = details?.PaidAmount ?? summary?.PaidAmount ?? 0m;
        var outstandingAmount = details?.BalanceAmount ?? summary?.OutstandingAmount ?? order.ActualAmount;
        var customerName = details?.CustomerName ?? summary?.CustomerName ?? order.CashierName;
        var customerPhone = details?.CustomerPhone ?? summary?.CustomerPhone ?? order.CustomerPhone;
        var status = summary?.Status ?? order.StatusLabel;

        var rows = new List<ReceiptPreviewRow>
        {
            new(ReceiptPreviewRowKind.Text, "===== TAX INVOICE =====", ReceiptPrintAlignment.Center, true),
            new(ReceiptPreviewRowKind.Text, FitPreviewColumns(T("installment.center.column.orderNumber"), order.DisplayOrderId), ReceiptPrintAlignment.Left, true),
            new(ReceiptPreviewRowKind.Text, $"{T("Customer")}: {customerName}"),
            new(ReceiptPreviewRowKind.Text, $"{T("Phone")}: {customerPhone}"),
            new(ReceiptPreviewRowKind.Separator, new string('-', 42))
        };

        if (details?.Lines.Count > 0)
        {
            foreach (var line in details.Lines)
            {
                rows.Add(new ReceiptPreviewRow(ReceiptPreviewRowKind.Text, FitPreviewColumns(line.DisplayName, FormatMoney(line.ActualAmount))));
                rows.Add(new ReceiptPreviewRow(
                    ReceiptPreviewRowKind.Text,
                    $"  {line.Quantity.ToString("0.##", CultureInfo.InvariantCulture)} x {FormatMoney(line.UnitPrice)}"));
            }

            rows.Add(new ReceiptPreviewRow(ReceiptPreviewRowKind.Separator, new string('-', 42)));
        }

        rows.Add(new ReceiptPreviewRow(ReceiptPreviewRowKind.Text, FitPreviewColumns(T("Total"), FormatMoney(totalAmount)), ReceiptPrintAlignment.Left, true));
        rows.Add(new ReceiptPreviewRow(ReceiptPreviewRowKind.Text, FitPreviewColumns(T("history.installment.paid"), FormatMoney(paidAmount)), ReceiptPrintAlignment.Left, true));
        rows.Add(new ReceiptPreviewRow(ReceiptPreviewRowKind.Text, FitPreviewColumns(T("payment.installment.outstanding"), FormatMoney(outstandingAmount)), ReceiptPrintAlignment.Left, true));
        rows.Add(new ReceiptPreviewRow(ReceiptPreviewRowKind.Text, FitPreviewColumns(T("common.status"), status)));

        var recordedPayments = details?.Payments
            .Where(payment => payment.Status == InstallmentPaymentStatus.Recorded)
            .ToList();
        if (recordedPayments is { Count: > 0 })
        {
            rows.Add(new ReceiptPreviewRow(ReceiptPreviewRowKind.Separator, new string('-', 42)));
            rows.Add(new ReceiptPreviewRow(ReceiptPreviewRowKind.Text, "Payments", ReceiptPrintAlignment.Center, true));
            foreach (var payment in recordedPayments)
            {
                rows.Add(new ReceiptPreviewRow(ReceiptPreviewRowKind.Text, FitPreviewColumns(GetPaymentMethodLabel(payment.Method), FormatMoney(payment.Amount))));
            }
        }

        return rows;
    }

    private async Task ContinueInstallmentPaymentAsync(HistoryOrderListItem? order)
    {
        if (!CanContinueInstallmentPayment(order) || _continueInstallmentPaymentAsync is null)
        {
            return;
        }

        await _continueInstallmentPaymentAsync(order!.InstallmentOrder!);
    }

    private async Task ConfirmInstallmentPickupAsync(HistoryOrderListItem? order)
    {
        if (!CanConfirmInstallmentPickup(order))
        {
            return;
        }

        var orderSnapshot = order;
        using var authorization = await AuthorizeAsync(Permissions.PosTerminal.Installments.ConfirmPickup, "confirm-installment-pickup");
        if (authorization is null)
        {
            return;
        }
        using var authorizationActivation = authorization.Activate();

        // 中文注释：历史页提货入口复用分期中心同一接口，成功后刷新列表和右侧预览状态。
        var result = await _installmentOrderService.ConfirmPickupAsync(orderSnapshot!.InstallmentOrder!.OrderId, Session);
        StatusMessage = result.Message;
        if (result.Succeeded)
        {
            var message = result.Message;
            await LoadAsync();
            if (string.IsNullOrWhiteSpace(StatusMessage))
            {
                StatusMessage = message;
            }
        }
    }

    private async Task ReprintSelectedAsync()
    {
        using var authorization = await AuthorizeAsync(Permissions.PosTerminal.History.Reprint, "reprint-selected");
        if (authorization is null)
        {
            return;
        }
        using var authorizationActivation = authorization.Activate();

        ReprintRequested?.Invoke(this, EventArgs.Empty);
    }

    private Task<ViewModelAuthorizationGrant?> AuthorizeAsync(string permissionCode, string action) =>
        ViewModelOperationAuthorization.AuthorizeAsync(
            _operationAuthorizationService,
            TryRequirePermission,
            permissionCode,
            "transaction-history",
            action,
            Session);

    private bool TryRequirePermission(string permissionCode)
    {
        if ((!_enforcePermissions && _cashierSessionContext.CurrentSession is null && Session.CashierSession is null) ||
            _cashierSessionContext.RequirePermission(permissionCode, out var message))
        {
            return true;
        }

        var operationType = permissionCode switch
        {
            Permissions.PosTerminal.History.Recall => OperationAuditTypes.OrderRecall,
            Permissions.PosTerminal.History.Reprint => OperationAuditTypes.ReceiptReprint,
            _ => null
        };
        if (operationType is not null)
        {
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                operationType,
                "Denied",
                Session,
                reasonCode: "PERMISSION_DENIED",
                safeMessage: message,
                orderGuid: SelectedOrder?.OrderGuid.ToString("D"));
        }

        StatusMessage = message;
        return false;
    }

    private static ReceiptDetails CreateSuspendedReceipt(SuspendedOrder order)
    {
        return new ReceiptDetails(
            order.SuspendedOrderGuid,
            order.StoreCode,
            order.DeviceCode,
            order.CashierName,
            order.SuspendedAt,
            order.TotalAmount,
            order.DiscountAmount,
            order.ActualAmount,
            order.Lines.Select(line => new ReceiptPreviewLine(
                line.DisplayName,
                line.LookupCode,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.ActualAmount)).ToList(),
            []);
    }

    private void ReturnToPos()
    {
        _returnToPos?.Invoke();
    }

    private bool CanReturnToPos()
    {
        return _returnToPos is not null;
    }

    private void ClearReceiptPreview()
    {
        ReceiptLines.Clear();
        Payments.Clear();
        ReceiptPreviewRows.Clear();
        PreviewSubtotal = 0m;
        PreviewDiscount = 0m;
        PreviewTotal = 0m;
        PreviewOrderId = "-";
        PreviewSoldAt = "-";
    }

    private static string? NormalizeKeyword(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private string? SelectedTerminalDeviceCode => SelectedTerminalOption?.DeviceCode;

    private void SetSelectedSource(TransactionHistorySource source)
    {
        if (SelectedSource == source)
        {
            return;
        }

        SelectedSourceOption = SourceOptions.First(option => option.Source == source);
    }

    private void RefreshSourceOptions(TransactionHistorySource selectedSource)
    {
        _suppressSourceAutoLoad = true;
        SourceOptions.Clear();
        SourceOptions.Add(new HistorySourceOption(TransactionHistorySource.LocalOrders, T("history.source.local")));
        SourceOptions.Add(new HistorySourceOption(TransactionHistorySource.RemoteOrders, T("history.source.online")));
        SourceOptions.Add(new HistorySourceOption(TransactionHistorySource.InstallmentOrders, T("history.source.installments")));
        SelectedSourceOption = SourceOptions.First(option => option.Source == selectedSource);
        _suppressSourceAutoLoad = false;
    }

    private void RefreshTerminalOptions(bool selectAllTerminals)
    {
        var currentDeviceCode = Session.DeviceCode.Trim();
        TerminalOptions.Clear();
        var allTerminals = new TerminalFilterOption(null, T("history.allTerminals"));
        TerminalOptions.Add(allTerminals);

        TerminalFilterOption selected = allTerminals;
        if (!string.IsNullOrWhiteSpace(currentDeviceCode))
        {
            var currentTerminal = new TerminalFilterOption(currentDeviceCode, currentDeviceCode);
            TerminalOptions.Add(currentTerminal);
            selected = selectAllTerminals ? allTerminals : currentTerminal;
        }

        SelectedTerminalOption = selected;
        TerminalFilterText = selected.DeviceCode ?? T("history.allTerminals");
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RefreshSourceOptions(SelectedSource);
        RefreshTerminalOptions(SelectedTerminalOption?.DeviceCode is null);
        LocalizeSuspendedRows();
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SearchHintText));
        OnPropertyChanged(nameof(ReceiptPreviewLabel));
        OnPropertyChanged(nameof(ReprintLabel));
        OnPropertyChanged(nameof(RefundLabel));
        OnPropertyChanged(nameof(IsInstallmentSourceSelected));
        OnPropertyChanged(nameof(IsContinueInstallmentPaymentVisible));
    }

    private void LocalizeSuspendedRows()
    {
        if (Orders.Count == 0)
        {
            return;
        }

        var selectedOrderGuid = SelectedOrder?.OrderGuid;
        Orders.ReplaceWith(Orders.Select(order => order.IsSuspendedOrder
            ? order with
            {
                PaymentSummary = T("history.payment.suspended"),
                StatusLabel = T("history.status.pendingRecall")
            }
            : order).ToList());
        SelectedOrder = selectedOrderGuid is null
            ? Orders.FirstOrDefault()
            : Orders.FirstOrDefault(order => order.OrderGuid == selectedOrderGuid.Value);
    }

    private string T(string key)
    {
        if (_localization is not null)
        {
            return _localization.T(key);
        }

        return key switch
        {
            "TransactionHistory" => "Transaction History",
            "success.receiptPreview" => "Receipt Preview",
            "history.reprint" => "Reprint",
            "history.refund" => "Refund",
            "history.search" => "Search order, cashier, or terminal...",
            "history.allTerminals" => "All Terminals",
            "history.source.local" => "Local",
            "history.source.online" => "Online",
            "history.source.installments" => "Installments",
            "Customer" => "Customer",
            "Phone" => "Phone",
            "Total" => "Total",
            "common.status" => "Status",
            "payment.installment.outstanding" => "Outstanding",
            "history.installment.paid" => "Paid",
            "history.installment.continuePayment" => "Continue payment",
            "history.payment.suspended" => "Suspended",
            "history.status.pendingRecall" => "Pending recall",
            "installment.center.column.orderNumber" => "Order No.",
            "payment.method.cash" => "Cash",
            "payment.method.card" => "Credit/Debit Card",
            "payment.method.voucher" => "Voucher",
            _ => key
        };
    }

    private string GetPaymentMethodLabel(PaymentMethodKind method)
    {
        return method switch
        {
            PaymentMethodKind.Cash => T("payment.method.cash"),
            PaymentMethodKind.Card => T("payment.method.card"),
            PaymentMethodKind.Voucher => T("payment.method.voucher"),
            _ => method.ToString()
        };
    }

    private static string FormatMoney(decimal amount)
    {
        return string.Create(CultureInfo.InvariantCulture, $"${amount:0.00}");
    }

    private static string FitPreviewColumns(string left, string right)
    {
        const int lineWidth = 42;
        left = left.Length > 24 ? left[..24] : left;
        right = right.Length > 16 ? right[..16] : right;
        return left + new string(' ', Math.Max(1, lineWidth - left.Length - right.Length)) + right;
    }

    private static DateTimeOffset? ParseDateFrom(DateTime? value)
    {
        return value is null ? null : new DateTimeOffset(value.Value.Date);
    }

    private static DateTimeOffset? ParseDateTo(DateTime? value)
    {
        return value is null ? null : new DateTimeOffset(value.Value.Date.AddDays(1).AddTicks(-1));
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

    public void Dispose()
    {
        if (_localization is not null)
        {
            _localization.CultureChanged -= OnCultureChanged;
        }

        ReprintRequested = null;
    }
}
