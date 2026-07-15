using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Wpf.ViewModels;

public enum StatusFeedbackKind
{
    Neutral,
    Success,
    Warning,
    Error
}

public sealed partial class PosTerminalViewModel : ObservableObject, IScannerInputTarget, IDisposable
{
    public const string PageId = "PosTerminal";

    public string ScannerPageId => PageId;

    private readonly LocalSellableItemIndex _priceIndex;
    private readonly PosCartService _cart;
    private readonly IPosTerminalWorkflowService _workflowService;
    private readonly IPromotionEvaluationService? _promotionEvaluationService;
    private readonly PosTerminalActions _actions;
    private readonly PosTerminalScanController _scanController;
    private readonly ILocalizationService? _localization;
    private readonly IUserFeedbackService _userFeedbackService;
    private readonly ICashierSessionContext _cashierSessionContext;
    private readonly bool _enforcePermissions;
    private readonly IRawScannerService? _rawScannerService;
    private readonly Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? _syncCatalogAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? _resetCatalogAsync;
    private readonly Func<CancellationToken, Task<bool>>? _refreshOnlineAsync;
    private readonly Func<string, CancellationToken, Task<bool>>? _tryLoginCashierFromScannerFallbackAsync;
    private readonly IOperationAuditLogger? _operationAuditLogger;
    private string _statusKey = "pos.status.ready";
    private object[] _statusArgs = [];
    private string? _statusText;
    private string? _activeScanTraceId;
    private DateTimeOffset? _activeScanStartedAt;
    private long _cartChangedSequence;
    private bool _isPromotionEvaluationRunning;
    private string? _queuedPromotionEvaluationReason;
    private Task _pendingPromotionEvaluationTask = Task.CompletedTask;

    [ObservableProperty]
    private PosSessionState _session;

    [ObservableProperty]
    private string _scanText = string.Empty;

    [ObservableProperty]
    private string _keypadBuffer = string.Empty;

    [ObservableProperty]
    private SellableItemDto? _selectedItem;

    [ObservableProperty]
    private CartLine? _selectedCartLine;

    [ObservableProperty]
    private bool _isMatchesPopupOpen;

    [ObservableProperty]
    private bool _isTouchKeyboardOpen;

    [ObservableProperty]
    private bool _isWholeOrderOperation;

    [ObservableProperty]
    private StatusFeedbackKind _statusFeedbackKind = StatusFeedbackKind.Neutral;

    [ObservableProperty]
    private int _statusPulseToken;

    public PosTerminalViewModel(
        LocalSellableItemIndex priceIndex,
        PosCartService cart,
        PosSessionState session,
        Action? onOpenPayment,
        Func<Task>? onOpenSpecialProductsAsync = null,
        ILocalizationService? localization = null,
        IUserFeedbackService? userFeedbackService = null,
        Func<string, string, CancellationToken, Task<RemoteLookupRefreshResult>>? remoteLookupRefreshAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? reloadCatalogAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? syncCatalogAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? resetCatalogAsync = null,
        Func<CancellationToken, Task<bool>>? refreshOnlineAsync = null,
        IRawScannerService? rawScannerService = null,
        Func<Task>? onReregisterDeviceAsync = null,
        IPosTerminalWorkflowService? workflowService = null,
        Func<Task>? onHoldOrderAsync = null,
        Func<Task>? onRecallOrderAsync = null,
        Func<Task>? onOpenHistoryAsync = null,
        Func<Task>? onOpenDailyCloseAsync = null,
        Func<Task>? onOpenSettingsAsync = null,
        Action? onOpenCustomerDisplay = null,
        Action? onOpenReturns = null,
        Func<Task<ReceiptPrintResult>>? onPrintLastReceiptAsync = null,
        Func<Task<ReceiptPrintResult>>? onOpenCashDrawerAsync = null,
        Func<Task>? onExitApplicationAsync = null,
        Func<string, CancellationToken, Task<bool>>? tryLoginCashierFromScannerFallbackAsync = null,
        ICashierSessionContext? cashierSessionContext = null,
        bool enforcePermissionsWhenNoCashier = false,
        IPromotionEvaluationService? promotionEvaluationService = null,
        IOperationAuditLogger? operationAuditLogger = null,
        Func<Task>? onLockCashierAsync = null)
    {
        _priceIndex = priceIndex;
        _cart = cart;
        _workflowService = workflowService ?? new PosTerminalWorkflowService(priceIndex, cart, remoteLookupRefreshAsync, reloadCatalogAsync);
        _promotionEvaluationService = promotionEvaluationService;
        _operationAuditLogger = operationAuditLogger;
        _actions = PosTerminalActions.FromLegacyCallbacks(
            onOpenPayment,
            onOpenReturns,
            onOpenSpecialProductsAsync,
            onHoldOrderAsync,
            onRecallOrderAsync,
            onOpenHistoryAsync,
            onOpenDailyCloseAsync,
            onOpenSettingsAsync,
            onOpenCustomerDisplay,
            onPrintLastReceiptAsync,
            onOpenCashDrawerAsync,
            onExitApplicationAsync,
            onReregisterDeviceAsync,
            onLockCashierAsync);
        _scanController = new PosTerminalScanController(cart);
        _session = session;
        _localization = localization;
        _userFeedbackService = userFeedbackService ?? NoopUserFeedbackService.Instance;
        _cashierSessionContext = cashierSessionContext ?? new CashierSessionContext();
        _enforcePermissions = enforcePermissionsWhenNoCashier;
        if (session.CashierSession is not null)
        {
            _cashierSessionContext.SetCurrent(session.CashierSession);
        }

        _syncCatalogAsync = syncCatalogAsync;
        _resetCatalogAsync = resetCatalogAsync;
        _refreshOnlineAsync = refreshOnlineAsync;
        _tryLoginCashierFromScannerFallbackAsync = tryLoginCashierFromScannerFallbackAsync;
        _rawScannerService = rawScannerService;
        if (_localization is not null)
        {
            _localization.CultureChanged += OnCultureChanged;
        }

        _cart.CartChanged += OnCartChanged;
        _workflowService.CatalogReloaded += OnWorkflowCatalogReloaded;
        _rawScannerService?.Subscribe(PageId, OnRawBarcodeScanned);

        ScanCommand = new AsyncRelayCommand(SearchAndAddAsync);
        NumberInputCommand = new RelayCommand<string>(AppendScanText);
        KeypadInputCommand = new RelayCommand<string>(AppendKeypadBuffer);
        ToggleTouchKeyboardCommand = new RelayCommand(ToggleTouchKeyboard);
        AddOpenItemCommand = new RelayCommand(AddOpenItem, CanAddOpenItem);
        AddSelectedCommand = new RelayCommand(AddSelected, () => SelectedItem is not null);
        SelectMatchCommand = new RelayCommand<SellableItemDto>(SelectMatch);
        RemoveLineCommand = new RelayCommand<CartLine>(RemoveLine);
        IncreaseLineCommand = new RelayCommand<CartLine>(IncreaseLine, line => line is not null && !line.IsLocked && _cart.Lines.Contains(line));
        DecreaseLineCommand = new RelayCommand<CartLine>(DecreaseLine, line => line is not null && !line.IsLocked && _cart.Lines.Contains(line));
        ModifySelectedLineQuantityCommand = new RelayCommand(ModifySelectedLineQuantity);
        ModifySelectedLinePriceCommand = new RelayCommand(ModifySelectedLinePrice);
        ApplySelectedLineDiscountAmountCommand = new RelayCommand(ApplySelectedLineDiscountAmount);
        ApplySelectedLineDiscountPercentCommand = new RelayCommand(ApplySelectedLineDiscountPercent);
        ApplyQuickDiscountPercentCommand = new RelayCommand<string>(ApplyQuickDiscountPercent);
        ClearSearchCommand = new RelayCommand(ClearSearch, () => !string.IsNullOrWhiteSpace(ScanText));
        ClearCartCommand = new RelayCommand(ClearCart, () => !_cart.IsEmpty);
        OpenPaymentCommand = new AsyncRelayCommand(OpenPaymentAsync, () => !_cart.IsEmpty);
        OpenReturnsCommand = new RelayCommand(OpenReturns);
        OpenSpecialProductsCommand = new AsyncRelayCommand(OpenSpecialProductsAsync);
        HoldOrderCommand = new AsyncRelayCommand(HoldOrderAsync, () => !_cart.IsEmpty);
        RecallOrderCommand = new AsyncRelayCommand(RecallOrderAsync);
        OpenHistoryCommand = new AsyncRelayCommand(OpenHistoryAsync);
        OpenDailyCloseCommand = new AsyncRelayCommand(OpenDailyCloseAsync);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        OpenCustomerDisplayCommand = new RelayCommand(OpenCustomerDisplay);
        PrintLastReceiptCommand = new AsyncRelayCommand(PrintLastReceiptAsync, () => _actions.CanPrintLastReceipt);
        OpenCashDrawerCommand = new AsyncRelayCommand(OpenCashDrawerAsync, () => _actions.CanOpenCashDrawer);
        LockCashierCommand = new AsyncRelayCommand(LockCashierAsync);
        ExitApplicationCommand = new AsyncRelayCommand(ExitApplicationAsync, () => _actions.CanExitApplication);
        // 手动目录操作使用命令令牌，保留调用方取消能力，同时不影响后台无超时同步。
        SyncCommand = new AsyncRelayCommand(SyncAsync);
        ResetCatalogCommand = new AsyncRelayCommand(ResetCatalogAsync);
        ReregisterDeviceCommand = new AsyncRelayCommand(ReregisterDeviceAsync);
    }

