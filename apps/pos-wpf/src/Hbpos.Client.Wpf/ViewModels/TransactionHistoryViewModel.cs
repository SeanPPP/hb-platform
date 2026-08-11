using System.Collections.ObjectModel;
using System.Globalization;
using BlazorApp.Shared.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.HeldOrders;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;
using HeldClaimStatus = Hbpos.Client.Wpf.Models.SharedHeldOrderClaimStatus;
using HeldPublicationStatus = Hbpos.Client.Wpf.Models.SharedHeldOrderPublicationStatus;
using HeldServerStatus = Hbpos.Contracts.HeldOrders.SharedHeldOrderStatus;

namespace Hbpos.Client.Wpf.ViewModels;

public enum TransactionHistorySource
{
    LocalOrders,
    RemoteOrders,
    InstallmentOrders,
    HeldOrders
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
    string CustomerPhone = "",
    string SyncStatus = "",
    CultureInfo? DisplayCulture = null,
    bool IsHeldOrder = false,
    HeldOrderBadgeKind HeldBadgeKind = HeldOrderBadgeKind.LocalHold,
    string HeldStatusDetail = "",
    Guid? HeldClaimId = null,
    bool CanForceRelease = false,
    bool CanRemoteRecall = false,
    bool CanOfflineRecall = false,
    bool CanLegacyRecall = false,
    bool CanDeleteHeldOrder = false)
{
    public RowSelectionState Selection { get; } = new();

    public bool CanReupload => Source == TransactionHistorySource.LocalOrders &&
        !IsSuspendedOrder &&
        !IsInstallmentOrder &&
        (SyncStatus.Equals("Synced", StringComparison.OrdinalIgnoreCase) ||
         SyncStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
         SyncStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase));

    public string ShortOrderId => OrderGuid.ToString("N")[..8].ToUpperInvariant();

    public string DisplayOrderId => InstallmentOrder?.OrderNumber ?? ShortOrderId;

    public string SoldAtDisplay => OccurredAt.ToLocalTime().ToString(
        "MMM dd, yyyy HH:mm",
        DisplayCulture ?? CultureInfo.GetCultureInfo(LocalizationService.DefaultCultureName));
}

public sealed partial class TransactionHistoryViewModel : ObservableObject, IDisposable
{
    private const int ReuploadBatchSize = 500;
    private static readonly TimeSpan HeldAutoRefreshInterval = TimeSpan.FromSeconds(10);

    private readonly IReceiptQueryService? _receiptQueryService;
    private readonly ISuspendedOrderService? _suspendedOrderService;
    private readonly IRemoteOrderHistoryService? _remoteOrderHistoryService;
    private readonly ISharedHeldOrderCoordinator? _sharedHeldOrderCoordinator;
    private readonly ISharedHeldOrderApiClient? _sharedHeldOrderApiClient;
    private readonly ISharedHeldOrderRepository? _sharedHeldOrderRepository;
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
    private readonly IOrderUploadExecutionService _orderUploadExecutionService;
    private readonly IConfirmationDialogService? _confirmationDialogService;
    private readonly TimeProvider _timeProvider;
    private bool _suppressSelectedOrderLoad;
    private bool _suppressSourceAutoLoad;
    private bool _disposed;
    private bool _isScreenVisible;
    private Task<IReadOnlyList<HistoryOrderListItem>>? _heldLoadTask;
    private ITimer? _heldAutoRefreshTimer;

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
    private string _heldOrdersRemoteStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isForceReleaseReasonPromptOpen;

    [ObservableProperty]
    private string _forceReleaseReason = string.Empty;

    [ObservableProperty]
    private HistoryOrderListItem? _forceReleaseCandidate;

    [ObservableProperty]
    private bool _isReuploading;

    [ObservableProperty]
    private PosSessionState _session = new("HB POS", "1002", "Main Branch", "Terminal 04", "C001", "Alice", false, 0);

    public TransactionHistoryViewModel()
        : this(null, null, null, null, null, null, null, null, null, null, false, null, null, null, null, null, null, null, null, null, null, initialize: true)
    {
    }

    public TransactionHistoryViewModel(ILocalOrderRepository orderRepository)
        : this(new ReceiptQueryService(orderRepository), null, null, null, null, null, null, null, null, null, false, null, null, null, null, null, null, null, null, null, null, initialize: true)
    {
    }

