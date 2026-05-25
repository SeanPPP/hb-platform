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

public sealed partial class PosTerminalViewModel : ObservableObject, IDisposable
{
    public const string PageId = "PosTerminal";
    private static readonly TimeSpan RemoteLookupTimeout = TimeSpan.FromSeconds(2);

    private readonly LocalSellableItemIndex _priceIndex;
    private readonly PosCartService _cart;
    private readonly Action? _onOpenPayment;
    private readonly Func<Task>? _onOpenSpecialProductsAsync;
    private readonly Func<Task>? _onReregisterDeviceAsync;
    private readonly ILocalizationService? _localization;
    private readonly IRawScannerService? _rawScannerService;
    private readonly Func<string, string, CancellationToken, Task<RemoteLookupRefreshResult>>? _remoteLookupRefreshAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? _reloadCatalogAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? _syncCatalogAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? _resetCatalogAsync;
    private readonly Func<CancellationToken, Task<bool>>? _refreshOnlineAsync;
    private string _statusKey = "pos.status.ready";
    private object[] _statusArgs = [];
    private string? _statusText;

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

    public PosTerminalViewModel(
        LocalSellableItemIndex priceIndex,
        PosCartService cart,
        PosSessionState session,
        Action? onOpenPayment,
        Func<Task>? onOpenSpecialProductsAsync = null,
        ILocalizationService? localization = null,
        Func<string, string, CancellationToken, Task<RemoteLookupRefreshResult>>? remoteLookupRefreshAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? reloadCatalogAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? syncCatalogAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? resetCatalogAsync = null,
        Func<CancellationToken, Task<bool>>? refreshOnlineAsync = null,
        IRawScannerService? rawScannerService = null,
        Func<Task>? onReregisterDeviceAsync = null)
    {
        _priceIndex = priceIndex;
        _cart = cart;
        _session = session;
        _onOpenPayment = onOpenPayment;
        _onOpenSpecialProductsAsync = onOpenSpecialProductsAsync;
        _onReregisterDeviceAsync = onReregisterDeviceAsync;
        _localization = localization;
        _remoteLookupRefreshAsync = remoteLookupRefreshAsync;
        _reloadCatalogAsync = reloadCatalogAsync;
        _syncCatalogAsync = syncCatalogAsync;
        _resetCatalogAsync = resetCatalogAsync;
        _refreshOnlineAsync = refreshOnlineAsync;
        _rawScannerService = rawScannerService;
        if (_localization is not null)
        {
            _localization.CultureChanged += (_, _) => RaiseLocalizedProperties();
        }

        _cart.CartChanged += OnCartChanged;
        _rawScannerService?.Subscribe(PageId, OnRawBarcodeScanned);

        ScanCommand = new RelayCommand(SearchAndAdd);
        NumberInputCommand = new RelayCommand<string>(AppendScanText);
        KeypadInputCommand = new RelayCommand<string>(AppendKeypadBuffer);
        ToggleTouchKeyboardCommand = new RelayCommand(ToggleTouchKeyboard);
        AddSelectedCommand = new RelayCommand(AddSelected, () => SelectedItem is not null);
        SelectMatchCommand = new RelayCommand<SellableItemDto>(SelectMatch);
        RemoveLineCommand = new RelayCommand<CartLine>(RemoveLine);
        IncreaseLineCommand = new RelayCommand<CartLine>(IncreaseLine, line => line is not null && _cart.Lines.Contains(line));
        DecreaseLineCommand = new RelayCommand<CartLine>(DecreaseLine, line => line is not null && _cart.Lines.Contains(line));
        ModifySelectedLineQuantityCommand = new RelayCommand(ModifySelectedLineQuantity);
        ModifySelectedLinePriceCommand = new RelayCommand(ModifySelectedLinePrice);
        ApplySelectedLineDiscountAmountCommand = new RelayCommand(ApplySelectedLineDiscountAmount);
        ApplySelectedLineDiscountPercentCommand = new RelayCommand(ApplySelectedLineDiscountPercent);
        ApplyQuickDiscountPercentCommand = new RelayCommand<string>(ApplyQuickDiscountPercent);
        ClearSearchCommand = new RelayCommand(ClearSearch, () => !string.IsNullOrWhiteSpace(ScanText));
        ClearCartCommand = new RelayCommand(ClearCart, () => !_cart.IsEmpty);
        OpenPaymentCommand = new RelayCommand(OpenPayment, () => !_cart.IsEmpty);
        OpenSpecialProductsCommand = new AsyncRelayCommand(OpenSpecialProductsAsync);
        SyncCommand = new AsyncRelayCommand(SyncAsync);
        ResetCatalogCommand = new AsyncRelayCommand(ResetCatalogAsync);
        ReregisterDeviceCommand = new AsyncRelayCommand(ReregisterDeviceAsync);
    }