    public ObservableCollection<SellableItemDto> Matches { get; } = [];

    public ObservableCollection<CartLine> CartLines { get; } = [];

    public IAsyncRelayCommand ScanCommand { get; }

    public IRelayCommand<string> NumberInputCommand { get; }

    public IRelayCommand<string> KeypadInputCommand { get; }

    public IRelayCommand ToggleTouchKeyboardCommand { get; }

    public IRelayCommand AddOpenItemCommand { get; }

    public IRelayCommand AddSelectedCommand { get; }

    public IRelayCommand<SellableItemDto> SelectMatchCommand { get; }

    public IRelayCommand<CartLine> RemoveLineCommand { get; }

    public IRelayCommand<CartLine> IncreaseLineCommand { get; }

    public IRelayCommand<CartLine> DecreaseLineCommand { get; }

    public IRelayCommand ModifySelectedLineQuantityCommand { get; }

    public IRelayCommand ModifySelectedLinePriceCommand { get; }

    public IRelayCommand ApplySelectedLineDiscountAmountCommand { get; }

    public IRelayCommand ApplySelectedLineDiscountPercentCommand { get; }

    public IRelayCommand<string> ApplyQuickDiscountPercentCommand { get; }

    public IRelayCommand ClearSearchCommand { get; }

    public IRelayCommand ClearCartCommand { get; }

    public IRelayCommand OpenPaymentCommand { get; }

    public IRelayCommand OpenReturnsCommand { get; }

    public IAsyncRelayCommand OpenSpecialProductsCommand { get; }

    public IAsyncRelayCommand HoldOrderCommand { get; }

    public IAsyncRelayCommand RecallOrderCommand { get; }

    public IAsyncRelayCommand OpenHistoryCommand { get; }

    public IAsyncRelayCommand OpenDailyCloseCommand { get; }

    public IAsyncRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand OpenCustomerDisplayCommand { get; }

    public IAsyncRelayCommand PrintLastReceiptCommand { get; }

    public IAsyncRelayCommand OpenCashDrawerCommand { get; }

    public IAsyncRelayCommand LockCashierCommand { get; }

    public IAsyncRelayCommand ExitApplicationCommand { get; }

    public IAsyncRelayCommand SyncCommand { get; }

    public IAsyncRelayCommand ResetCatalogCommand { get; }

    public IAsyncRelayCommand ReregisterDeviceCommand { get; }

    public event EventHandler? PaymentRequested;

    public string ScreenTitleText => T("pos.terminal.title");

    public string SearchPlaceholderText => T("pos.terminal.search.placeholder");

    public string SearchButtonText => T("pos.terminal.search.action");

    public string AddSelectedText => T("pos.terminal.addSelected");

    public string CartTitleText => T("pos.terminal.cart.title");

    public string TotalsLabelText => T("pos.terminal.totals.total");

    public string PayNowText => T("pos.terminal.payNow");

    public string ClearCartText => T("pos.terminal.actions.clearCart");

    public string HoldOrderText => T("pos.terminal.actions.holdOrder");

    public string RecallOrderText => T("pos.terminal.actions.recallOrder");

    public string HistoryText => T("pos.terminal.actions.history");

    public string DailyCloseText => T("pos.terminal.actions.dailyClose");

    public string SettingsText => T("pos.terminal.actions.settings");

    public string CustomerDisplayText => T("pos.terminal.actions.customerDisplay");

    public string PrintLastReceiptText => T("pos.terminal.actions.printLastReceipt");

    public string OpenCashDrawerText => T("pos.terminal.actions.openCashDrawer");

    public string LockCashierText => T("pos.terminal.actions.lock");

    public string ExitApplicationText => T("pos.terminal.actions.exitApplication");

    public string MemberText => T("pos.terminal.actions.member");

    public string SyncText => T("pos.terminal.actions.sync");

    public string CatalogResetText => T("pos.terminal.actions.catalogReset");

    public string ReregisterDeviceText => T("pos.terminal.actions.reregisterDevice");

    public string OnlineText => T(Session.IsOnline ? "pos.status.online" : "pos.status.offline");

    public string PendingSyncText => Format("pos.status.pendingSync", Session.PendingSyncCount);

    public string StatusMessage => _statusText ?? Format(_statusKey, _statusArgs);

    public decimal TotalAmount => _cart.TotalAmount;

    public decimal DiscountAmount => _cart.DiscountAmount;

    public decimal ActualAmount => _cart.ActualAmount;

    public decimal CartItemQuantity => _cart.Lines.Sum(line => line.SignedQuantity);

    public int CartSkuCount => _cart.Lines.Count;

    public void Dispose()
    {
        if (_localization is not null)
        {
            _localization.CultureChanged -= OnCultureChanged;
        }

        _cart.CartChanged -= OnCartChanged;
        _workflowService.CatalogReloaded -= OnWorkflowCatalogReloaded;
        _rawScannerService?.Unsubscribe(PageId);
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RaiseLocalizedProperties();
    }