    public TransactionHistoryViewModel(IReceiptQueryService receiptQueryService)
        : this(receiptQueryService, null, null, null, null, null, null, null, null, null, false, null, null, null, null, null, null, null, null, null, null, initialize: true)
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
        IOperationAuthorizationService? operationAuthorizationService = null,
        IOrderUploadExecutionService? orderUploadExecutionService = null,
        IConfirmationDialogService? confirmationDialogService = null,
        ISharedHeldOrderCoordinator? sharedHeldOrderCoordinator = null,
        ISharedHeldOrderApiClient? sharedHeldOrderApiClient = null,
        ISharedHeldOrderRepository? sharedHeldOrderRepository = null,
        TimeProvider? timeProvider = null)
        : this(receiptQueryService, suspendedOrderService, remoteOrderHistoryService, session, onSuspendedOrderRecalledAsync, returnToPos, localization, receiptTextFormatter, receiptPrinterSettingsStore, cashierSessionContext, enforcePermissionsWhenNoCashier, installmentOrderService, continueInstallmentPaymentAsync, operationAuditLogger, operationAuthorizationService, orderUploadExecutionService, confirmationDialogService, sharedHeldOrderCoordinator, sharedHeldOrderApiClient, sharedHeldOrderRepository, timeProvider, initialize: true)
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
        IOrderUploadExecutionService? orderUploadExecutionService,
        IConfirmationDialogService? confirmationDialogService,
        ISharedHeldOrderCoordinator? sharedHeldOrderCoordinator,
        ISharedHeldOrderApiClient? sharedHeldOrderApiClient,
        ISharedHeldOrderRepository? sharedHeldOrderRepository,
        TimeProvider? timeProvider,
        bool initialize)
    {
        _receiptQueryService = receiptQueryService;
        _suspendedOrderService = suspendedOrderService;
        _remoteOrderHistoryService = remoteOrderHistoryService;
        _sharedHeldOrderCoordinator = sharedHeldOrderCoordinator;
        _sharedHeldOrderApiClient = sharedHeldOrderApiClient;
        _sharedHeldOrderRepository = sharedHeldOrderRepository;
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
        _orderUploadExecutionService = orderUploadExecutionService ?? NoopOrderUploadExecutionService.Instance;
        _confirmationDialogService = confirmationDialogService;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        SelectAllReuploadableCommand = new RelayCommand(SelectAllReuploadable, CanStartReupload);
        ReuploadSelectedCommand = new AsyncRelayCommand(ReuploadSelectedAsync, CanStartReupload);
        ReuploadDateRangeCommand = new AsyncRelayCommand(ReuploadDateRangeAsync, CanReuploadDateRange);
        DeleteHeldOrderCommand = new AsyncRelayCommand<HistoryOrderListItem>(DeleteHeldOrderAsync, CanDeleteHeldOrder);
        ForceReleaseHeldOrderCommand = new RelayCommand<HistoryOrderListItem>(RequestForceRelease, CanForceReleaseOrder);
        ConfirmForceReleaseCommand = new AsyncRelayCommand(ConfirmForceReleaseAsync, CanConfirmForceRelease);
        CancelForceReleaseCommand = new RelayCommand(CancelForceRelease, CanCancelForceRelease);
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

    public IRelayCommand SelectAllReuploadableCommand { get; }

    public IAsyncRelayCommand ReuploadSelectedCommand { get; }

    public IAsyncRelayCommand ReuploadDateRangeCommand { get; }

    public IAsyncRelayCommand<HistoryOrderListItem> DeleteHeldOrderCommand { get; }

    public IRelayCommand<HistoryOrderListItem> ForceReleaseHeldOrderCommand { get; }

    public IAsyncRelayCommand ConfirmForceReleaseCommand { get; }

    public IRelayCommand CancelForceReleaseCommand { get; }

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

    public bool IsHeldSourceSelected
    {
        get => SelectedSource == TransactionHistorySource.HeldOrders;
        set
        {
            if (value)
            {
                SetSelectedSource(TransactionHistorySource.HeldOrders);
            }
        }
    }

    public bool IsStandardSourceSelected => !IsInstallmentSourceSelected;

    public bool IsForceReleaseVisible => SelectedOrder?.CanForceRelease == true;

    public string HeldOrdersSourceLabel => T("history.source.held");

    public string DeleteHeldOrderLabel => T("history.held.delete");

    public string ForceReleaseLabel => T("history.held.forceRelease");

    public string ForceReleaseHeaderLabel => T("history.held.forceRelease");

    public string ForceReleaseReasonLabel => T("history.held.forceReleaseReason");

    public string ForceReleaseConfirmLabel => T("history.held.forceReleaseConfirm");

    public string ForceReleaseCancelLabel => T("history.held.forceReleaseCancel");

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
                TransactionHistorySource.HeldOrders => await LoadHeldOrdersAsync(cancellationToken),
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
        OnPropertyChanged(nameof(IsHeldSourceSelected));
        OnPropertyChanged(nameof(IsStandardSourceSelected));
        OnPropertyChanged(nameof(IsForceReleaseVisible));
        OnPropertyChanged(nameof(IsContinueInstallmentPaymentVisible));
        OnPropertyChanged(nameof(IsConfirmInstallmentPickupVisible));
        ForceReleaseHeldOrderCommand?.NotifyCanExecuteChanged();
        DeleteHeldOrderCommand?.NotifyCanExecuteChanged();
        ConfirmForceReleaseCommand?.NotifyCanExecuteChanged();
        CancelForceReleaseCommand?.NotifyCanExecuteChanged();
        ReprintCommand?.NotifyCanExecuteChanged();
        RecallSelectedCommand?.NotifyCanExecuteChanged();
        RecallOrderCommand?.NotifyCanExecuteChanged();
        ContinueInstallmentPaymentCommand?.NotifyCanExecuteChanged();
        ConfirmInstallmentPickupCommand?.NotifyCanExecuteChanged();
        NotifyReuploadCanExecuteChanged();
        UpdateHeldAutoRefresh();
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
        ForceReleaseHeldOrderCommand?.NotifyCanExecuteChanged();
        DeleteHeldOrderCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsRecallVisible));
        OnPropertyChanged(nameof(IsReprintVisible));
        OnPropertyChanged(nameof(IsForceReleaseVisible));
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
                order.StatusLabel,
                SyncStatus: order.SyncStatus,
                DisplayCulture: CurrentDisplayCulture))
            .ToList();
    }

    partial void OnDateFromChanged(DateTime? value) => ReuploadDateRangeCommand?.NotifyCanExecuteChanged();

    partial void OnDateToChanged(DateTime? value) => ReuploadDateRangeCommand?.NotifyCanExecuteChanged();

    partial void OnIsReuploadingChanged(bool value) => NotifyReuploadCanExecuteChanged();

    private void SelectAllReuploadable()
    {
        foreach (var order in Orders.Where(order => order.CanReupload))
        {
            order.Selection.IsSelected = true;
        }
    }

    private async Task ReuploadSelectedAsync()
    {
        var selected = Orders
            .Where(order => order.CanReupload && order.Selection.IsSelected)
            .Select(order => order.OrderGuid)
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        IsReuploading = true;
        try
        {
            var result = await _orderUploadExecutionService.ExecuteSelectedAsync(selected);
            await LoadAsync();
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                T("history.reuploadCompleted"),
                result.UploadedCount,
                result.FailedCount);
        }
        finally
        {
            IsReuploading = false;
        }
    }

    private bool CanStartReupload() => IsLocalSourceSelected && !IsReuploading;

    private bool CanReuploadDateRange() =>
        CanStartReupload() &&
        DateFrom is not null &&
        DateTo is not null &&
        DateFrom.Value.Date <= DateTo.Value.Date;

    private void NotifyReuploadCanExecuteChanged()
    {
        SelectAllReuploadableCommand?.NotifyCanExecuteChanged();
        ReuploadSelectedCommand?.NotifyCanExecuteChanged();
        ReuploadDateRangeCommand?.NotifyCanExecuteChanged();
    }

    private async Task ReuploadDateRangeAsync()
    {
        var dateFrom = DateFrom;
        var dateTo = DateTo;
        if (!CanReuploadDateRange() || dateFrom is null || dateTo is null)
        {
            return;
        }

        var soldFrom = ParseDateFrom(dateFrom)!.Value;
        var soldTo = ParseDateTo(dateTo)!.Value;

        IsReuploading = true;
        try
        {
            var orderGuids = await _orderUploadExecutionService.GetReuploadableOrderGuidsAsync(
                soldFrom,
                soldTo,
                SelectedTerminalDeviceCode);
            if (orderGuids.Count == 0)
            {
                StatusMessage = T("history.reuploadRangeEmpty");
                return;
            }

            var batchCount = (orderGuids.Count + ReuploadBatchSize - 1) / ReuploadBatchSize;
            if (_confirmationDialogService is null ||
                !await _confirmationDialogService.ConfirmOrderDateRangeReuploadAsync(
                    orderGuids.Count,
                    batchCount,
                    dateFrom.Value,
                    dateTo.Value))
            {
                StatusMessage = T("history.reuploadRangeCancelled");
                return;
            }

            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                T("history.reuploadRangeProgress"),
                orderGuids.Count,
                batchCount);
            var attemptedCount = 0;
            var uploadedCount = 0;
            var failedCount = 0;
            var wasInterrupted = false;
            foreach (var batch in orderGuids.Chunk(ReuploadBatchSize))
            {
                var result = await _orderUploadExecutionService.ExecuteSelectedAsync(batch);
                attemptedCount += result.AttemptedCount;
                uploadedCount += result.UploadedCount;
                failedCount += result.FailedCount;
                if (result.WasInterrupted)
                {
                    wasInterrupted = true;
                    break;
                }
            }

            await LoadAsync();
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                T(wasInterrupted ? "history.reuploadRangeInterrupted" : "history.reuploadRangeCompleted"),
                attemptedCount,
                uploadedCount,
                failedCount);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = T("history.reuploadRangeCancelled");
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, T("history.reuploadRangeFailed"), ex.Message);
        }
        finally
        {
            IsReuploading = false;
        }
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
                CanRecall: true,
                DisplayCulture: CurrentDisplayCulture))
            .ToList();
    }

    private sealed record LocalHeldRow(
        SuspendedOrderSummary Order,
        SharedHeldOrderPublication? Publication,
        HeldClaimStatus? ClaimStatus,
        Guid? ClaimId);

    /// <summary>
    /// 共享挂单源：合并本机待发布/已发布挂单与服务端 Pending 汇总，按 HoldGuid 去重。
    /// single-flight：并发刷新复用同一个进行中的任务；任何远端/本地错误都保留现有列表，
    /// 只通过 HeldOrdersRemoteStatusMessage 做非阻塞提示。
    /// </summary>
    private Task<IReadOnlyList<HistoryOrderListItem>> LoadHeldOrdersAsync(CancellationToken cancellationToken)
    {
        if (_heldLoadTask is not null && !_heldLoadTask.IsCompleted)
        {
            return _heldLoadTask;
        }

        _heldLoadTask = LoadHeldOrdersGuardedAsync(cancellationToken);
        return _heldLoadTask;
    }

    private async Task<IReadOnlyList<HistoryOrderListItem>> LoadHeldOrdersGuardedAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await LoadHeldOrdersCoreAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HeldOrdersRemoteStatusMessage = ex.Message;
            // 共享挂单首次加载失败绝不回退显示 Local/Online 旧列表；
            // 已成功的 Held 列表保留，其余来源行一律不展示。
            return Orders
                .Where(order => order.Source == TransactionHistorySource.HeldOrders)
                .ToArray();
        }
    }

    private async Task<IReadOnlyList<HistoryOrderListItem>> LoadHeldOrdersCoreAsync(CancellationToken cancellationToken)
    {
        HeldOrdersRemoteStatusMessage = string.Empty;
        var localTask = LoadLocalHeldRowsAsync(cancellationToken);
        var remoteTask = LoadRemoteHeldRowsAsync(cancellationToken);
        await Task.WhenAll(localTask, remoteTask);

        var localRows = localTask.Result;
        var remoteRows = remoteTask.Result;
        var merged = new List<HistoryOrderListItem>(localRows.Count + remoteRows.Count);
        foreach (var remote in remoteRows)
        {
            if (localRows.Remove(remote.HoldGuid, out var local))
            {
                merged.Add(BuildHeldOrderRow(
                    local.Order,
                    local.Publication,
                    local.ClaimStatus,
                    local.ClaimId,
                    remote.Item));
            }
            else
            {
                merged.Add(BuildHeldOrderRow(
                    null,
                    null,
                    null,
                    null,
                    remote.Item));
            }
        }

        foreach (var local in localRows.Values)
        {
            merged.Add(BuildHeldOrderRow(
                local.Order,
                local.Publication,
                local.ClaimStatus,
                local.ClaimId,
                null));
        }

        return merged
            .OrderByDescending(order => order.OccurredAt)
            .ToList();
    }

    private async Task<Dictionary<Guid, LocalHeldRow>> LoadLocalHeldRowsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, LocalHeldRow>();
        if (_suspendedOrderService is null)
        {
            return result;
        }

        var summaries = await _suspendedOrderService.GetPendingOrdersAsync(
            Session.StoreCode,
            SelectedTerminalDeviceCode,
            NormalizeKeyword(SearchText),
            100,
            cancellationToken);
        var from = ParseDateFrom(DateFrom);
        var to = ParseDateTo(DateTo);
        var filtered = summaries
            .Where(order => from is null || order.SuspendedAt >= from.Value)
            .Where(order => to is null || order.SuspendedAt <= to.Value)
            .ToList();

        var publications = new Dictionary<Guid, SharedHeldOrderPublication?>();
        if (_sharedHeldOrderRepository is not null)
        {
            var publicationTasks = filtered.Select(async order =>
            {
                try
                {
                    return (order.SuspendedOrderGuid, await _sharedHeldOrderRepository
                        .GetPublicationAsync(order.SuspendedOrderGuid, cancellationToken));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return (order.SuspendedOrderGuid, (SharedHeldOrderPublication?)null);
                }
            }).ToArray();
            foreach (var pair in await Task.WhenAll(publicationTasks))
            {
                publications[pair.Item1] = pair.Item2;
            }
        }

        var claimsByHold = new Dictionary<Guid, SharedHeldOrderClaimRecovery>();
        if (_sharedHeldOrderRepository is not null)
        {
            var claims = await _sharedHeldOrderRepository.FindRecoverableClaimsAsync(
                Session.StoreCode,
                Session.DeviceCode,
                cancellationToken);
            claimsByHold = claims.ToDictionary(claim => claim.HoldGuid);
        }

        foreach (var order in filtered)
        {
            var claim = claimsByHold.GetValueOrDefault(order.SuspendedOrderGuid);
            result[order.SuspendedOrderGuid] = new LocalHeldRow(
                order,
                publications.GetValueOrDefault(order.SuspendedOrderGuid),
                claim?.Status,
                claim?.ClaimId);
        }

        return result;
    }

    private async Task<IReadOnlyList<(Guid HoldGuid, SharedHeldOrderListItemDto Item)>> LoadRemoteHeldRowsAsync(
        CancellationToken cancellationToken)
    {
        if (_sharedHeldOrderApiClient is null)
        {
            return [];
        }

        try
        {
            var rows = await _sharedHeldOrderApiClient.ListPendingAsync(cancellationToken);
            var from = ParseDateFrom(DateFrom);
            var to = ParseDateTo(DateTo);
            var keyword = NormalizeKeyword(SearchText);
            return rows
                .Where(item => string.Equals(item.StoreCode, Session.StoreCode, StringComparison.OrdinalIgnoreCase))
                .Where(item => SelectedTerminalDeviceCode is null ||
                    string.Equals(item.DeviceCode, SelectedTerminalDeviceCode, StringComparison.OrdinalIgnoreCase))
                .Where(item => from is null || item.HeldAtUtc >= from.Value)
                .Where(item => to is null || item.HeldAtUtc <= to.Value)
                .Where(item => keyword is null ||
                    item.HeldByCashierName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.DeviceCode.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Select(item => (item.HoldGuid, item))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 远端错误：保留本地列表，仅非阻塞提示。
            HeldOrdersRemoteStatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                T("history.held.remoteUnavailable"),
                ex.Message);
            return [];
        }
    }

    private HistoryOrderListItem BuildHeldOrderRow(
        SuspendedOrderSummary? local,
        SharedHeldOrderPublication? publication,
        HeldClaimStatus? claimStatus,
        Guid? claimId,
        SharedHeldOrderListItemDto? remote)
    {
        var holdGuid = remote?.HoldGuid ?? local!.SuspendedOrderGuid;
        var serverStatus = remote is null ? null : (HeldServerStatus?)HeldServerStatus.Pending;
        var badge = HeldOrderStatusResolver.Resolve(publication?.Status, serverStatus, claimStatus);
        var isDeletePending = publication is
        {
            Status: HeldPublicationStatus.Blocked,
            ErrorCode: "LOCAL_DELETE_PENDING_REMOTE" or "LOCAL_DELETE_PENDING_LOCAL"
        };
        var blockDetail = isDeletePending
            ? T("history.held.deletePending")
            : publication is null
            ? string.Empty
            : string.Join(
                " ",
                new[] { publication.ErrorCode, publication.ErrorMessage }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        var hasLocalCopy = local is not null;
        var canRemoteRecall = !isDeletePending &&
            serverStatus == HeldServerStatus.Pending &&
            claimStatus is null &&
            Session.IsOnline;
        var canOfflineRecall = !isDeletePending &&
            hasLocalCopy &&
            publication?.Status is HeldPublicationStatus.PendingPublish or HeldPublicationStatus.Published &&
            claimStatus is null;
        var canLegacyRecall = hasLocalCopy &&
            !isDeletePending &&
            !canRemoteRecall &&
            !canOfflineRecall &&
            (publication is null ||
             publication.Status is HeldPublicationStatus.NeedsEvaluation or HeldPublicationStatus.Blocked);
        var canDeleteHeldOrder = hasLocalCopy &&
            string.Equals(local!.StoreCode, Session.StoreCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(local.DeviceCode, Session.DeviceCode, StringComparison.OrdinalIgnoreCase) &&
            publication?.ConsumedAtIso is null &&
            claimStatus is null;

        return new HistoryOrderListItem(
            holdGuid,
            TransactionHistorySource.HeldOrders,
            remote?.StoreCode ?? local!.StoreCode,
            remote?.DeviceCode ?? local!.DeviceCode,
            remote?.HeldByCashierName ?? local!.CashierName,
            remote?.HeldAtUtc ?? local!.SuspendedAt,
            ToMoney(remote?.TotalCents) ?? local!.TotalAmount,
            ToMoney(remote?.DiscountCents) ?? local!.DiscountAmount,
            ToMoney(remote?.ActualCents) ?? local!.ActualAmount,
            remote?.LineCount ?? local!.LineCount,
            T("history.payment.suspended"),
            BuildHeldStatusLabel(badge, blockDetail),
            IsSuspendedOrder: hasLocalCopy,
            CanRecall: canRemoteRecall || canOfflineRecall || canLegacyRecall,
            DisplayCulture: CurrentDisplayCulture,
            IsHeldOrder: true,
            HeldBadgeKind: badge,
            HeldStatusDetail: blockDetail,
            HeldClaimId: claimId,
            CanForceRelease: claimStatus is HeldClaimStatus.Prepared or HeldClaimStatus.Active,
            CanRemoteRecall: canRemoteRecall,
            CanOfflineRecall: canOfflineRecall,
            CanLegacyRecall: canLegacyRecall,
            CanDeleteHeldOrder: canDeleteHeldOrder);
    }

    private static decimal? ToMoney(long? cents)
    {
        return cents is null ? null : cents.Value / 100m;
    }

    private string BuildHeldStatusLabel(HeldOrderBadgeKind kind, string detail)
    {
        var label = kind switch
        {
            HeldOrderBadgeKind.LocalPendingPublish => T("history.held.pendingPublish"),
            HeldOrderBadgeKind.Published => T("history.held.published"),
            HeldOrderBadgeKind.RemotePending => T("history.held.remotePending"),
            HeldOrderBadgeKind.LocalClaimPrepared => T("history.held.claimPrepared"),
            HeldOrderBadgeKind.LocalClaimActive => T("history.held.claimActive"),
            HeldOrderBadgeKind.ClaimedByOther => T("history.held.claimedByOther"),
            HeldOrderBadgeKind.Completed => T("history.held.completed"),
            HeldOrderBadgeKind.Blocked => T("history.held.blocked"),
            _ => T("history.held.localHold")
        };
        return string.IsNullOrWhiteSpace(detail) ? label : $"{label}: {detail}";
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
            order.StatusLabel,
            DisplayCulture: CurrentDisplayCulture)).ToList();
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
                string.Format(
                    CurrentDisplayCulture,
                    "{0}: {1:C2}",
                    T("history.installment.paid"),
                    order.PaidAmount),
                order.Status,
                InstallmentOrder: order,
                IsInstallmentOrder: true,
                CanContinueInstallmentPayment: order.CanAddRepayment,
                CanConfirmInstallmentPickup: order.CanConfirmPickup,
                CustomerPhone: order.CustomerPhone,
                DisplayCulture: CurrentDisplayCulture))
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
                PreviewSoldAt = installmentReceipt.SoldAt.ToLocalTime().ToString(
                    "MMM dd, yyyy HH:mm",
                    CurrentDisplayCulture);
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
        PreviewSoldAt = receipt.SoldAt.ToLocalTime().ToString(
            "MMM dd, yyyy HH:mm",
            CurrentDisplayCulture);
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

        if (orderSnapshot?.IsHeldOrder == true)
        {
            await RecallHeldOrderCoreAsync(orderSnapshot);
            return;
        }

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

    /// <summary>
    /// 共享挂单取单路由：服务端 Pending 且在线 -> ISharedHeldOrderCoordinator 远端取单；
    /// 本地待发布/已发布副本 -> 原设备本地/离线 recall；无发布状态的本地挂单 ->
    /// 既有本地挂单召回。成功统一走 _onSuspendedOrderRecalledAsync 刷新购物车并返回 POS。
    /// </summary>
    private async Task RecallHeldOrderCoreAsync(HistoryOrderListItem order)
    {
        var correlation = OperationAuditEvents.CreateCorrelation();
        try
        {
            if (order.CanRemoteRecall && Session.IsOnline)
            {
                if (_sharedHeldOrderCoordinator is null)
                {
                    throw new InvalidOperationException(T("history.held.unavailable"));
                }

                await _sharedHeldOrderCoordinator.TakeRemoteHoldAsync(
                    order.OrderGuid,
                    Session);
            }
            else if (order.CanOfflineRecall)
            {
                if (_sharedHeldOrderCoordinator is null)
                {
                    throw new InvalidOperationException(T("history.held.unavailable"));
                }

                await _sharedHeldOrderCoordinator.RecallLocalPublicationAsync(
                    order.OrderGuid,
                    Session);
            }
            else if (order.CanLegacyRecall)
            {
                if (_suspendedOrderService is null)
                {
                    return;
                }

                var recalledOrder = await _suspendedOrderService.RecallOrderAsync(order.OrderGuid);
                OperationAuditEvents.RecordCartChange(
                    _operationAuditLogger,
                    OperationAuditTypes.OrderRecall,
                    Session,
                    new OperationAuditCartSnapshot(0m, 0m, 0m, []),
                    OperationAuditEvents.CaptureSuspendedOrder(recalledOrder),
                    reasonCode: "SUSPENDED_ORDER",
                    orderGuid: order.OrderGuid.ToString("D"),
                    correlationId: correlation.CorrelationId,
                    traceId: correlation.TraceId);
            }
            else
            {
                return;
            }

            if (_onSuspendedOrderRecalledAsync is not null)
            {
                await _onSuspendedOrderRecalledAsync();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                OperationAuditTypes.OrderRecall,
                "Failed",
                Session,
                reasonCode: "SHARED_HELD_ORDER",
                safeMessage: ex.GetType().Name,
                orderGuid: order.OrderGuid.ToString("D"),
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            ConsoleLog.WriteError(
                "OperationAudit",
                $"held order recall failed error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            StatusMessage = ex.Message;
        }
    }

    private bool CanDeleteHeldOrder(HistoryOrderListItem? order)
    {
        return order?.CanDeleteHeldOrder == true;
    }

    /// <summary>
    /// 与 iPad 对齐的两阶段删除：先在本地事务中阻断发布；若已经或可能发布到服务端，
    /// 再在线取消；最后才把本地挂单标记为 Canceled。任一步失败都保留暂存状态供重试。
    /// </summary>
    private async Task DeleteHeldOrderAsync(HistoryOrderListItem? order)
    {
        if (!CanDeleteHeldOrder(order))
        {
            return;
        }

        var candidate = order!;

        using var authorization = await AuthorizeAsync(
            Permissions.PosTerminal.History.Recall,
            "delete-held-order");
        if (authorization is null)
        {
            return;
        }
        using var authorizationActivation = authorization.Activate();

        if (_confirmationDialogService is null ||
            !await _confirmationDialogService.ConfirmHeldOrderCancellationAsync())
        {
            return;
        }

        if (_sharedHeldOrderRepository is null)
        {
            StatusMessage = T("history.held.unavailable");
            return;
        }

        var correlation = OperationAuditEvents.CreateCorrelation();
        var deleteStaged = false;
        try
        {
            var timestamp = _timeProvider
                .GetUtcNow()
                .ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            var staged = await _sharedHeldOrderRepository.TryStageDeletePendingAsync(
                candidate.OrderGuid,
                Session.StoreCode,
                Session.DeviceCode,
                timestamp,
                CancellationToken.None);
            if (staged is null)
            {
                throw new InvalidOperationException(T("history.held.deleteFailed"));
            }
            deleteStaged = true;

            if (staged.RemoteCancellationRequired)
            {
                if (!Session.IsOnline || _sharedHeldOrderApiClient is null)
                {
                    throw new InvalidOperationException(T("history.held.deleteOnlineRequired"));
                }

                var cancelled = await _sharedHeldOrderApiClient.CancelAsync(
                    candidate.OrderGuid,
                    CancellationToken.None);
                if (cancelled.HoldGuid != candidate.OrderGuid ||
                    cancelled.Status != HeldServerStatus.Cancelled)
                {
                    throw new InvalidOperationException(T("history.held.deleteFailed"));
                }
            }

            if (!await _sharedHeldOrderRepository.TryCompleteDeletePendingAsync(
                    candidate.OrderGuid,
                    Session.StoreCode,
                    Session.DeviceCode,
                    timestamp,
                    CancellationToken.None))
            {
                throw new InvalidOperationException(T("history.held.deleteFailed"));
            }

            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                OperationAuditTypes.OrderCancel,
                "Succeeded",
                Session,
                reasonCode: "SHARED_HELD_ORDER",
                orderGuid: candidate.OrderGuid.ToString("D"),
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            await LoadAsync();
            StatusMessage = T("history.held.deleted");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failureMessage = ex.Message;
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                OperationAuditTypes.OrderCancel,
                "Failed",
                Session,
                reasonCode: "SHARED_HELD_ORDER",
                safeMessage: ex.GetType().Name,
                orderGuid: candidate?.OrderGuid.ToString("D"),
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            if (deleteStaged)
            {
                // 暂存已把 publication 置为 Blocked；立即重读，禁止失败窗口内继续取回。
                await LoadAsync();
            }
            StatusMessage = failureMessage;
        }
    }

    private bool CanForceReleaseOrder(HistoryOrderListItem? order)
    {
        return order?.CanForceRelease == true && !IsForceReleaseReasonPromptOpen;
    }

    private void RequestForceRelease(HistoryOrderListItem? order)
    {
        if (!CanForceReleaseOrder(order))
        {
            return;
        }

        ForceReleaseCandidate = order;
        ForceReleaseReason = string.Empty;
        StatusMessage = string.Empty;
        IsForceReleaseReasonPromptOpen = true;
    }

    private bool CanConfirmForceRelease()
    {
        return IsForceReleaseReasonPromptOpen &&
            ForceReleaseCandidate?.CanForceRelease == true &&
            !string.IsNullOrWhiteSpace(ForceReleaseReason);
    }

    private bool CanCancelForceRelease()
    {
        return IsForceReleaseReasonPromptOpen;
    }

    private void CancelForceRelease()
    {
        IsForceReleaseReasonPromptOpen = false;
        ForceReleaseCandidate = null;
        ForceReleaseReason = string.Empty;
    }

    partial void OnForceReleaseReasonChanged(string value)
    {
        ConfirmForceReleaseCommand?.NotifyCanExecuteChanged();
    }

    partial void OnIsForceReleaseReasonPromptOpenChanged(bool value)
    {
        ForceReleaseHeldOrderCommand?.NotifyCanExecuteChanged();
        ConfirmForceReleaseCommand?.NotifyCanExecuteChanged();
        CancelForceReleaseCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 主管强制释放：复用 History.Recall 授权；原因必须非空（且不超过 500 字符），
    /// 校验通过后调用 coordinator 的 durable 方法（服务端 force-release 成功后才
    /// 清理本地 cart binding/fence/claim；失败保留可重试状态）。
    /// </summary>
    private async Task ConfirmForceReleaseAsync()
    {
        var candidate = ForceReleaseCandidate;
        if (candidate?.HeldClaimId is not Guid claimGuid)
        {
            CancelForceRelease();
            return;
        }

        var reason = ForceReleaseReason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            StatusMessage = T("history.held.forceReleaseReasonRequired");
            return;
        }

        if (reason.Length > 500)
        {
            StatusMessage = T("history.held.forceReleaseReasonTooLong");
            return;
        }

        IsForceReleaseReasonPromptOpen = false;
        ForceReleaseCandidate = null;

        if (_sharedHeldOrderCoordinator is null)
        {
            StatusMessage = T("history.held.unavailable");
            ForceReleaseReason = string.Empty;
            return;
        }

        using var authorization = await AuthorizeAsync(
            Permissions.PosTerminal.History.Recall,
            "force-release-held-order");
        if (authorization is null)
        {
            ForceReleaseReason = string.Empty;
            return;
        }
        using var authorizationActivation = authorization.Activate();

        try
        {
            await _sharedHeldOrderCoordinator.ForceReleaseAsync(
                candidate.OrderGuid,
                claimGuid,
                reason,
                Session,
                CancellationToken.None);
            StatusMessage = T("history.held.forceReleased");
            await LoadAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            ForceReleaseReason = string.Empty;
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

        var orderGuid = order.OrderGuid.ToString("D");
        // 分期本地快照缺失时仍保留完整订单号二维码，确保历史预览可直接扫码退货。
        rows.Add(new ReceiptPreviewRow(
            ReceiptPreviewRowKind.QrCode,
            $"QR {orderGuid}",
            ReceiptPrintAlignment.Center)
        {
            QrCodeValue = orderGuid
        });

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
        SourceOptions.Add(new HistorySourceOption(TransactionHistorySource.HeldOrders, T("history.source.held")));
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
        OnPropertyChanged(nameof(IsHeldSourceSelected));
        OnPropertyChanged(nameof(IsContinueInstallmentPaymentVisible));
        OnPropertyChanged(nameof(HeldOrdersSourceLabel));
        OnPropertyChanged(nameof(DeleteHeldOrderLabel));
        OnPropertyChanged(nameof(ForceReleaseLabel));
        OnPropertyChanged(nameof(ForceReleaseHeaderLabel));
        OnPropertyChanged(nameof(ForceReleaseReasonLabel));
        OnPropertyChanged(nameof(ForceReleaseConfirmLabel));
        OnPropertyChanged(nameof(ForceReleaseCancelLabel));
    }

    private void LocalizeSuspendedRows()
    {
        if (Orders.Count == 0)
        {
            return;
        }

        var selectedOrderGuid = SelectedOrder?.OrderGuid;
        var displayCulture = CurrentDisplayCulture;
        Orders.ReplaceWith(Orders.Select(order => order with
        {
            DisplayCulture = displayCulture,
            PaymentSummary = order.IsSuspendedOrder || order.IsHeldOrder
                ? T("history.payment.suspended")
                : order.PaymentSummary,
            StatusLabel = order.IsHeldOrder
                ? BuildHeldStatusLabel(order.HeldBadgeKind, order.HeldStatusDetail)
                : order.IsSuspendedOrder ? T("history.status.pendingRecall") : order.StatusLabel
        }).ToList());
        SelectedOrder = selectedOrderGuid is null
            ? Orders.FirstOrDefault()
            : Orders.FirstOrDefault(order => order.OrderGuid == selectedOrderGuid.Value);
    }

    private string T(string key)
    {
        if (_localization?.T(key) is { } localized && !localized.StartsWith("[[", StringComparison.Ordinal))
        {
            return localized;
        }

        return key switch
        {
            "TransactionHistory" => "Transaction History",
            "success.receiptPreview" => "Receipt Preview",
            "history.reprint" => "Reprint",
            "history.reuploadCompleted" => "Reupload completed: {0} succeeded, {1} failed.",
            "history.reuploadRangeEmpty" => "No eligible local orders were found for the selected date range.",
            "history.reuploadRangeCancelled" => "Date-range reupload cancelled.",
            "history.reuploadRangeProgress" => "Reuploading {0} orders in {1} batch(es)...",
            "history.reuploadRangeCompleted" => "Date-range reupload completed: {0} attempted, {1} succeeded, {2} failed.",
            "history.reuploadRangeInterrupted" => "Date-range reupload stopped while the server address was switching: {0} attempted, {1} succeeded, {2} did not complete in the batches already started. Later batches were not started; queued orders will continue uploading after the switch.",
            "history.reuploadRangeFailed" => "Date-range reupload stopped: {0}",
            "history.refund" => "Refund",
            "history.search" => "Search order, cashier, or terminal...",
            "history.allTerminals" => "All Terminals",
            "history.source.local" => "Local",
            "history.source.online" => "Online",
            "history.source.installments" => "Installments",
            "history.source.held" => "Held",
            "history.held.localHold" => "Local hold",
            "history.held.pendingPublish" => "Pending publish",
            "history.held.published" => "Published",
            "history.held.remotePending" => "Remote recall",
            "history.held.claimPrepared" => "Preparing on this device",
            "history.held.claimActive" => "Active on this device",
            "history.held.claimedByOther" => "Claimed by another terminal",
            "history.held.completed" => "Completed",
            "history.held.blocked" => "Blocked",
            "history.held.forceRelease" => "Force release",
            "history.held.forceReleaseReason" => "Force release reason",
            "history.held.forceReleaseConfirm" => "Confirm",
            "history.held.forceReleaseCancel" => "Cancel",
            "history.held.forceReleaseReasonRequired" => "A force-release reason is required.",
            "history.held.forceReleaseReasonTooLong" => "The force-release reason must not exceed 500 characters.",
            "history.held.forceReleased" => "Held order force-released.",
            "history.held.delete" => "Delete",
            "history.held.deleteConfirmTitle" => "Delete held sale?",
            "history.held.deleteConfirmMessage" => "This permanently removes the local hold. A shared hold is cancelled online first.",
            "history.held.deleteConfirmAction" => "Delete held sale",
            "history.held.deleted" => "Held sale deleted.",
            "history.held.deleteFailed" => "The held sale was not deleted.",
            "history.held.deleteOnlineRequired" => "This shared held sale must be cancelled while online.",
            "history.held.deletePending" => "Deletion is pending; choose Delete to retry.",
            "history.held.remoteUnavailable" => "Remote held orders unavailable: {0}",
            "history.held.unavailable" => "Shared held orders are not configured on this terminal.",
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

    private CultureInfo CurrentDisplayCulture =>
        _localization?.CurrentCulture ??
        CultureInfo.GetCultureInfo(LocalizationService.DefaultCultureName);

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

    /// <summary>
    /// 界面变为可见时由视图代码调用：挂单源立即刷新并启动 10 秒自动刷新。
    /// </summary>
    public void OnScreenShown()
    {
        if (_disposed)
        {
            return;
        }

        _isScreenVisible = true;
        UpdateHeldAutoRefresh();
        if (SelectedSource == TransactionHistorySource.HeldOrders)
        {
            LastHeldAutoRefreshTask = RefreshHeldOrdersSilentlyAsync();
        }
    }

    /// <summary>界面隐藏时停表，避免后台空转。</summary>
    public void OnScreenHidden()
    {
        _isScreenVisible = false;
        UpdateHeldAutoRefresh();
    }

    private void UpdateHeldAutoRefresh()
    {
        var shouldRun = !_disposed &&
            _isScreenVisible &&
            SelectedSource == TransactionHistorySource.HeldOrders;
        if (shouldRun)
        {
            if (_heldAutoRefreshTimer is null)
            {
                _heldAutoRefreshTimer = _timeProvider.CreateTimer(
                    OnHeldAutoRefreshTick,
                    null,
                    HeldAutoRefreshInterval,
                    HeldAutoRefreshInterval);
            }

            return;
        }

        StopHeldAutoRefresh();
    }

    private void StopHeldAutoRefresh()
    {
        _heldAutoRefreshTimer?.Dispose();
        _heldAutoRefreshTimer = null;
    }

    private void OnHeldAutoRefreshTick(object? state)
    {
        if (_disposed || !_isScreenVisible || SelectedSource != TransactionHistorySource.HeldOrders)
        {
            return;
        }

        // 生产环境 Timer 回调在线程池线程，需回到 Dispatcher 再刷新 UI 集合；
        // 测试环境没有 Application，直接在线程上执行。
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(() => LastHeldAutoRefreshTask = RefreshHeldOrdersSilentlyAsync());
            return;
        }

        LastHeldAutoRefreshTask = RefreshHeldOrdersSilentlyAsync();
    }

    /// <summary>仅测试使用：最近一次自动刷新任务，便于等待 tick 完成。</summary>
    internal Task? LastHeldAutoRefreshTask { get; private set; }

    private async Task RefreshHeldOrdersSilentlyAsync()
    {
        if (_disposed ||
            !_isScreenVisible ||
            SelectedSource != TransactionHistorySource.HeldOrders)
        {
            return;
        }

        var rows = await LoadHeldOrdersAsync(CancellationToken.None);
        if (_disposed)
        {
            return;
        }

        var selectedGuid = SelectedOrder?.OrderGuid;
        _suppressSelectedOrderLoad = true;
        Orders.ReplaceWith(rows);
        SelectedOrder = selectedGuid is { } preserved
            ? Orders.FirstOrDefault(order => order.OrderGuid == preserved)
            : Orders.FirstOrDefault();
        _suppressSelectedOrderLoad = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopHeldAutoRefresh();
        if (_localization is not null)
        {
            _localization.CultureChanged -= OnCultureChanged;
        }

        ReprintRequested = null;
    }
}
