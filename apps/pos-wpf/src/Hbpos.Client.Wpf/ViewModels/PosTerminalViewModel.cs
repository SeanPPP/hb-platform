using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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
    private readonly PosTerminalActions _actions;
    private readonly PosTerminalScanController _scanController;
    private readonly ILocalizationService? _localization;
    private readonly IUserFeedbackService _userFeedbackService;
    private readonly IRawScannerService? _rawScannerService;
    private readonly Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? _syncCatalogAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? _resetCatalogAsync;
    private readonly Func<CancellationToken, Task<bool>>? _refreshOnlineAsync;
    private string _statusKey = "pos.status.ready";
    private object[] _statusArgs = [];
    private string? _statusText;
    private string? _activeScanTraceId;
    private DateTimeOffset? _activeScanStartedAt;

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
        Func<Task>? onExitApplicationAsync = null)
    {
        _priceIndex = priceIndex;
        _cart = cart;
        _workflowService = workflowService ?? new PosTerminalWorkflowService(priceIndex, cart, remoteLookupRefreshAsync, reloadCatalogAsync);
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
            onReregisterDeviceAsync);
        _scanController = new PosTerminalScanController(cart);
        _session = session;
        _localization = localization;
        _userFeedbackService = userFeedbackService ?? NoopUserFeedbackService.Instance;
        _syncCatalogAsync = syncCatalogAsync;
        _resetCatalogAsync = resetCatalogAsync;
        _refreshOnlineAsync = refreshOnlineAsync;
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
        OpenPaymentCommand = new RelayCommand(OpenPayment, () => !_cart.IsEmpty);
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
        ExitApplicationCommand = new AsyncRelayCommand(ExitApplicationAsync, () => _actions.CanExitApplication);
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
        RefreshCartCore("cart-changed", _activeScanTraceId, _activeScanStartedAt);
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
        ApplyWorkflowResult(_workflowService.AddOpenItem(Session, KeypadBuffer));
    }

    private void AddSelected()
    {
        if (SelectedItem is null)
        {
            return;
        }

        ApplyWorkflowResult(_workflowService.AddSelectedItem(
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
        ApplyWorkflowResult(_workflowService.AddSelectedItem(
            Session,
            item,
            clearScanText: true,
            closeMatchesPopup: true,
            operation: "manual-select-match"));
    }

    private void RemoveLine(CartLine? line)
    {
        ApplyWorkflowResult(_workflowService.RemoveLine(line));
    }

    private void IncreaseLine(CartLine? line)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = _workflowService.IncreaseLine(line);
        ApplyWorkflowResult(result);
        stopwatch.Stop();
        if (line is not null && string.Equals(result.StatusKey, "pos.status.ready", StringComparison.Ordinal))
        {
            LogCartOperation("increase-line", line, success: true, stopwatch.ElapsedMilliseconds);
        }
    }

    private void DecreaseLine(CartLine? line)
    {
        ApplyWorkflowResult(_workflowService.DecreaseLine(line));
    }

    private void ModifySelectedLineQuantity()
    {
        ApplyWorkflowResult(_workflowService.ModifySelectedLineQuantity(SelectedCartLine, KeypadBuffer));
    }

    private void ModifySelectedLinePrice()
    {
        ApplyWorkflowResult(_workflowService.ModifySelectedLinePrice(SelectedCartLine, KeypadBuffer));
    }

    private void ApplySelectedLineDiscountAmount()
    {
        ApplyWorkflowResult(_workflowService.ApplySelectedLineDiscountAmount(SelectedCartLine, KeypadBuffer, IsWholeOrderOperation));
    }

    private void ApplySelectedLineDiscountPercent()
    {
        ApplyWorkflowResult(_workflowService.ApplySelectedLineDiscountPercent(SelectedCartLine, KeypadBuffer, IsWholeOrderOperation));
    }

    private void ApplyQuickDiscountPercent(string? value)
    {
        ApplyWorkflowResult(_workflowService.ApplyQuickDiscountPercent(SelectedCartLine, value, IsWholeOrderOperation));
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
            await ProcessScannerBarcodeAsync(barcode, devicePath, source, scannedAt);
        }
        catch (Exception ex)
        {
            // 扫描枪入口不能 await，异常必须在后台任务内记录，避免静默吞掉。
            ConsoleLog.Write(
                "PosScan",
                $"scanner async processing failed barcode={barcode} source={source} device={devicePath} error={ex.Message}");
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
        ApplyWorkflowResult(_workflowService.ClearCart());
    }

    private void OpenPayment()
    {
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
        _actions.OpenReturns?.Invoke();
    }

    private async Task OpenSpecialProductsAsync()
    {
        if (_actions.OpenSpecialProductsAsync is not null)
        {
            await _actions.OpenSpecialProductsAsync();
        }
    }

    private async Task HoldOrderAsync()
    {
        if (_actions.HoldOrderAsync is not null)
        {
            await _actions.HoldOrderAsync();
        }
    }

    private async Task RecallOrderAsync()
    {
        if (_actions.RecallOrderAsync is not null)
        {
            await _actions.RecallOrderAsync();
        }
    }

    private async Task OpenHistoryAsync()
    {
        if (_actions.OpenHistoryAsync is not null)
        {
            await _actions.OpenHistoryAsync();
        }
    }

    private async Task OpenDailyCloseAsync()
    {
        if (_actions.OpenDailyCloseAsync is not null)
        {
            await _actions.OpenDailyCloseAsync();
        }
    }

    private async Task OpenSettingsAsync()
    {
        if (_actions.OpenSettingsAsync is not null)
        {
            await _actions.OpenSettingsAsync();
        }
    }

    private void OpenCustomerDisplay()
    {
        _actions.OpenCustomerDisplay?.Invoke();
    }

    private async Task PrintLastReceiptAsync()
    {
        if (_actions.PrintLastReceiptAsync is null)
        {
            return;
        }

        SetStatusText(T("receipt.print.printing"));
        var result = await _actions.PrintLastReceiptAsync();
        SetStatusText(
            result.Message,
            result.Succeeded ? StatusFeedbackKind.Success : StatusFeedbackKind.Error,
            result.Succeeded ? null : UserFeedbackCue.OperationError);
    }

    private async Task OpenCashDrawerAsync()
    {
        if (_actions.OpenCashDrawerAsync is null)
        {
            return;
        }

        SetStatusText(T("cashDrawer.opening"));
        var result = await _actions.OpenCashDrawerAsync();
        SetStatusText(
            result.Succeeded ? T("cashDrawer.opened") : result.Message,
            result.Succeeded ? StatusFeedbackKind.Success : StatusFeedbackKind.Error,
            result.Succeeded ? null : UserFeedbackCue.OperationError);
    }

    private async Task ExitApplicationAsync()
    {
        if (_actions.ExitApplicationAsync is not null)
        {
            await _actions.ExitApplicationAsync();
        }
    }

    private async Task ReregisterDeviceAsync()
    {
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
            result = await _workflowService.ProcessScanAsync(
                Session,
                plan.Barcode,
                plan.PreferExactLookup,
                plan.Source,
                plan.TraceId);
            workflowStopwatch.Stop();
            applyStopwatch.Start();
            ApplyWorkflowResult(result, statusTextOverrideFactory?.Invoke(result));
            applyStopwatch.Stop();
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

    private async Task SyncAsync()
    {
        await RunCatalogDownloadAsync(
            _syncCatalogAsync,
            T("pos.catalogSync.starting", "Syncing catalog..."),
            T("pos.catalogSync.completed", "Catalog sync completed."),
            T("pos.catalogSync.failed", "Catalog sync failed"));
    }

    private async Task ResetCatalogAsync()
    {
        await RunCatalogDownloadAsync(
            _resetCatalogAsync,
            T("pos.catalogReset.starting", "Resetting catalog..."),
            T("pos.catalogReset.completed", "Catalog reset completed."),
            T("pos.catalogReset.failed", "Catalog reset failed"));
    }

    private async Task RunCatalogDownloadAsync(
        Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? downloadCatalogAsync,
        string startingMessage,
        string completedMessage,
        string failedPrefix)
    {
        if (_refreshOnlineAsync is not null)
        {
            var isOnline = await _refreshOnlineAsync(CancellationToken.None);
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

        try
        {
            SetStatusText(startingMessage);
            var catalogItems = await downloadCatalogAsync(CancellationToken.None);
            RefreshMatches(catalogItems);
            SetStatusText(completedMessage);
        }
        catch (OperationCanceledException)
        {
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