    partial void OnSelectedItemChanged(SellableItemDto? value)
    {
        AddSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnScanTextChanged(string value)
    {
        ClearSearchCommand.NotifyCanExecuteChanged();
    }

    partial void OnKeypadBufferChanged(string value)
    {
        AddOpenItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnSessionChanged(PosSessionState value)
    {
        if (value.CashierSession is not null)
        {
            _cashierSessionContext.SetCurrent(value.CashierSession);
        }

        OnPropertyChanged(nameof(OnlineText));
        OnPropertyChanged(nameof(PendingSyncText));
    }

    public void LoadMatches(IEnumerable<SellableItemDto> items)
    {
        Matches.ReplaceWith(items.Take(8));
    }

    public void RefreshCart()
    {
        RefreshCartCore("manual-refresh");
    }

    internal void RevealCartLine(CartLine line)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!_cart.Lines.Contains(line))
        {
            stopwatch.Stop();
            LogCartOperation("reveal-cart-line", line, success: false, stopwatch.ElapsedMilliseconds, "line-not-in-cart");
            return;
        }

        if (!CartLines.Contains(line))
        {
            SyncCartLines(_cart.Lines);
        }

        SelectCartLine(line);
        RefreshCartState();
        stopwatch.Stop();
        LogCartOperation("reveal-cart-line", line, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void RefreshCartCore(string operation, string? traceId = null, DateTimeOffset? scanStartedAt = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var syncCartStopwatch = Stopwatch.StartNew();
        SyncCartLines(_cart.Lines);
        syncCartStopwatch.Stop();

        var stateRefreshStopwatch = Stopwatch.StartNew();
        RefreshCartState();
        stateRefreshStopwatch.Stop();
        totalStopwatch.Stop();

        LogCartPerf(
            $"{FormatTraceId(traceId)}operation={operation} storeCode={Session.StoreCode} cartLines={_cart.Lines.Count} scanAgeMs={FormatElapsedSince(scanStartedAt)} syncCartElapsedMs={syncCartStopwatch.ElapsedMilliseconds} stateRefreshElapsedMs={stateRefreshStopwatch.ElapsedMilliseconds} totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
    }

    private void RefreshCartState()
    {
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(DiscountAmount));
        OnPropertyChanged(nameof(ActualAmount));
        OnPropertyChanged(nameof(CartItemQuantity));
        OnPropertyChanged(nameof(CartSkuCount));
        IncreaseLineCommand.NotifyCanExecuteChanged();
        DecreaseLineCommand.NotifyCanExecuteChanged();
        ClearCartCommand.NotifyCanExecuteChanged();
        OpenPaymentCommand.NotifyCanExecuteChanged();
        HoldOrderCommand.NotifyCanExecuteChanged();
    }

    private void SyncCartLines(IReadOnlyList<CartLine> sourceLines)
    {
        for (var i = CartLines.Count - 1; i >= 0; i--)
        {
            if (!sourceLines.Contains(CartLines[i]))
            {
                CartLines.RemoveAt(i);
            }
        }

        for (var sourceIndex = 0; sourceIndex < sourceLines.Count; sourceIndex++)
        {
            var line = sourceLines[sourceIndex];
            var currentIndex = CartLines.IndexOf(line);
            if (currentIndex < 0)
            {
                CartLines.Insert(Math.Min(sourceIndex, CartLines.Count), line);
            }
            else if (currentIndex != sourceIndex)
            {
                CartLines.Move(currentIndex, sourceIndex);
            }
        }

        if (SelectedCartLine is not null && !sourceLines.Contains(SelectedCartLine))
        {
            SelectedCartLine = null;
        }
    }

    private void OnCartChanged(object? sender, EventArgs e)
    {
        _cartChangedSequence++;
        RefreshCartCore("cart-changed", _activeScanTraceId, _activeScanStartedAt);
    }

    private PosTerminalWorkflowResult ExecuteCartMutation(
        string reason,
        Func<PosTerminalWorkflowResult> operation)
    {
        var before = OperationAuditEvents.CaptureCart(_cart.Lines);
        var operationType = MapCartOperationType(reason, IsWholeOrderOperation);
        var previousSequence = _cartChangedSequence;
        PosTerminalWorkflowResult result;
        try
        {
            result = operation();
        }
        catch (Exception ex)
        {
            var correlation = OperationAuditEvents.CreateCorrelation();
            OperationAuditEvents.RecordCartChange(
                _operationAuditLogger,
                operationType,
                Session,
                before,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                outcome: "Failed",
                reasonCode: reason,
                safeMessage: ex.GetType().Name,
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            ConsoleLog.WriteError(
                "OperationAudit",
                $"cart operation failed operation={reason} error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            throw;
        }

        ApplyWorkflowResult(result);
        QueuePromotionEvaluationIfCartChanged(previousSequence, reason);
        var after = OperationAuditEvents.CaptureCart(_cart.Lines);
        if (OperationAuditEvents.HasChanged(before, after))
        {
            // 只在购物车真实变化后记录成功，促销后台重算不会单独生成员工操作事件。
            OperationAuditEvents.RecordCartChange(
                _operationAuditLogger,
                operationType,
                Session,
                before,
                after,
                reasonCode: reason);
        }

        return result;
    }

    private static string MapCartOperationType(string reason, bool isWholeOrderOperation)
    {
        return reason switch
        {
            "add-open-item" or "manual-add-selected" or "manual-select-match" => OperationAuditTypes.CartItemAdd,
            "remove-line" => OperationAuditTypes.CartItemRemove,
            "increase-line" or "decrease-line" or "modify-quantity" => OperationAuditTypes.CartItemQuantityChange,
            "modify-price" => OperationAuditTypes.CartItemPriceChange,
            "manual-discount-amount" or "manual-discount-percent" or "quick-discount-percent" =>
                isWholeOrderOperation
                    ? OperationAuditTypes.CartOrderDiscountChange
                    : OperationAuditTypes.CartLineDiscountChange,
            "clear-cart" => OperationAuditTypes.CartClear,
            _ => OperationAuditTypes.CartItemAdd
        };
    }

    private void QueuePromotionEvaluationIfCartChanged(long previousSequence, string reason)
    {
        if (_promotionEvaluationService is null || _cartChangedSequence == previousSequence)
        {
            return;
        }

        // 中文注释：扫码和改数量只排队促销重算，真正等待发生在收款或挂单前。
        _queuedPromotionEvaluationReason = reason;
        if (_isPromotionEvaluationRunning)
        {
            return;
        }

        _pendingPromotionEvaluationTask = RunPromotionEvaluationLoopAsync();
    }

    private async Task RunPromotionEvaluationLoopAsync()
    {
        if (_isPromotionEvaluationRunning)
        {
            return;
        }

        _isPromotionEvaluationRunning = true;
        try
        {
            while (_queuedPromotionEvaluationReason is string reason)
            {
                _queuedPromotionEvaluationReason = null;
                await ReevaluatePromotionsAsync(reason);
            }
        }
        finally
        {
            _isPromotionEvaluationRunning = false;
            if (_queuedPromotionEvaluationReason is not null)
            {
                _pendingPromotionEvaluationTask = RunPromotionEvaluationLoopAsync();
            }
        }
    }

    private async Task WaitForPendingPromotionEvaluationAsync()
    {
        while (true)
        {
            var pendingTask = _pendingPromotionEvaluationTask;
            if (pendingTask.IsCompleted)
            {
                return;
            }

            await pendingTask;
        }
    }

    private async Task ReevaluatePromotionsAsync(string reason)
    {
        if (_promotionEvaluationService is null)
        {
            return;
        }

        try
        {
            var discounts = await _promotionEvaluationService.EvaluateAsync(
                _cart.Lines.ToArray(),
                Session.StoreCode,
                DateTimeOffset.Now);
            _cart.ApplyPromotionDiscounts(discounts);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 中文注释：促销失败时保留当前折扣，只提示降级，避免收银金额被半更新。
            ConsoleLog.Write("Promotion", $"promotion evaluation failed reason={reason} error={ex.Message}");
            SetStatusText(T("pos.status.promotionFallback"), StatusFeedbackKind.Warning);
        }
    }

    private void OnWorkflowCatalogReloaded(object? sender, PosTerminalCatalogReloadedEventArgs e)
    {
        RefreshMatches(e.CatalogItems);
    }

    private void AppendScanText(string? value)
    {
        if (value == "Enter")
        {
            _ = SearchAndAddSafeAsync();
            IsTouchKeyboardOpen = false;
            return;
        }

        if (value == "Back")
        {
            ScanText = ScanText.Length > 0 ? ScanText[..^1] : string.Empty;
            return;
        }

        if (value == "Space")
        {
            ScanText += " ";
            return;
        }

        if (value == "Clear")
        {
            ScanText = string.Empty;
            IsMatchesPopupOpen = false;
            IsTouchKeyboardOpen = false;
            return;
        }

        ScanText += value;
    }

    private void AppendKeypadBuffer(string? value)
    {
        if (value == "QuickHalf")
        {
            ReplaceKeypadDecimal("50");
            return;
        }

        if (value == "QuickNinetyNine")
        {
            ReplaceKeypadDecimal("99");
            return;
        }

        if (value == "Back")
        {
            KeypadBuffer = KeypadBuffer.Length > 0 ? KeypadBuffer[..^1] : string.Empty;
            return;
        }

        if (value == "Clear")
        {
            KeypadBuffer = string.Empty;
            return;
        }

        if (string.IsNullOrEmpty(value) || value == "Enter" || value == "Space")
        {
            return;
        }

        if (value == ".")
        {
            if (!KeypadBuffer.Contains('.'))
            {
                KeypadBuffer = KeypadBuffer.Length == 0 ? "0." : KeypadBuffer + ".";
            }

            return;
        }

        if (value.Length != 1 || !char.IsDigit(value[0]) || HasTwoDecimalDigits(KeypadBuffer))
        {
            return;
        }

        KeypadBuffer += value;
    }

    private void ReplaceKeypadDecimal(string decimalDigits)
    {
        var wholePart = KeypadBuffer.Split('.')[0];
        KeypadBuffer = $"{(string.IsNullOrEmpty(wholePart) ? "0" : wholePart)}.{decimalDigits}";
    }

    private static bool HasTwoDecimalDigits(string value)
    {
        var decimalPointIndex = value.IndexOf('.', StringComparison.Ordinal);
        return decimalPointIndex >= 0 && value.Length - decimalPointIndex - 1 >= 2;
    }

    private void ToggleTouchKeyboard()
    {
        IsTouchKeyboardOpen = !IsTouchKeyboardOpen;
        if (IsTouchKeyboardOpen)
        {
            IsMatchesPopupOpen = false;
        }
    }

    private async Task SearchAndAddAsync()
    {
        await ExecuteScanAsync(_scanController.CreateManual(ScanText), statusTextOverrideFactory: null);
    }

    private async Task SearchAndAddSafeAsync()
    {
        try
        {
            await SearchAndAddAsync();
        }
        catch (Exception ex)
        {
            // 触摸键盘 Enter 不能 await，异常在后台任务中记录，避免未观察异常。
            ConsoleLog.Write("PosScan", $"manual async processing failed scanText={ScanText} error={ex.Message}");
        }
    }

    private bool CanAddOpenItem()
    {
        return decimal.TryParse(KeypadBuffer, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) &&
            value > 0m;
    }

    private void AddOpenItem()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Sales.AddOpenItem))
        {
            return;
        }

        ExecuteCartMutation("add-open-item", () => _workflowService.AddOpenItem(Session, KeypadBuffer));
    }

    private void AddSelected()
    {
        if (SelectedItem is null)
        {
            return;
        }

        if (!TryRequirePermission(Permissions.PosTerminal.Sales.AddItem))
        {
            return;
        }

        ExecuteCartMutation("manual-add-selected", () => _workflowService.AddSelectedItem(
            Session,
            SelectedItem,
            clearScanText: false,
            closeMatchesPopup: false,
            operation: "manual-add-selected"));
    }

    private void SelectMatch(SellableItemDto? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;
        if (!TryRequirePermission(Permissions.PosTerminal.Sales.AddItem))
        {
            return;
        }

        ExecuteCartMutation("manual-select-match", () => _workflowService.AddSelectedItem(
            Session,
            item,
            clearScanText: true,
            closeMatchesPopup: true,
            operation: "manual-select-match"));
    }

    private void RemoveLine(CartLine? line)
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Sales.RemoveLine))
        {
            return;
        }

        ExecuteCartMutation("remove-line", () => _workflowService.RemoveLine(line));
    }

    private void IncreaseLine(CartLine? line)
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Sales.ChangeQuantity))
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var result = ExecuteCartMutation("increase-line", () => _workflowService.IncreaseLine(line));
        stopwatch.Stop();
        if (line is not null && string.Equals(result.StatusKey, "pos.status.ready", StringComparison.Ordinal))
        {
            LogCartOperation("increase-line", line, success: true, stopwatch.ElapsedMilliseconds);
        }
    }

    private void DecreaseLine(CartLine? line)
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Sales.ChangeQuantity))
        {
            return;
        }

        ExecuteCartMutation("decrease-line", () => _workflowService.DecreaseLine(line));
    }

    private void ModifySelectedLineQuantity()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Sales.ChangeQuantity))
        {
            return;
        }

        ExecuteCartMutation("modify-quantity", () => _workflowService.ModifySelectedLineQuantity(SelectedCartLine, KeypadBuffer));
    }

    private void ModifySelectedLinePrice()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Sales.ChangePrice))
        {
            return;
        }

        ExecuteCartMutation("modify-price", () => _workflowService.ModifySelectedLinePrice(SelectedCartLine, KeypadBuffer));
    }

    private void ApplySelectedLineDiscountAmount()
    {
        if (!TryRequirePermission(IsWholeOrderOperation ? Permissions.PosTerminal.Sales.OrderDiscount : Permissions.PosTerminal.Sales.LineDiscount))
        {
            return;
        }

        ExecuteCartMutation("manual-discount-amount", () => _workflowService.ApplySelectedLineDiscountAmount(SelectedCartLine, KeypadBuffer, IsWholeOrderOperation));
    }

    private void ApplySelectedLineDiscountPercent()
    {
        if (!TryRequirePermission(IsWholeOrderOperation ? Permissions.PosTerminal.Sales.OrderDiscount : Permissions.PosTerminal.Sales.LineDiscount))
        {
            return;
        }

        ExecuteCartMutation("manual-discount-percent", () => _workflowService.ApplySelectedLineDiscountPercent(SelectedCartLine, KeypadBuffer, IsWholeOrderOperation));
    }

    private void ApplyQuickDiscountPercent(string? value)
    {
        if (!TryRequirePermission(IsWholeOrderOperation ? Permissions.PosTerminal.Sales.OrderDiscount : Permissions.PosTerminal.Sales.LineDiscount))
        {
            return;
        }

        ExecuteCartMutation("quick-discount-percent", () => _workflowService.ApplyQuickDiscountPercent(SelectedCartLine, value, IsWholeOrderOperation));
    }

    private void SelectCartLine(CartLine line)
    {
        if (ReferenceEquals(SelectedCartLine, line))
        {
            SelectedCartLine = null;
        }

        SelectedCartLine = line;
    }

    private void LogCartOperation(
        string operation,
        SellableItemDto item,
        bool success,
        long totalElapsedMs,
        string? reason = null,
        string? traceId = null)
    {
        LogCartPerf(
            $"{FormatTraceId(traceId)}operation={operation} storeCode={Session.StoreCode} productCode={LogValue(item.ProductCode)} lookupCode={LogValue(item.LookupCode)} success={FormatBool(success)} cartLines={_cart.Lines.Count} totalAmount={FormatAmount(_cart.TotalAmount)} actualAmount={FormatAmount(_cart.ActualAmount)} totalElapsedMs={totalElapsedMs}{FormatReason(reason)}");
    }

    private void LogCartOperation(
        string operation,
        CartLine? line,
        bool success,
        long totalElapsedMs,
        string? reason = null,
        string? traceId = null)
    {
        LogCartPerf(
            $"{FormatTraceId(traceId)}operation={operation} storeCode={Session.StoreCode} productCode={LogValue(line?.ProductCode)} lookupCode={LogValue(line?.LookupCode)} success={FormatBool(success)} cartLines={_cart.Lines.Count} totalAmount={FormatAmount(_cart.TotalAmount)} actualAmount={FormatAmount(_cart.ActualAmount)} totalElapsedMs={totalElapsedMs}{FormatReason(reason)}");
    }

    private static void LogCartPerf(string message)
    {
        ConsoleLog.Write("CartPerf", message);
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string FormatAmount(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? string.Empty : $" reason={reason.Trim()}";
    }

    private static string FormatTraceId(string? traceId)
    {
        return string.IsNullOrWhiteSpace(traceId) ? string.Empty : $"traceId={traceId} ";
    }

    private static string FormatElapsedSince(DateTimeOffset? startedAt)
    {
        return startedAt is null
            ? "<none>"
            : Math.Max(0, (DateTimeOffset.Now - startedAt.Value).TotalMilliseconds).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string LogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value.Trim();
    }

    private void OnRawBarcodeScanned(RawBarcodeScannedEventArgs e)
    {
        ProcessScannerBarcode(e.Barcode, e.DevicePath, "raw", e.ScannedAt);
    }

    public bool ProcessScannerBarcode(string barcode, string devicePath, string source)
    {
        _ = ProcessScannerBarcodeSafeAsync(barcode, devicePath, source, null);
        return true;
    }

    private Task ProcessScannerBarcodeAsync(string barcode, string devicePath, string source, DateTimeOffset? scannedAt)
    {
        return ExecuteScanAsync(
            _scanController.CreateScanner(barcode, devicePath, source, scannedAt),
            result => FormatScannerResultStatus(barcode, result));
    }

    private void ProcessScannerBarcode(string barcode, string devicePath, string source, DateTimeOffset? scannedAt)
    {
        _ = ProcessScannerBarcodeSafeAsync(barcode, devicePath, source, scannedAt);
    }

    private async Task ProcessScannerBarcodeSafeAsync(string barcode, string devicePath, string source, DateTimeOffset? scannedAt)
    {
        try
        {
            if (barcode.Trim().StartsWith($"{EmergencyLoginTokenCodec.TokenPrefix}-", StringComparison.Ordinal) &&
                _tryLoginCashierFromScannerFallbackAsync is not null)
            {
                // 原始扫描枪也必须在商品工作流前分流，令牌不得触发本地或远程商品查询。
                await _tryLoginCashierFromScannerFallbackAsync(barcode, CancellationToken.None);
                return;
            }

            await ProcessScannerBarcodeAsync(barcode, devicePath, source, scannedAt);
        }
        catch (Exception ex)
        {
            // 扫描枪入口不能 await，异常必须在后台任务内记录，避免静默吞掉。
            ConsoleLog.Write(
                "PosScan",
                $"scanner async processing failed barcodeInfo={BarcodeLogFormatter.FormatBarcodeInfo(barcode)} source={source} device={devicePath} error={ex.Message}");
        }
    }

    private void ClearSearch()
    {
        ScanText = string.Empty;
        IsMatchesPopupOpen = false;
        IsTouchKeyboardOpen = false;
    }

    private void ClearCart()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Sales.ClearCart))
        {
            return;
        }

        ExecuteCartMutation("clear-cart", () => _workflowService.ClearCart());
    }

    private async Task OpenPaymentAsync()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Payment.View))
        {
            return;
        }

        // 进入支付前等待本轮促销尝试结束，避免使用过期金额。
        await WaitForPendingPromotionEvaluationAsync();

        var stopwatch = Stopwatch.StartNew();
        var result = _workflowService.GuardPayment();
        ApplyWorkflowResult(result);
        if (!result.PaymentAllowed)
        {
            stopwatch.Stop();
            return;
        }

        PaymentRequested?.Invoke(this, EventArgs.Empty);
        _actions.OpenPayment?.Invoke();
        stopwatch.Stop();
        LogCartOperation("open-payment", (CartLine?)null, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void OpenReturns()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Returns.View))
        {
            return;
        }

        _actions.OpenReturns?.Invoke();
    }

    private async Task OpenSpecialProductsAsync()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.SpecialProducts.View))
        {
            return;
        }

        if (_actions.OpenSpecialProductsAsync is not null)
        {
            await _actions.OpenSpecialProductsAsync();
        }
    }

    private async Task HoldOrderAsync()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Sales.HoldOrder))
        {
            return;
        }

        // 挂单会持久化金额，必须先等当前促销重算完成。
        await WaitForPendingPromotionEvaluationAsync();

        if (_actions.HoldOrderAsync is not null)
        {
            var snapshot = OperationAuditEvents.CaptureCart(_cart.Lines);
            await RecordActionCallbackAsync(
                OperationAuditTypes.OrderHold,
                _actions.HoldOrderAsync,
                snapshot,
                "HOLD");
        }
    }

    private async Task RecallOrderAsync()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Sales.RecallOrder))
        {
            return;
        }

        if (_actions.RecallOrderAsync is not null)
        {
            // 这里仅导航到暂存订单列表，真实取单成功由 TransactionHistoryViewModel 记录。
            await _actions.RecallOrderAsync();
        }
    }

    private async Task OpenHistoryAsync()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.History.View))
        {
            return;
        }

        if (_actions.OpenHistoryAsync is not null)
        {
            await _actions.OpenHistoryAsync();
        }
    }

    private async Task OpenDailyCloseAsync()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.DailyClose.View))
        {
            return;
        }

        if (_actions.OpenDailyCloseAsync is not null)
        {
            await _actions.OpenDailyCloseAsync();
        }
    }

    private async Task OpenSettingsAsync()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Settings.View))
        {
            return;
        }

        if (_actions.OpenSettingsAsync is not null)
        {
            await _actions.OpenSettingsAsync();
        }
    }

    private void OpenCustomerDisplay()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.CustomerDisplay.Manage))
        {
            return;
        }

        _actions.OpenCustomerDisplay?.Invoke();
    }

    private async Task PrintLastReceiptAsync()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Receipt.PrintLast))
        {
            return;
        }

        if (_actions.PrintLastReceiptAsync is null)
        {
            return;
        }

        SetStatusText(T("receipt.print.printing"));
        var result = await _actions.PrintLastReceiptAsync();
        OperationAuditEvents.RecordAction(
            _operationAuditLogger,
            OperationAuditTypes.ReceiptReprint,
            result.Succeeded ? "Succeeded" : "Failed",
            Session,
            OperationAuditEvents.CaptureCart(_cart.Lines),
            reasonCode: "LAST_RECEIPT",
            safeMessage: result.Succeeded ? null : result.Message);
        SetStatusText(
            result.Message,
            result.Succeeded ? StatusFeedbackKind.Success : StatusFeedbackKind.Error,
            result.Succeeded ? null : UserFeedbackCue.OperationError);
    }

    private async Task OpenCashDrawerAsync()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.CashDrawer.Open))
        {
            return;
        }

        if (_actions.OpenCashDrawerAsync is null)
        {
            return;
        }

        SetStatusText(T("cashDrawer.opening"));
        var result = await _actions.OpenCashDrawerAsync();
        OperationAuditEvents.RecordAction(
            _operationAuditLogger,
            OperationAuditTypes.CashDrawerOpen,
            result.Succeeded ? "Succeeded" : "Failed",
            Session,
            OperationAuditEvents.CaptureCart(_cart.Lines),
            reasonCode: "MANUAL",
            safeMessage: result.Succeeded ? null : result.Message);
        SetStatusText(
            result.Succeeded ? T("cashDrawer.opened") : result.Message,
            result.Succeeded ? StatusFeedbackKind.Success : StatusFeedbackKind.Error,
            result.Succeeded ? null : UserFeedbackCue.OperationError);
    }

    private async Task RecordActionCallbackAsync(
        string operationType,
        Func<Task> callback,
        OperationAuditCartSnapshot snapshot,
        string reasonCode)
    {
        var correlation = OperationAuditEvents.CreateCorrelation();
        try
        {
            await callback();
            var after = OperationAuditEvents.CaptureCart(_cart.Lines);
            if (!OperationAuditEvents.HasChanged(snapshot, after))
            {
                OperationAuditEvents.RecordAction(
                    _operationAuditLogger,
                    operationType,
                    "Failed",
                    Session,
                    snapshot,
                    reasonCode: $"{reasonCode}_NO_STATE_CHANGE",
                    safeMessage: "NO_STATE_CHANGE",
                    correlationId: correlation.CorrelationId,
                    traceId: correlation.TraceId);
                return;
            }

            OperationAuditEvents.RecordCartChange(
                _operationAuditLogger,
                operationType,
                Session,
                snapshot,
                after,
                reasonCode: reasonCode,
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
        }
        catch (Exception ex)
        {
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                operationType,
                "Failed",
                Session,
                snapshot,
                reasonCode: reasonCode,
                safeMessage: ex.GetType().Name,
                correlationId: correlation.CorrelationId,
                traceId: correlation.TraceId);
            ConsoleLog.WriteError(
                "OperationAudit",
                $"operation failed operation={operationType} error={ex.GetType().Name}",
                new ApplicationLogContext(TraceId: correlation.TraceId),
                ex);
            throw;
        }
    }

    private async Task ExitApplicationAsync()
    {
        if (_actions.ExitApplicationAsync is not null)
        {
            await _actions.ExitApplicationAsync();
        }
    }

    private async Task LockCashierAsync()
    {
        if (_actions.LockCashierAsync is not null)
        {
            await _actions.LockCashierAsync();
        }
    }

    private async Task ReregisterDeviceAsync()
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Settings.DeviceRegistration))
        {
            return;
        }

        if (_actions.ReregisterDeviceAsync is not null)
        {
            await _actions.ReregisterDeviceAsync();
        }
    }

    private async Task ExecuteScanAsync(
        PosTerminalScanPlan plan,
        Func<PosTerminalWorkflowResult, string?>? statusTextOverrideFactory)
    {
        // 中文注释：统一承接手动检索与扫描枪入口，只让 VM 负责 UI 状态投影和结果应用。
        var totalStopwatch = Stopwatch.StartNew();
        _scanController.LogStarted(plan, Session.StoreCode);

        if (plan.ApplyBarcodeToScanText)
        {
            var setScanTextStopwatch = Stopwatch.StartNew();
            ScanText = plan.Barcode;
            if (plan.CloseTouchKeyboard)
            {
                IsTouchKeyboardOpen = false;
            }

            setScanTextStopwatch.Stop();
            _scanController.LogInputApplied(plan, setScanTextStopwatch.ElapsedMilliseconds);
        }

        _activeScanTraceId = plan.TraceId;
        _activeScanStartedAt = plan.StartedAt;
        var workflowStopwatch = Stopwatch.StartNew();
        PosTerminalWorkflowResult result;
        var applyStopwatch = new Stopwatch();
        try
        {
            var hasCashierSession = _cashierSessionContext.CurrentSession is not null || Session.CashierSession is not null;
            // 关键逻辑：已登录且本地已知商品时必须按 AddItem 权限拒绝；未登录时则优先把同码输入解释为收银员登录。
            var hasLocalCatalogMatch = HasLocalCatalogExactMatch(plan.Barcode);
            if (!TryRequirePermission(Permissions.PosTerminal.Sales.AddItem))
            {
                workflowStopwatch.Stop();
                if ((!hasCashierSession || !hasLocalCatalogMatch) &&
                    await TryApplyCashierLoginFallbackAsync(plan, result: null, CancellationToken.None))
                {
                    totalStopwatch.Stop();
                    _scanController.LogFinished(
                        plan,
                        "cashier-login",
                        false,
                        _cart.Lines.Count,
                        workflowStopwatch.ElapsedMilliseconds,
                        0,
                        totalStopwatch.ElapsedMilliseconds);
                }

                return;
            }

            var auditBefore = OperationAuditEvents.CaptureCart(_cart.Lines);
            var previousSequence = _cartChangedSequence;
            result = await _workflowService.ProcessScanAsync(
                Session,
                plan.Barcode,
                plan.PreferExactLookup,
                plan.Source,
                plan.TraceId);
            workflowStopwatch.Stop();
            if (await TryApplyCashierLoginFallbackAsync(plan, result, CancellationToken.None))
            {
                totalStopwatch.Stop();
                _scanController.LogFinished(
                    plan,
                    "cashier-login",
                    false,
                    _cart.Lines.Count,
                    workflowStopwatch.ElapsedMilliseconds,
                    0,
                    totalStopwatch.ElapsedMilliseconds);
                return;
            }

            applyStopwatch.Start();
            ApplyWorkflowResult(result, statusTextOverrideFactory?.Invoke(result));
            applyStopwatch.Stop();
            QueuePromotionEvaluationIfCartChanged(previousSequence, "scan");
            var auditAfter = OperationAuditEvents.CaptureCart(_cart.Lines);
            if (OperationAuditEvents.HasChanged(auditBefore, auditAfter))
            {
                OperationAuditEvents.RecordCartChange(
                    _operationAuditLogger,
                    OperationAuditTypes.CartItemAdd,
                    Session,
                    auditBefore,
                    auditAfter,
                    reasonCode: "SCAN",
                    traceId: plan.TraceId);
            }
        }
        finally
        {
            _activeScanTraceId = null;
            _activeScanStartedAt = null;
        }

        totalStopwatch.Stop();
        if (result.SelectedCartLine is not null && string.Equals(result.StatusKey, "pos.status.added", StringComparison.Ordinal))
        {
            LogCartOperation("scan-auto-add", result.SelectedCartLine, success: true, totalStopwatch.ElapsedMilliseconds, traceId: plan.TraceId);
        }

        _scanController.LogFinished(
            plan,
            result.StatusKey,
            result.SelectedCartLine is not null,
            _cart.Lines.Count,
            workflowStopwatch.ElapsedMilliseconds,
            applyStopwatch.ElapsedMilliseconds,
            totalStopwatch.ElapsedMilliseconds);
    }

    private bool HasLocalCatalogExactMatch(string barcode)
    {
        return _priceIndex.FindExactMatches(Session.StoreCode, barcode).Count > 0 ||
            _priceIndex.FindMetadataExactMatches(Session.StoreCode, barcode).Count > 0;
    }

    private async Task<bool> TryApplyCashierLoginFallbackAsync(
        PosTerminalScanPlan plan,
        PosTerminalWorkflowResult? result,
        CancellationToken cancellationToken)
    {
        if (_tryLoginCashierFromScannerFallbackAsync is null ||
            (result is not null && !IsCatalogMiss(result)))
        {
            return false;
        }

        var loggedIn = await _tryLoginCashierFromScannerFallbackAsync(plan.Barcode, cancellationToken);
        if (!loggedIn)
        {
            return false;
        }

        // 关键逻辑：未登录时允许扫码先进收银员登录；已能查商品时只在商品明确 miss 后解释为收银员条码。
        Matches.Clear();
        SelectedItem = null;
        IsMatchesPopupOpen = false;
        IsTouchKeyboardOpen = false;
        ScanText = string.Empty;
        SetStatusText(T("shell.cashierLogin.succeeded", "收银员登录成功"), StatusFeedbackKind.Success);
        ConsoleLog.Write(
            "CashierLogin",
            $"scanner fallback cashier login succeeded source={plan.Source} device={plan.DevicePath} barcodeInfo={BarcodeLogFormatter.FormatBarcodeInfo(plan.Barcode)}");
        return true;
    }

    private static bool IsCatalogMiss(PosTerminalWorkflowResult result)
    {
        return string.Equals(result.StatusKey, "pos.status.noLocalMatch", StringComparison.Ordinal) &&
            result.Matches is not null &&
            result.Matches.Count == 0;
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        if (!TryRequirePermission(Permissions.PosTerminal.System.Sync))
        {
            return;
        }

        await RunCatalogDownloadAsync(
            _syncCatalogAsync,
            T("pos.catalogSync.starting", "Syncing catalog..."),
            T("pos.catalogSync.completed", "Catalog sync completed."),
            T("pos.catalogSync.failed", "Catalog sync failed"),
            cancellationToken);
    }

    private async Task ResetCatalogAsync(CancellationToken cancellationToken)
    {
        if (!TryRequirePermission(Permissions.PosTerminal.Settings.CatalogReset))
        {
            return;
        }

        await RunCatalogDownloadAsync(
            _resetCatalogAsync,
            T("pos.catalogReset.starting", "Resetting catalog..."),
            T("pos.catalogReset.completed", "Catalog reset completed."),
            T("pos.catalogReset.failed", "Catalog reset failed"),
            cancellationToken);
    }

    private async Task RunCatalogDownloadAsync(
        Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? downloadCatalogAsync,
        string startingMessage,
        string completedMessage,
        string failedPrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_refreshOnlineAsync is not null)
            {
                var isOnline = await _refreshOnlineAsync(cancellationToken);
                Session = Session with { IsOnline = isOnline };
            }

            if (!Session.IsOnline)
            {
                SetStatusText(T("pos.status.catalogSyncSkipped", "Offline: catalog sync skipped."));
                return;
            }

            if (downloadCatalogAsync is null)
            {
                SetStatus("pos.status.ready");
                return;
            }

            SetStatusText(startingMessage);
            var catalogItems = await downloadCatalogAsync(cancellationToken);
            RefreshMatches(catalogItems);
            SetStatusText(completedMessage);
        }
        catch (OperationCanceledException)
        {
            // 取消后恢复可操作状态，避免界面永久停留在“正在同步”。
            SetStatus("pos.status.ready");
        }
        catch (Exception ex)
        {
            SetStatusText($"{failedPrefix}: {ex.Message}", StatusFeedbackKind.Error, UserFeedbackCue.OperationError);
        }
    }

    private void RefreshMatches(IReadOnlyList<SellableItemDto> catalogItems)
    {
        var matches = string.IsNullOrWhiteSpace(ScanText)
            ? catalogItems.Take(8)
            : _priceIndex.Search(Session.StoreCode, ScanText);
        Matches.ReplaceWith(matches);
        SelectedItem = Matches.FirstOrDefault();
    }

    private void ApplyWorkflowResult(PosTerminalWorkflowResult result, string? statusTextOverride = null)
    {
        if (result.Matches is not null)
        {
            Matches.ReplaceWith(result.Matches);
            SelectedItem = result.SelectedItem;
        }

        if (result.MatchesPopupOpen is bool matchesPopupOpen)
        {
            IsMatchesPopupOpen = matchesPopupOpen;
        }

        if (result.TouchKeyboardOpen is bool touchKeyboardOpen)
        {
            IsTouchKeyboardOpen = touchKeyboardOpen;
        }

        if (result.WholeOrderOperation is bool wholeOrderOperation)
        {
            IsWholeOrderOperation = wholeOrderOperation;
        }

        if (result.ClearScanText)
        {
            ScanText = string.Empty;
        }

        if (result.ClearKeypadBuffer)
        {
            KeypadBuffer = string.Empty;
        }

        if (result.SelectedCartLine is not null)
        {
            SelectCartLine(result.SelectedCartLine);
        }

        if (!string.IsNullOrWhiteSpace(result.StatusKey))
        {
            var (feedbackKind, cue) = ClassifyStatusFeedback(result.StatusKey!);
            if (!string.IsNullOrWhiteSpace(statusTextOverride))
            {
                SetStatusText(statusTextOverride!, feedbackKind, cue);
            }
            else
            {
                SetStatusCore(result.StatusKey!, result.StatusArgs, feedbackKind, cue);
            }
        }
        else if (!string.IsNullOrWhiteSpace(statusTextOverride))
        {
            SetStatusText(statusTextOverride!, StatusFeedbackKind.Neutral);
        }
    }

    private string FormatScannerResultStatus(string barcode, PosTerminalWorkflowResult result)
    {
        var resultText = string.IsNullOrWhiteSpace(result.StatusKey)
            ? StatusMessage
            : Format(result.StatusKey!, result.StatusArgs);

        var template = T("pos.status.scannerResult");
        if (string.Equals(template, "pos.status.scannerResult", StringComparison.Ordinal))
        {
            template = "Scan {0}: {1}";
        }

        return string.Format(
            _localization?.CurrentCulture ?? CultureInfo.CurrentCulture,
            template,
            barcode,
            resultText);
    }

    private void SetStatus(string key, params object[] args)
    {
        SetStatusCore(key, args, StatusFeedbackKind.Neutral);
    }

    private void SetStatusCore(
        string key,
        object[] args,
        StatusFeedbackKind feedbackKind = StatusFeedbackKind.Neutral,
        UserFeedbackCue? cue = null)
    {
        _statusText = null;
        _statusKey = key;
        _statusArgs = args;
        ApplyStatusFeedback(feedbackKind, cue);
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void SetStatusText(
        string message,
        StatusFeedbackKind feedbackKind = StatusFeedbackKind.Neutral,
        UserFeedbackCue? cue = null)
    {
        _statusText = message;
        ApplyStatusFeedback(feedbackKind, cue);
        OnPropertyChanged(nameof(StatusMessage));
    }

    private bool TryRequirePermission(string permissionCode)
    {
        if ((!_enforcePermissions && _cashierSessionContext.CurrentSession is null && Session.CashierSession is null) ||
            _cashierSessionContext.RequirePermission(permissionCode, out var message))
        {
            return true;
        }

        // 中文注释：命令执行前二次校验，避免只靠按钮可用状态保护高风险操作。
        var deniedOperationType = GetDeniedAuditOperationType(permissionCode);
        if (deniedOperationType is not null)
        {
            OperationAuditEvents.RecordAction(
                _operationAuditLogger,
                deniedOperationType,
                "Denied",
                Session,
                OperationAuditEvents.CaptureCart(_cart.Lines),
                reasonCode: "PERMISSION_DENIED",
                safeMessage: message);
        }

        SetStatusText(message, StatusFeedbackKind.Error, UserFeedbackCue.OperationError);
        return false;
    }

    private static string? GetDeniedAuditOperationType(string permissionCode)
    {
        return permissionCode switch
        {
            Permissions.PosTerminal.Sales.AddItem or Permissions.PosTerminal.Sales.AddOpenItem => OperationAuditTypes.CartItemAdd,
            Permissions.PosTerminal.Sales.RemoveLine => OperationAuditTypes.CartItemRemove,
            Permissions.PosTerminal.Sales.ChangeQuantity => OperationAuditTypes.CartItemQuantityChange,
            Permissions.PosTerminal.Sales.ChangePrice => OperationAuditTypes.CartItemPriceChange,
            Permissions.PosTerminal.Sales.LineDiscount => OperationAuditTypes.CartLineDiscountChange,
            Permissions.PosTerminal.Sales.OrderDiscount => OperationAuditTypes.CartOrderDiscountChange,
            Permissions.PosTerminal.Sales.ClearCart => OperationAuditTypes.CartClear,
            Permissions.PosTerminal.Sales.HoldOrder => OperationAuditTypes.OrderHold,
            Permissions.PosTerminal.Sales.RecallOrder => OperationAuditTypes.OrderRecall,
            Permissions.PosTerminal.CashDrawer.Open => OperationAuditTypes.CashDrawerOpen,
            Permissions.PosTerminal.Receipt.PrintLast => OperationAuditTypes.ReceiptReprint,
            _ => null
        };
    }

    private void ApplyStatusFeedback(StatusFeedbackKind feedbackKind, UserFeedbackCue? cue)
    {
        StatusFeedbackKind = feedbackKind;
        StatusPulseToken++;
        if (cue is UserFeedbackCue feedbackCue)
        {
            _userFeedbackService.Play(feedbackCue);
        }
    }

    private static (StatusFeedbackKind Kind, UserFeedbackCue? Cue) ClassifyStatusFeedback(string statusKey)
    {
        return statusKey switch
        {
            "pos.status.added" => (StatusFeedbackKind.Success, UserFeedbackCue.ScanAdded),
            "pos.status.multipleMatches" => (StatusFeedbackKind.Warning, UserFeedbackCue.ScanMultipleMatches),
            "pos.status.noLocalMatch" => (StatusFeedbackKind.Error, UserFeedbackCue.ScanNoMatch),
            "cart.status.zeroPriceItem" => (StatusFeedbackKind.Error, UserFeedbackCue.OperationError),
            "cart.status.quantityMustBeInteger" => (StatusFeedbackKind.Error, UserFeedbackCue.OperationError),
            "pos.status.invalidKeypadValue" => (StatusFeedbackKind.Error, UserFeedbackCue.OperationError),
            "pos.status.selectCartLine" => (StatusFeedbackKind.Error, UserFeedbackCue.OperationError),
            "pos.status.discountAmountTooHigh" => (StatusFeedbackKind.Error, UserFeedbackCue.OperationError),
            "pos.status.discountPercentOutOfRange" => (StatusFeedbackKind.Error, UserFeedbackCue.OperationError),
            "pos.status.quantityMustBePositive" => (StatusFeedbackKind.Error, UserFeedbackCue.OperationError),
            _ => (StatusFeedbackKind.Neutral, null)
        };
    }

    private string T(string key)
    {
        return _localization?.T(key) ?? key;
    }

    private string T(string key, string fallback)
    {
        var value = _localization?.T(key);
        return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
    }

    private string Format(string key, params object[] args)
    {
        var template = T(key);
        return args.Length == 0
            ? template
            : string.Format(_localization?.CurrentCulture ?? System.Globalization.CultureInfo.CurrentCulture, template, args);
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(ScreenTitleText));
        OnPropertyChanged(nameof(SearchPlaceholderText));
        OnPropertyChanged(nameof(SearchButtonText));
        OnPropertyChanged(nameof(AddSelectedText));
        OnPropertyChanged(nameof(CartTitleText));
        OnPropertyChanged(nameof(TotalsLabelText));
        OnPropertyChanged(nameof(PayNowText));
        OnPropertyChanged(nameof(ClearCartText));
        OnPropertyChanged(nameof(HoldOrderText));
        OnPropertyChanged(nameof(RecallOrderText));
        OnPropertyChanged(nameof(HistoryText));
        OnPropertyChanged(nameof(DailyCloseText));
        OnPropertyChanged(nameof(SettingsText));
        OnPropertyChanged(nameof(CustomerDisplayText));
        OnPropertyChanged(nameof(PrintLastReceiptText));
        OnPropertyChanged(nameof(OpenCashDrawerText));
        OnPropertyChanged(nameof(LockCashierText));
        OnPropertyChanged(nameof(ExitApplicationText));
        OnPropertyChanged(nameof(MemberText));
        OnPropertyChanged(nameof(SyncText));
        OnPropertyChanged(nameof(CatalogResetText));
        OnPropertyChanged(nameof(ReregisterDeviceText));
        OnPropertyChanged(nameof(OnlineText));
        OnPropertyChanged(nameof(PendingSyncText));
        OnPropertyChanged(nameof(StatusMessage));
    }
}