    public ObservableCollection<SellableItemDto> Matches { get; } = [];

    public ObservableCollection<CartLine> CartLines { get; } = [];

    public IRelayCommand ScanCommand { get; }

    public IRelayCommand<string> NumberInputCommand { get; }

    public IRelayCommand<string> KeypadInputCommand { get; }

    public IRelayCommand ToggleTouchKeyboardCommand { get; }

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

    public IAsyncRelayCommand OpenSpecialProductsCommand { get; }

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

    public decimal CartItemQuantity => _cart.Lines.Sum(line => line.Quantity);

    public int CartSkuCount => _cart.Lines.Count;

    public void Dispose()
    {
        _cart.CartChanged -= OnCartChanged;
        _rawScannerService?.Unsubscribe(PageId);
    }

    partial void OnSelectedItemChanged(SellableItemDto? value)
    {
        AddSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnScanTextChanged(string value)
    {
        ClearSearchCommand.NotifyCanExecuteChanged();
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

    private void RefreshCartCore(string operation)
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
            $"operation={operation} storeCode={Session.StoreCode} cartLines={_cart.Lines.Count} syncCartElapsedMs={syncCartStopwatch.ElapsedMilliseconds} stateRefreshElapsedMs={stateRefreshStopwatch.ElapsedMilliseconds} totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
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
        RefreshCartCore("cart-changed");
    }

    private void AppendScanText(string? value)
    {
        if (value == "Enter")
        {
            SearchAndAdd();
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

    private void SearchAndAdd()
    {
        ProcessScan(ScanText, preferExactLookup: false, source: "manual");
    }

    private void ProcessScan(string scanText, bool preferExactLookup, string source)
    {
        var submittedScanText = scanText;
        var totalStopwatch = Stopwatch.StartNew();
        var exactLookupElapsedMs = 0L;
        var searchElapsedMs = 0L;
        var cartUpdateElapsedMs = 0L;
        var uiRefreshElapsedMs = 0L;
        var matchKind = preferExactLookup ? "exact-not-found" : "search";
        var matchCount = 0;
        var autoAdded = false;

        if (preferExactLookup)
        {
            var exactLookupStopwatch = Stopwatch.StartNew();
            var exactMatches = _priceIndex.FindExactMatches(Session.StoreCode, submittedScanText);
            exactLookupStopwatch.Stop();
            exactLookupElapsedMs = exactLookupStopwatch.ElapsedMilliseconds;
            matchKind = exactMatches.Count switch
            {
                0 => "exact-not-found",
                1 => "lookup-exact",
                _ => "search-multiple"
            };

            var matches = exactMatches;
            var allowAutoAdd = exactMatches.Count == 1;
            if (exactMatches.Count == 0)
            {
                var metadataMatches = _priceIndex.FindMetadataExactMatches(Session.StoreCode, submittedScanText);
                if (metadataMatches.Count > 0)
                {
                    matches = metadataMatches;
                    matchKind = metadataMatches.Count > 1 ? "metadata-duplicate" : "metadata-only";
                }
            }

            ApplyScanMatches(matches, submittedScanText, allowAutoAdd, out autoAdded, out cartUpdateElapsedMs, out uiRefreshElapsedMs);
            matchCount = matches.Count;
        }
        else
        {
            var exactLookupStopwatch = Stopwatch.StartNew();
            var exactMatches = _priceIndex.FindExactMatches(Session.StoreCode, submittedScanText);
            exactLookupStopwatch.Stop();
            exactLookupElapsedMs = exactLookupStopwatch.ElapsedMilliseconds;
            var hasDuplicateExactMatch = exactMatches.Count > 1;
            if (hasDuplicateExactMatch)
            {
                matchKind = "search-multiple";
            }

            var searchStopwatch = Stopwatch.StartNew();
            var matches = _priceIndex.Search(Session.StoreCode, submittedScanText);
            searchStopwatch.Stop();
            searchElapsedMs = searchStopwatch.ElapsedMilliseconds;

            ApplyScanMatches(matches, submittedScanText, allowAutoAdd: !hasDuplicateExactMatch, out autoAdded, out cartUpdateElapsedMs, out uiRefreshElapsedMs);
            matchCount = matches.Count;
        }

        totalStopwatch.Stop();
        ConsoleLog.Write(
            "PosScan",
            $"barcode={submittedScanText} storeCode={Session.StoreCode} source={source} hit={matchKind} matchCount={matchCount} autoAdded={autoAdded} cartLines={_cart.Lines.Count} exactLookupElapsedMs={exactLookupElapsedMs} searchElapsedMs={searchElapsedMs} cartUpdateElapsedMs={cartUpdateElapsedMs} uiRefreshElapsedMs={uiRefreshElapsedMs} totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
    }

    private void ApplyScanMatches(
        IReadOnlyList<SellableItemDto> matches,
        string submittedScanText,
        bool allowAutoAdd,
        out bool autoAdded,
        out long cartUpdateElapsedMs,
        out long uiRefreshElapsedMs)
    {
        var uiRefreshStopwatch = Stopwatch.StartNew();
        autoAdded = false;
        cartUpdateElapsedMs = 0;
        Matches.ReplaceWith(matches);
        SelectedItem = matches.FirstOrDefault();

        if (SelectedItem is null)
        {
            IsMatchesPopupOpen = false;
            SetStatus("pos.status.noLocalMatch");
            uiRefreshStopwatch.Stop();
            uiRefreshElapsedMs = uiRefreshStopwatch.ElapsedMilliseconds;
            return;
        }

        if (allowAutoAdd && (matches.Count == 1 || IsExactLookup(SelectedItem, submittedScanText)))
        {
            IsMatchesPopupOpen = false;
            var cartUpdateStopwatch = Stopwatch.StartNew();
            autoAdded = AddItem(SelectedItem, "scan-auto-add");
            cartUpdateStopwatch.Stop();
            cartUpdateElapsedMs = cartUpdateStopwatch.ElapsedMilliseconds;
            if (autoAdded)
            {
                ScanText = string.Empty;
            }
        }
        else
        {
            IsMatchesPopupOpen = true;
            SetStatus("pos.status.multipleMatches", matches.Count);
        }

        uiRefreshStopwatch.Stop();
        uiRefreshElapsedMs = uiRefreshStopwatch.ElapsedMilliseconds;
    }

    private void AddSelected()
    {
        if (SelectedItem is null)
        {
            return;
        }

        AddItem(SelectedItem, "manual-add-selected");
    }

    private void SelectMatch(SellableItemDto? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;
        if (AddItem(item, "manual-select-match"))
        {
            ScanText = string.Empty;
            IsMatchesPopupOpen = false;
            IsTouchKeyboardOpen = false;
        }
    }

    private void RemoveLine(CartLine? line)
    {
        var stopwatch = Stopwatch.StartNew();
        if (line is null)
        {
            stopwatch.Stop();
            LogCartOperation("remove-line", line, success: false, stopwatch.ElapsedMilliseconds, "null-line");
            return;
        }

        if (!_cart.RemoveLine(line))
        {
            stopwatch.Stop();
            LogCartOperation("remove-line", line, success: false, stopwatch.ElapsedMilliseconds, "not-found");
            return;
        }

        SetStatus("pos.status.ready");
        stopwatch.Stop();
        LogCartOperation("remove-line", line, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void IncreaseLine(CartLine? line)
    {
        var stopwatch = Stopwatch.StartNew();
        if (line is null)
        {
            stopwatch.Stop();
            LogCartOperation("increase-line", line, success: false, stopwatch.ElapsedMilliseconds, "null-line");
            return;
        }

        if (!_cart.IncreaseLine(line))
        {
            SetStatus("cart.status.quantityMustBeInteger");
            stopwatch.Stop();
            LogCartOperation("increase-line", line, success: false, stopwatch.ElapsedMilliseconds, "quantity-not-integer-or-not-found");
            return;
        }

        SelectCartLine(line);
        SetStatus("pos.status.ready");
        stopwatch.Stop();
        LogCartOperation("increase-line", line, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void DecreaseLine(CartLine? line)
    {
        var stopwatch = Stopwatch.StartNew();
        if (line is null)
        {
            stopwatch.Stop();
            LogCartOperation("decrease-line", line, success: false, stopwatch.ElapsedMilliseconds, "null-line");
            return;
        }

        if (!_cart.DecreaseLine(line))
        {
            SetStatus("cart.status.quantityMustBeInteger");
            stopwatch.Stop();
            LogCartOperation("decrease-line", line, success: false, stopwatch.ElapsedMilliseconds, "quantity-not-integer-or-not-found");
            return;
        }

        if (_cart.Lines.Contains(line))
        {
            SelectCartLine(line);
        }

        SetStatus("pos.status.ready");
        stopwatch.Stop();
        LogCartOperation("decrease-line", line, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void ModifySelectedLineQuantity()
    {
        var stopwatch = Stopwatch.StartNew();
        if (!TryGetSelectedLineKeypadValue(out var line, out var value))
        {
            stopwatch.Stop();
            LogCartOperation("set-line-quantity", SelectedCartLine, success: false, stopwatch.ElapsedMilliseconds, "invalid-keypad-or-no-selection");
            return;
        }

        if (value <= 0m)
        {
            SetStatus("pos.status.quantityMustBePositive");
            stopwatch.Stop();
            LogCartOperation("set-line-quantity", line, success: false, stopwatch.ElapsedMilliseconds, "quantity-not-positive");
            return;
        }

        if (!PosCartService.IsPositiveIntegerQuantity(value))
        {
            SetStatus("cart.status.quantityMustBeInteger");
            stopwatch.Stop();
            LogCartOperation("set-line-quantity", line, success: false, stopwatch.ElapsedMilliseconds, "quantity-not-integer");
            return;
        }

        if (!_cart.SetLineQuantity(line, value))
        {
            stopwatch.Stop();
            LogCartOperation("set-line-quantity", line, success: false, stopwatch.ElapsedMilliseconds, "not-found");
            return;
        }

        SelectCartLine(line);
        KeypadBuffer = string.Empty;
        SetStatus("pos.status.lineQuantityUpdated");
        stopwatch.Stop();
        LogCartOperation("set-line-quantity", line, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void ModifySelectedLinePrice()
    {
        var stopwatch = Stopwatch.StartNew();
        if (!TryGetSelectedLineKeypadValue(out var line, out var value))
        {
            stopwatch.Stop();
            LogCartOperation("set-line-price", SelectedCartLine, success: false, stopwatch.ElapsedMilliseconds, "invalid-keypad-or-no-selection");
            return;
        }

        if (!_cart.SetLineUnitPrice(line, value))
        {
            stopwatch.Stop();
            LogCartOperation("set-line-price", line, success: false, stopwatch.ElapsedMilliseconds, "not-found");
            return;
        }

        SelectCartLine(line);
        KeypadBuffer = string.Empty;
        SetStatus("pos.status.linePriceUpdated");
        stopwatch.Stop();
        LogCartOperation("set-line-price", line, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void ApplySelectedLineDiscountAmount()
    {
        if (IsWholeOrderOperation)
        {
            ApplyWholeOrderDiscountAmount();
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        if (!TryGetSelectedLineKeypadValue(out var line, out var value))
        {
            stopwatch.Stop();
            LogCartOperation("set-line-discount-amount", SelectedCartLine, success: false, stopwatch.ElapsedMilliseconds, "invalid-keypad-or-no-selection");
            return;
        }

        if (value > line.GrossAmount)
        {
            SetStatus("pos.status.discountAmountTooHigh");
            stopwatch.Stop();
            LogCartOperation("set-line-discount-amount", line, success: false, stopwatch.ElapsedMilliseconds, "discount-too-high");
            return;
        }

        if (!_cart.SetLineDiscountAmount(line, value))
        {
            stopwatch.Stop();
            LogCartOperation("set-line-discount-amount", line, success: false, stopwatch.ElapsedMilliseconds, "not-found");
            return;
        }

        SelectCartLine(line);
        KeypadBuffer = string.Empty;
        SetStatus("pos.status.lineDiscountUpdated");
        stopwatch.Stop();
        LogCartOperation("set-line-discount-amount", line, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void ApplySelectedLineDiscountPercent()
    {
        if (IsWholeOrderOperation)
        {
            ApplyWholeOrderDiscountPercent();
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        if (!TryGetSelectedLineKeypadValue(out var line, out var value))
        {
            stopwatch.Stop();
            LogCartOperation("set-line-discount-percent", SelectedCartLine, success: false, stopwatch.ElapsedMilliseconds, "invalid-keypad-or-no-selection");
            return;
        }

        if (value > 100m)
        {
            SetStatus("pos.status.discountPercentOutOfRange");
            stopwatch.Stop();
            LogCartOperation("set-line-discount-percent", line, success: false, stopwatch.ElapsedMilliseconds, "discount-percent-out-of-range");
            return;
        }

        if (!_cart.SetLineDiscountPercent(line, value))
        {
            stopwatch.Stop();
            LogCartOperation("set-line-discount-percent", line, success: false, stopwatch.ElapsedMilliseconds, "not-found");
            return;
        }

        SelectCartLine(line);
        KeypadBuffer = string.Empty;
        SetStatus("pos.status.lineDiscountUpdated");
        stopwatch.Stop();
        LogCartOperation("set-line-discount-percent", line, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void ApplyQuickDiscountPercent(string? value)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var discountPercent) ||
            discountPercent < 0m)
        {
            SetStatus("pos.status.invalidKeypadValue");
            return;
        }

        if (discountPercent > 100m)
        {
            SetStatus("pos.status.discountPercentOutOfRange");
            return;
        }

        if (IsWholeOrderOperation)
        {
            ApplyWholeOrderDiscountPercent(discountPercent);
            return;
        }

        ApplySelectedLineDiscountPercent(discountPercent);
    }

    private void ApplyWholeOrderDiscountAmount()
    {
        var stopwatch = Stopwatch.StartNew();
        if (!TryGetOrderDiscountKeypadValue(out var value))
        {
            stopwatch.Stop();
            LogCartOperation("set-order-discount-amount", (CartLine?)null, success: false, stopwatch.ElapsedMilliseconds, "invalid-keypad-or-empty-cart");
            return;
        }

        if (value > _cart.TotalAmount)
        {
            SetStatus("pos.status.discountAmountTooHigh");
            stopwatch.Stop();
            LogCartOperation("set-order-discount-amount", (CartLine?)null, success: false, stopwatch.ElapsedMilliseconds, "discount-too-high");
            return;
        }

        if (!_cart.SetOrderDiscountAmount(value))
        {
            stopwatch.Stop();
            LogCartOperation("set-order-discount-amount", (CartLine?)null, success: false, stopwatch.ElapsedMilliseconds, "not-applied");
            return;
        }

        IsWholeOrderOperation = false;
        KeypadBuffer = string.Empty;
        SetStatus("pos.status.orderDiscountUpdated");
        stopwatch.Stop();
        LogCartOperation("set-order-discount-amount", (CartLine?)null, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void ApplyWholeOrderDiscountPercent()
    {
        if (!TryGetOrderDiscountKeypadValue(out var value))
        {
            return;
        }

        ApplyWholeOrderDiscountPercent(value);
    }

    private void ApplyWholeOrderDiscountPercent(decimal value)
    {
        var stopwatch = Stopwatch.StartNew();
        if (_cart.IsEmpty)
        {
            SetStatus("pos.status.selectCartLine");
            stopwatch.Stop();
            LogCartOperation("set-order-discount-percent", (CartLine?)null, success: false, stopwatch.ElapsedMilliseconds, "empty-cart");
            return;
        }

        if (value > 100m)
        {
            SetStatus("pos.status.discountPercentOutOfRange");
            stopwatch.Stop();
            LogCartOperation("set-order-discount-percent", (CartLine?)null, success: false, stopwatch.ElapsedMilliseconds, "discount-percent-out-of-range");
            return;
        }

        if (!_cart.SetOrderDiscountPercent(value))
        {
            stopwatch.Stop();
            LogCartOperation("set-order-discount-percent", (CartLine?)null, success: false, stopwatch.ElapsedMilliseconds, "not-applied");
            return;
        }

        IsWholeOrderOperation = false;
        KeypadBuffer = string.Empty;
        SetStatus("pos.status.orderDiscountUpdated");
        stopwatch.Stop();
        LogCartOperation("set-order-discount-percent", (CartLine?)null, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void ApplySelectedLineDiscountPercent(decimal value)
    {
        var stopwatch = Stopwatch.StartNew();
        if (SelectedCartLine is null)
        {
            SetStatus("pos.status.selectCartLine");
            stopwatch.Stop();
            LogCartOperation("quick-line-discount-percent", (CartLine?)null, success: false, stopwatch.ElapsedMilliseconds, "no-selection");
            return;
        }

        if (value > 100m)
        {
            SetStatus("pos.status.discountPercentOutOfRange");
            stopwatch.Stop();
            LogCartOperation("quick-line-discount-percent", SelectedCartLine, success: false, stopwatch.ElapsedMilliseconds, "discount-percent-out-of-range");
            return;
        }

        var line = SelectedCartLine;
        if (!_cart.SetLineDiscountPercent(line, value))
        {
            stopwatch.Stop();
            LogCartOperation("quick-line-discount-percent", line, success: false, stopwatch.ElapsedMilliseconds, "not-found");
            return;
        }

        SelectCartLine(line);
        KeypadBuffer = string.Empty;
        SetStatus("pos.status.lineDiscountUpdated");
        stopwatch.Stop();
        LogCartOperation("quick-line-discount-percent", line, success: true, stopwatch.ElapsedMilliseconds);
    }

    private bool AddItem(SellableItemDto item, string operation)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!PosCartService.IsPositiveIntegerQuantity(item.QuantityFactor))
        {
            SetStatus("cart.status.quantityMustBeInteger");
            stopwatch.Stop();
            LogCartOperation(operation, item, success: false, stopwatch.ElapsedMilliseconds, "quantity-factor-not-integer");
            return false;
        }

        CartLine line;
        try
        {
            line = _cart.AddItem(item);
        }
        catch (InvalidOperationException)
        {
            SetStatus("cart.status.quantityMustBeInteger");
            stopwatch.Stop();
            LogCartOperation(operation, item, success: false, stopwatch.ElapsedMilliseconds, "cart-quantity-not-integer");
            return false;
        }

        SelectCartLine(line);
        IsTouchKeyboardOpen = false;
        SetStatus("pos.status.added", item.DisplayName);
        BeginRemoteLookup(line, item);
        stopwatch.Stop();
        LogCartOperation(operation, line, success: true, stopwatch.ElapsedMilliseconds);
        return true;
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
        string? reason = null)
    {
        LogCartPerf(
            $"operation={operation} storeCode={Session.StoreCode} productCode={LogValue(item.ProductCode)} lookupCode={LogValue(item.LookupCode)} success={FormatBool(success)} cartLines={_cart.Lines.Count} totalAmount={FormatAmount(_cart.TotalAmount)} actualAmount={FormatAmount(_cart.ActualAmount)} totalElapsedMs={totalElapsedMs}{FormatReason(reason)}");
    }

    private void LogCartOperation(
        string operation,
        CartLine? line,
        bool success,
        long totalElapsedMs,
        string? reason = null)
    {
        LogCartPerf(
            $"operation={operation} storeCode={Session.StoreCode} productCode={LogValue(line?.ProductCode)} lookupCode={LogValue(line?.LookupCode)} success={FormatBool(success)} cartLines={_cart.Lines.Count} totalAmount={FormatAmount(_cart.TotalAmount)} actualAmount={FormatAmount(_cart.ActualAmount)} totalElapsedMs={totalElapsedMs}{FormatReason(reason)}");
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

    private static string LogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value.Trim();
    }

    private bool TryGetSelectedLineKeypadValue(out CartLine line, out decimal value)
    {
        value = 0m;

        if (SelectedCartLine is null)
        {
            line = null!;
            SetStatus("pos.status.selectCartLine");
            return false;
        }

        line = SelectedCartLine;
        return TryGetKeypadValue(out value);
    }

    private bool TryGetOrderDiscountKeypadValue(out decimal value)
    {
        value = 0m;

        if (_cart.IsEmpty)
        {
            SetStatus("pos.status.selectCartLine");
            return false;
        }

        return TryGetKeypadValue(out value);
    }

    private bool TryGetKeypadValue(out decimal value)
    {
        if (!decimal.TryParse(KeypadBuffer, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
            value < 0m)
        {
            SetStatus("pos.status.invalidKeypadValue");
            return false;
        }

        return true;
    }

    private void OnRawBarcodeScanned(RawBarcodeScannedEventArgs e)
    {
        ProcessScannerBarcode(e.Barcode, e.DevicePath, "raw");
    }

    public void ProcessScannerBarcode(string barcode, string devicePath, string source)
    {
        ConsoleLog.Write("PosScan", $"{source} scanner event received barcode={barcode} devicePath={devicePath}");
        ScanText = barcode;
        IsTouchKeyboardOpen = false;
        ProcessScan(barcode, preferExactLookup: true, source);
    }

    private void ClearSearch()
    {
        ScanText = string.Empty;
        IsMatchesPopupOpen = false;
        IsTouchKeyboardOpen = false;
    }

    private void ClearCart()
    {
        var stopwatch = Stopwatch.StartNew();
        _cart.Clear();
        SetStatus("pos.status.cartCleared");
        stopwatch.Stop();
        LogCartOperation("clear-cart", (CartLine?)null, success: true, stopwatch.ElapsedMilliseconds);
    }

    private void OpenPayment()
    {
        var stopwatch = Stopwatch.StartNew();
        if (_cart.HasNonIntegerQuantity)
        {
            SetStatus("cart.status.quantityMustBeInteger");
            stopwatch.Stop();
            LogCartOperation("open-payment", (CartLine?)null, success: false, stopwatch.ElapsedMilliseconds, "quantity-not-integer");
            return;
        }

        if (_cart.HasZeroPriceLine)
        {
            SetStatus("cart.status.zeroPriceItem");
            stopwatch.Stop();
            LogCartOperation("open-payment", (CartLine?)null, success: false, stopwatch.ElapsedMilliseconds, "zero-price-line");
            return;
        }

        PaymentRequested?.Invoke(this, EventArgs.Empty);
        _onOpenPayment?.Invoke();
        stopwatch.Stop();
        LogCartOperation("open-payment", (CartLine?)null, success: true, stopwatch.ElapsedMilliseconds);
    }

    private async Task OpenSpecialProductsAsync()
    {
        if (_onOpenSpecialProductsAsync is not null)
        {
            await _onOpenSpecialProductsAsync();
        }
    }

    private async Task ReregisterDeviceAsync()
    {
        if (_onReregisterDeviceAsync is not null)
        {
            await _onReregisterDeviceAsync();
        }
    }

    private void BeginRemoteLookup(CartLine line, SellableItemDto item)
    {
        if (!Session.IsOnline || _remoteLookupRefreshAsync is null)
        {
            return;
        }

        var snapshot = new RemoteLookupCartSnapshot(
            line,
            Session.StoreCode,
            item.LookupCode,
            item.ProductCode,
            item.ReferenceCode);
        _ = RefreshRemoteLookupAsync(snapshot);
    }

    private async Task RefreshRemoteLookupAsync(RemoteLookupCartSnapshot snapshot)
    {
        using var timeoutCts = new CancellationTokenSource(RemoteLookupTimeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _remoteLookupRefreshAsync!(
                snapshot.StoreCode,
                snapshot.LookupCode,
                timeoutCts.Token);
            stopwatch.Stop();

            if (result.Updated && result.Item is not null)
            {
                if (CanApplyRemoteItemToCartLine(snapshot, result.Item))
                {
                    var updated = _cart.UpdateLineFromRemote(snapshot.Line, result.Item);
                    ConsoleLog.Write(
                        "PosScan",
                        $"remote lookup cart update storeCode={snapshot.StoreCode} lookupCode={snapshot.LookupCode} productCode={snapshot.ProductCode} referenceCode={snapshot.ReferenceCode ?? "<null>"} updated={updated} elapsedMs={stopwatch.ElapsedMilliseconds}");
                }
                else
                {
                    ConsoleLog.Write(
                        "PosScan",
                        $"remote lookup ignored for cart storeCode={snapshot.StoreCode} lookupCode={snapshot.LookupCode} localProductCode={snapshot.ProductCode} localReferenceCode={snapshot.ReferenceCode ?? "<null>"} remoteProductCode={result.Item.ProductCode} remoteReferenceCode={result.Item.ReferenceCode ?? "<null>"} elapsedMs={stopwatch.ElapsedMilliseconds}");
                }
            }
            else if (result.Deleted)
            {
                ConsoleLog.Write(
                    "PosScan",
                    $"remote lookup deleted local cache only storeCode={result.StoreCode} lookupCode={result.LookupCode} deletedCount={result.DeletedCount} elapsedMs={stopwatch.ElapsedMilliseconds}");
            }

            var catalogItems = await ReloadCatalogAsync(CancellationToken.None);
            RefreshMatches(catalogItems);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
        {
            stopwatch.Stop();
            ConsoleLog.Write(
                "PosScan",
                $"remote lookup timeout storeCode={snapshot.StoreCode} lookupCode={snapshot.LookupCode} timeoutMs={RemoteLookupTimeout.TotalMilliseconds:0} elapsedMs={stopwatch.ElapsedMilliseconds} error={ex.Message}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ConsoleLog.Write(
                "PosScan",
                $"remote lookup failed storeCode={snapshot.StoreCode} lookupCode={snapshot.LookupCode} elapsedMs={stopwatch.ElapsedMilliseconds} error={ex.Message}");
        }
    }

    private static bool CanApplyRemoteItemToCartLine(RemoteLookupCartSnapshot snapshot, SellableItemDto item)
    {
        return EqualsIdentity(snapshot.StoreCode, item.StoreCode) &&
            EqualsIdentity(snapshot.ProductCode, item.ProductCode) &&
            EqualsIdentity(snapshot.ReferenceCode, item.ReferenceCode);
    }

    private static bool EqualsIdentity(string? left, string? right)
    {
        return string.Equals(NormalizeIdentity(left), NormalizeIdentity(right), StringComparison.Ordinal);
    }

    private static string NormalizeIdentity(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private sealed record RemoteLookupCartSnapshot(
        CartLine Line,
        string StoreCode,
        string LookupCode,
        string ProductCode,
        string? ReferenceCode);

    private async Task SyncAsync()
    {
        await RunCatalogDownloadAsync(
            _syncCatalogAsync,
            "Syncing catalog...",
            "Catalog sync completed.",
            "Catalog sync failed");
    }

    private async Task ResetCatalogAsync()
    {
        await RunCatalogDownloadAsync(
            _resetCatalogAsync,
            "Resetting catalog...",
            "Catalog reset completed.",
            "Catalog reset failed");
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
            SetStatusText("Offline: catalog sync skipped.");
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
        catch (Exception ex)
        {
            SetStatusText($"{failedPrefix}: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<SellableItemDto>> ReloadCatalogAsync(CancellationToken cancellationToken)
    {
        return _reloadCatalogAsync is null
            ? _priceIndex.Items
            : await _reloadCatalogAsync(cancellationToken);
    }

    private void RefreshMatches(IReadOnlyList<SellableItemDto> catalogItems)
    {
        var matches = string.IsNullOrWhiteSpace(ScanText)
            ? catalogItems.Take(8)
            : _priceIndex.Search(Session.StoreCode, ScanText);
        Matches.ReplaceWith(matches);
        SelectedItem = Matches.FirstOrDefault();
    }

    private static bool IsExactLookup(SellableItemDto item, string query)
    {
        var normalized = query.Trim();
        return string.Equals(item.Barcode, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.LookupCode, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.ItemNumber, normalized, StringComparison.OrdinalIgnoreCase);
    }

    private void SetStatus(string key, params object[] args)
    {
        _statusText = null;
        _statusKey = key;
        _statusArgs = args;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void SetStatusText(string message)
    {
        _statusText = message;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private string T(string key)
    {
        return _localization?.T(key) ?? key;
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
        OnPropertyChanged(nameof(MemberText));
        OnPropertyChanged(nameof(SyncText));
        OnPropertyChanged(nameof(CatalogResetText));
        OnPropertyChanged(nameof(ReregisterDeviceText));
        OnPropertyChanged(nameof(OnlineText));
        OnPropertyChanged(nameof(PendingSyncText));
        OnPropertyChanged(nameof(StatusMessage));
    }
}
