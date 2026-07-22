using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.Services.Facades;

namespace Hbpos.Client.Wpf.ViewModels;

internal sealed class ScreenNavigator
{
    private readonly MainChildViewModelFactory _factory;
    private readonly PosCartService _cart;
    private readonly ILocalizationService _localization;
    private readonly ISuspendedOrderService? _suspendedOrderService;
    private readonly ICardTerminalSetupService? _cardTerminalSetupService;
    private readonly ITestSalesDataResetService? _testSalesDataResetService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ICustomerDisplayOrchestrator _customerDisplayOrchestrator;
    private readonly ILinklyFallbackPromptCoordinator? _linklyFallbackPromptCoordinator;
    private readonly Func<CancellationToken, Task> _syncCatalogAndReloadAsync;
    private readonly Func<CancellationToken, Task> _resetCatalogAndReloadAsync;
    private readonly Func<CancellationToken, Task<AppUpdateCoordinatorResult>>? _checkForAppUpdateAsync;
    private readonly Func<Task<DeviceReregistrationStartResult>> _beginDeviceReregistrationAsync;
    private readonly Func<Task<bool>>? _recoverActiveCardPaymentSessionFromPaymentAsync;
    private readonly Func<InstallmentOrderSummary, Task> _onInstallmentOrderCreatedAsync;

    // Callbacks provided by MainViewModel for integration points.
    private readonly Action<object?> _setScreen;
    private readonly Action<PaymentViewModel> _onPaymentCreated;
    private readonly Action<PaymentViewModel> _onPaymentDisposed;
    private readonly Func<TransactionHistoryViewModel, Task> _printSelectedHistoryReceiptAsync;
    private readonly Action<string> _setStatusMessage;
    private readonly Func<LocalOrder?> _getLastCompletedOrder;
    private readonly Action<LocalOrder?> _setLastCompletedOrder;

    // Screen state
    private object? _currentScreen;
    private PosTerminalViewModel? _cachedPosTerminalScreen;
    private PaymentViewModel? _cachedCashPaymentScreen;
    private SpecialProductsViewModel? _cachedSpecialProductsScreen;

    // Navigation commands
    public IRelayCommand ShowPosCommand { get; }
    public IRelayCommand ShowCashPaymentCommand { get; }
    public IRelayCommand ShowReturnsCommand { get; }
    public IAsyncRelayCommand ShowPaymentSuccessCommand { get; }
    public IAsyncRelayCommand ShowHistoryCommand { get; }
    public IAsyncRelayCommand ShowDailyCloseCommand { get; }
    public IRelayCommand ShowCustomerDisplayCommand { get; }
    public IAsyncRelayCommand ShowSettingsCommand { get; }


    public ScreenNavigator(
        MainChildViewModelFactory factory,
        PosCartService cart,
        ILocalizationService localization,
        ISuspendedOrderService? suspendedOrderService,
        ICardTerminalSetupService? cardTerminalSetupService,
        ITestSalesDataResetService? testSalesDataResetService,
        IConfirmationDialogService confirmationDialogService,
        ICustomerDisplayOrchestrator customerDisplayOrchestrator,
        ILinklyFallbackPromptCoordinator? linklyFallbackPromptCoordinator,
        Func<CancellationToken, Task> syncCatalogAndReloadAsync,
        Func<CancellationToken, Task> resetCatalogAndReloadAsync,
        Func<CancellationToken, Task<AppUpdateCoordinatorResult>>? checkForAppUpdateAsync,
        Func<Task<DeviceReregistrationStartResult>> beginDeviceReregistrationAsync,
        Func<Task<bool>>? recoverActiveCardPaymentSessionFromPaymentAsync,
        Func<InstallmentOrderSummary, Task> onInstallmentOrderCreatedAsync,
        Action<object?> setScreen,
        Action<PaymentViewModel> onPaymentCreated,
        Action<PaymentViewModel> onPaymentDisposed,
        Func<TransactionHistoryViewModel, Task> printSelectedHistoryReceiptAsync,
        Action<string> setStatusMessage,
        Func<LocalOrder?> getLastCompletedOrder,
        Action<LocalOrder?> setLastCompletedOrder)
    {
        _factory = factory;
        _cart = cart;
        _localization = localization;
        _suspendedOrderService = suspendedOrderService;
        _cardTerminalSetupService = cardTerminalSetupService;
        _testSalesDataResetService = testSalesDataResetService;
        _confirmationDialogService = confirmationDialogService;
        _customerDisplayOrchestrator = customerDisplayOrchestrator;
        _linklyFallbackPromptCoordinator = linklyFallbackPromptCoordinator;
        _syncCatalogAndReloadAsync = syncCatalogAndReloadAsync;
        _resetCatalogAndReloadAsync = resetCatalogAndReloadAsync;
        _checkForAppUpdateAsync = checkForAppUpdateAsync;
        _beginDeviceReregistrationAsync = beginDeviceReregistrationAsync;
        _recoverActiveCardPaymentSessionFromPaymentAsync = recoverActiveCardPaymentSessionFromPaymentAsync;
        _onInstallmentOrderCreatedAsync = onInstallmentOrderCreatedAsync;
        _setScreen = setScreen;
        _onPaymentCreated = onPaymentCreated;
        _onPaymentDisposed = onPaymentDisposed;
        _printSelectedHistoryReceiptAsync = printSelectedHistoryReceiptAsync;
        _setStatusMessage = setStatusMessage;
        _getLastCompletedOrder = getLastCompletedOrder;
        _setLastCompletedOrder = setLastCompletedOrder;

        ShowPosCommand = new RelayCommand(ShowPos);
        ShowCashPaymentCommand = new RelayCommand(ShowCashPayment, () => !_cart.IsEmpty);
        ShowReturnsCommand = new RelayCommand(ShowReturns);
        ShowPaymentSuccessCommand = new AsyncRelayCommand(ShowPaymentSuccessLatestAsync);
        ShowHistoryCommand = new AsyncRelayCommand(ShowHistoryAsync);
        ShowDailyCloseCommand = new AsyncRelayCommand(ShowDailyCloseAsync);
        ShowCustomerDisplayCommand = new RelayCommand(ShowCustomerDisplay);
        ShowSettingsCommand = new AsyncRelayCommand(ShowSettingsAsync);

    }

    // ── Public getters for MainViewModel pass-through ──

    public object? CurrentScreen => _currentScreen;

    public void SetCurrentScreen(object? screen)
    {
        _currentScreen = screen;
        _setScreen(screen);
    }

    public PosTerminalViewModel? CachedPosTerminalScreen => _cachedPosTerminalScreen;
    public PaymentViewModel? CachedCashPaymentScreen => _cachedCashPaymentScreen;
    public SpecialProductsViewModel? CachedSpecialProductsScreen => _cachedSpecialProductsScreen;

    // ── Screen active-state computed properties ──

    public bool IsPosTerminalScreenActive => ReferenceEquals(_currentScreen, _cachedPosTerminalScreen);
    public bool IsCashPaymentScreenActive => ReferenceEquals(_currentScreen, _cachedCashPaymentScreen);
    public bool IsSpecialProductsScreenActive => ReferenceEquals(_currentScreen, _cachedSpecialProductsScreen);
    public bool IsFallbackScreenActive => _currentScreen is not null &&
        !IsPosTerminalScreenActive &&
        !IsCashPaymentScreenActive &&
        !IsSpecialProductsScreenActive;

    public string ActivePageTitleText => GetActivePageTitleText();

    // ── Screen VM instances ──

    public PosTerminalViewModel? PosTerminal { get; set; }
    public SpecialProductsViewModel? SpecialProducts { get; set; }
    public PaymentViewModel? CashPayment { get; set; }
    public InstallmentCenterViewModel? InstallmentCenter { get; set; }
    public InstallmentCreateViewModel? InstallmentCreate { get; set; }
    public ReceiptReturnsViewModel? ReceiptReturns { get; set; }
    public PaymentSuccessViewModel PaymentSuccess { get; set; } = null!;
    public TransactionHistoryViewModel? TransactionHistory { get; set; }
    public DailyCloseViewModel? DailyClose { get; set; }
    public SettingsViewModel? Settings { get; set; }

    private CustomerDisplayViewModel? _customerDisplay;
    public CustomerDisplayViewModel CustomerDisplay => _customerDisplay ??= _factory.CreateCustomerDisplayViewModel();

    // ── Session (updated by MainViewModel) ──

    public PosSessionState Session { get; set; } = new("HB POS", "1002", "Main Branch", "Terminal 04", "C001", "Alice", false, 0);

    // ── Startup options ──

    public AppStartupOptions? StartupOptions { get; set; }

    // ── Navigation methods ──

    public void ShowPos()
    {
        if (PosTerminal is null)
        {
            return;
        }

        SetCurrentScreen(PosTerminal);
    }

    public Task ShowSpecialProductsAsync()
    {
        if (SpecialProducts is null)
        {
            return Task.CompletedTask;
        }

        SpecialProducts.Session = Session;
        PrepareCachedSpecialProductsScreen();
        SpecialProducts.ActivateForEntry();
        SetCurrentScreen(SpecialProducts);
        _ = EnsureSpecialProductsLoadedAsync(SpecialProducts);
        return Task.CompletedTask;
    }

    public void ShowReturns()
    {
        if (ReceiptReturns is null)
        {
            return;
        }

        ReceiptReturns.Session = Session;
        SetCurrentScreen(ReceiptReturns);
    }

    public void ShowCashPayment()
    {
        if (_cart.IsEmpty)
        {
            ShowPos();
            return;
        }

        PrepareCachedCashPaymentScreen();
        if (CashPayment is null)
        {
            ShowPos();
            return;
        }

        CashPayment.PrepareForEntry(Session);
        SetCurrentScreen(CashPayment);
    }

    public void ShowInstallmentCenter()
    {
        if (CashPayment is null)
        {
            return;
        }

        // 分期入口从支付页进入，先带上当前购物车汇总做 UI 骨架展示。
        InstallmentCenter ??= CreateInstallmentCenterViewModel();
        InstallmentCenter.Prepare(Session, CreateCurrentCartSnapshot());
        SetCurrentScreen(InstallmentCenter);
        _ = InstallmentCenter.LoadAsync();
    }

    public async Task ShowInstallmentCreateAsync(PosCartServiceSnapshot? cartSnapshot)
    {
        InstallmentCreate ??= CreateInstallmentCreateViewModel();
        InstallmentCreate.Prepare(Session, cartSnapshot);
        SetCurrentScreen(InstallmentCreate);
        await Task.CompletedTask;
    }

    public Task ShowInstallmentRepaymentAsync(InstallmentOrderSummary order)
    {
        PrepareCachedCashPaymentScreen();
        if (CashPayment is null)
        {
            ShowPos();
            return Task.CompletedTask;
        }

        // 中文注释：历史分期续付复用普通支付页，只替换收款目标为原分期单余额。
        CashPayment.PrepareForInstallmentRepayment(Session, order);
        SetCurrentScreen(CashPayment);
        return Task.CompletedTask;
    }

    public async Task ShowPaymentSuccessLatestAsync()
    {
        var lastCompletedOrder = _getLastCompletedOrder();
        if (lastCompletedOrder is not null)
        {
            await PaymentSuccess.LoadFromOrderAsync(lastCompletedOrder);
        }
        else
        {
            await PaymentSuccess.LoadLatestAsync();
        }

        SetCurrentScreen(PaymentSuccess);
    }

    public async Task ShowHistoryAsync()
    {
        TransactionHistory ??= CreateTransactionHistoryViewModel();
        await TransactionHistory.LoadAsync();
        SetCurrentScreen(TransactionHistory);
    }

    public async Task ShowDailyCloseAsync()
    {
        DailyClose ??= _factory.CreateDailyCloseViewModel(
            Session,
            ShowPos);
        DailyClose.Session = Session;
        await DailyClose.LoadAsync();
        SetCurrentScreen(DailyClose);
    }

    public async Task ShowSettingsAsync()
    {
        if (_cardTerminalSetupService is null)
        {
            _setStatusMessage(_localization.T("main.settingsUnavailable"));
            return;
        }

        Func<CancellationToken, Task>? resetTestSalesDataAsync = null;
        Func<Task<bool>>? confirmResetTestSalesDataAsync = null;
#if DEBUG
        resetTestSalesDataAsync = async cancellationToken =>
        {
            if (_testSalesDataResetService is null)
            {
                throw new InvalidOperationException(_localization.T("settings.status.testSalesDataResetNotConfigured"));
            }

            await _testSalesDataResetService.ResetAsync(cancellationToken);
        };
        confirmResetTestSalesDataAsync = _confirmationDialogService.ConfirmResetTestSalesDataAsync;
#endif

        Settings ??= _factory.CreateSettingsViewModel(
            session: Session,
            downloadCatalogAsync: async cancellationToken =>
            {
                await _syncCatalogAndReloadAsync(cancellationToken);
            },
            resetCatalogAsync: async cancellationToken =>
            {
                await _resetCatalogAndReloadAsync(cancellationToken);
            },
            reregisterDeviceAsync: _beginDeviceReregistrationAsync,
            returnToPos: ShowPos,
            resetTestSalesDataAsync: resetTestSalesDataAsync,
            confirmResetTestSalesDataAsync: confirmResetTestSalesDataAsync,
            checkForAppUpdateAsync: _checkForAppUpdateAsync);
        await Settings.LoadAsync();
        SetCurrentScreen(Settings);
    }

    public async Task ShowSuspendedHistoryAsync()
    {
        TransactionHistory ??= CreateTransactionHistoryViewModel();
        await TransactionHistory.ShowSuspendedOrdersAsync();
        SetCurrentScreen(TransactionHistory);
    }

    public async Task SuspendCurrentOrderAsync()
    {
        if (_suspendedOrderService is null)
        {
            _setStatusMessage(_localization.T("main.suspendedUnavailable"));
            return;
        }

        try
        {
            var suspended = await _suspendedOrderService.SuspendCurrentOrderAsync(Session);
            PosTerminal?.RefreshCart();
            CashPayment?.RefreshCart();
            _setStatusMessage(string.Format(_localization.CurrentCulture, _localization.T("main.suspendedSaved"), suspended.SuspendedOrderGuid.ToString("N")[..8].ToUpperInvariant()));
        }
        catch (Exception ex)
        {
            _setStatusMessage(ex.Message);
        }
    }

    public void ShowCustomerDisplay()
    {
        LoadCustomerDisplayFromCart(forceAdvertisementRefresh: true);
        SetCurrentScreen(CustomerDisplay);
    }

    public void NavigateFromStartup(string? initialScreen)
    {
        switch ((initialScreen ?? "pos").Trim().ToLowerInvariant())
        {
            case "cash":
            case "payment":
                ShowCashPayment();
                break;
            case "success":
                SetCurrentScreen(PaymentSuccess);
                var lastCompletedOrder = _getLastCompletedOrder();
                if (lastCompletedOrder is not null)
                {
                    PaymentSuccess.LoadFromOrder(lastCompletedOrder);
                }
                break;
            case "history":
                _ = ShowHistoryAsync();
                break;
            case "settings":
                _ = ShowSettingsAsync();
                break;
            case "customer":
            case "display":
                ShowCustomerDisplay();
                break;
            default:
                ShowPos();
                break;
        }
    }

    public Task OnSuspendedOrderRecalledAsync()
    {
        PosTerminal?.RefreshCart();
        CashPayment?.RefreshCart();
        ShowPos();
        _setStatusMessage(_localization.T("main.suspendedRecalled"));
        return Task.CompletedTask;
    }

    public void ResetForNewTransaction()
    {
        _cart.Clear();
        ShowCashPaymentCommand.NotifyCanExecuteChanged();
        ShowPos();
    }

    // ── Cache management ──

    public void PrepareCachedCashPaymentScreen()
    {
        if (CashPayment is null)
        {
            CashPayment = _factory.CreatePaymentViewModel(
                Session,
                ShowPos,
                ShowInstallmentCenter,
                _recoverActiveCardPaymentSessionFromPaymentAsync,
                _linklyFallbackPromptCoordinator,
                _onInstallmentOrderCreatedAsync,
                ConfirmInstallmentFullFirstPaymentAsync);
            _onPaymentCreated(CashPayment);
        }

        if (ReferenceEquals(_cachedCashPaymentScreen, CashPayment))
        {
            return;
        }

        _cachedCashPaymentScreen = CashPayment;
    }

    private Task<bool> ConfirmInstallmentFullFirstPaymentAsync() =>
        _confirmationDialogService.ConfirmInstallmentFullFirstPaymentAsync();

    public void PrepareCachedSpecialProductsScreen()
    {
        if (SpecialProducts is null || ReferenceEquals(_cachedSpecialProductsScreen, SpecialProducts))
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _cachedSpecialProductsScreen = SpecialProducts;
        stopwatch.Stop();
        ConsoleLog.Write(
            "SpecialProducts",
            $"special screen view prepared store={Session.StoreCode} elapsedMs={stopwatch.ElapsedMilliseconds}");
    }

    public void ClearCashPaymentCache()
    {
        if (CashPayment is not null)
        {
            _onPaymentDisposed(CashPayment);
            CashPayment.Dispose();
        }

        _cachedCashPaymentScreen = null;
        CashPayment = null;
    }

    public void SetCachedPosTerminalScreen(PosTerminalViewModel? value)
    {
        _cachedPosTerminalScreen = value;
    }

    public void SetCachedSpecialProductsScreen(SpecialProductsViewModel? value)
    {
        _cachedSpecialProductsScreen = value;
    }

    // ── Clear all screens (for dispose) ──

    public void ClearScreens()
    {
        PosTerminal?.Dispose();
        PosTerminal = null;
        SpecialProducts?.Dispose();
        SpecialProducts = null;
        ReceiptReturns?.Dispose();
        ReceiptReturns = null;
        Settings?.Dispose();
        Settings = null;
        (DailyClose as IDisposable)?.Dispose();
        DailyClose = null;
        (InstallmentCenter as IDisposable)?.Dispose();
        InstallmentCenter = null;
        (InstallmentCreate as IDisposable)?.Dispose();
        InstallmentCreate = null;
        (TransactionHistory as IDisposable)?.Dispose();
        TransactionHistory = null;
        ClearCashPaymentCache();
        _customerDisplay = null;
    }

    // ── Helper methods ──

    public void LoadCustomerDisplayFromCart(bool forceAdvertisementRefresh = false)
    {
        // 显式打开客显页面要立即拿最新广告；购物车变化调用保持默认节流。
        _customerDisplayOrchestrator.LoadFromCart(
            CustomerDisplay,
            Session,
            _cart,
            forceAdvertisementRefresh: forceAdvertisementRefresh);
    }

    public PosCartServiceSnapshot? CreateCurrentCartSnapshot()
    {
        if (_cart.IsEmpty)
        {
            return null;
        }

        return new PosCartServiceSnapshot(
            _cart.TotalAmount,
            _cart.DiscountAmount,
            _cart.ActualAmount,
            _cart.Lines.Select(line => new PosCartLineServiceSnapshot(
                line.ProductCode,
                line.ReferenceCode,
                line.DisplayName,
                line.LookupCode,
                line.ItemNumber,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.ActualAmount)).ToList());
    }

    public void RefreshCartRelatedState()
    {
        CashPayment?.RefreshCart();
        InstallmentCenter?.Prepare(Session, CreateCurrentCartSnapshot());
        if (InstallmentCreate is not null)
        {
            InstallmentCreate.CartSnapshot = CreateCurrentCartSnapshot();
        }

        ShowCashPaymentCommand.NotifyCanExecuteChanged();
    }

    private string GetActivePageTitleText()
    {
        if (ReferenceEquals(_currentScreen, InstallmentCenter))
        {
            return InstallmentCenter?.PageTitleText ?? "分期中心";
        }

        if (ReferenceEquals(_currentScreen, InstallmentCreate))
        {
            return InstallmentCreate?.PageTitleText ?? "创建分期";
        }

        return _localization.T(GetActivePageTitleKey());
    }

    private string GetActivePageTitleKey()
    {
        if (ReferenceEquals(_currentScreen, PosTerminal))
        {
            return "shell.page.pos";
        }

        if (ReferenceEquals(_currentScreen, CashPayment))
        {
            return CashPayment?.PaymentMode switch
            {
                PaymentEntryMode.Refund => "shell.page.refund",
                PaymentEntryMode.ZeroSettlement => "shell.page.zeroSettlement",
                _ => "shell.page.payment"
            };
        }

        if (ReferenceEquals(_currentScreen, SpecialProducts))
        {
            return "shell.page.specialProducts";
        }

        if (ReferenceEquals(_currentScreen, ReceiptReturns))
        {
            return "shell.page.returns";
        }

        if (ReferenceEquals(_currentScreen, PaymentSuccess))
        {
            return "shell.page.paymentSuccess";
        }

        if (ReferenceEquals(_currentScreen, TransactionHistory))
        {
            return "shell.page.history";
        }

        if (ReferenceEquals(_currentScreen, DailyClose))
        {
            return "shell.page.dailyClose";
        }

        if (ReferenceEquals(_currentScreen, Settings))
        {
            return "shell.page.settings";
        }

        if (ReferenceEquals(_currentScreen, CustomerDisplay))
        {
            return "shell.page.customerDisplay";
        }

        if (ReferenceEquals(_currentScreen, DeviceRegistration))
        {
            return "shell.page.deviceRegistration";
        }

        return "shell.page.loading";
    }

    public DeviceRegistrationViewModel? DeviceRegistration { get; set; }

    // ── Factory helper methods for creating child VMs ──

    public TransactionHistoryViewModel CreateTransactionHistoryViewModel()
    {
        return _factory.CreateTransactionHistoryViewModel(
            Session,
            OnSuspendedOrderRecalledAsync,
            ShowPos,
            viewModel => _printSelectedHistoryReceiptAsync(viewModel),
            ShowInstallmentRepaymentAsync,
            _confirmationDialogService);
    }

    public InstallmentCenterViewModel CreateInstallmentCenterViewModel()
    {
        return _factory.CreateInstallmentCenterViewModel(
            Session,
            ShowInstallmentCreateAsync,
            ShowCashPayment);
    }

    public InstallmentCreateViewModel CreateInstallmentCreateViewModel()
    {
        return _factory.CreateInstallmentCreateViewModel(
            Session,
            _onInstallmentOrderCreatedAsync,
            () =>
            {
                InstallmentCenter ??= CreateInstallmentCenterViewModel();
                InstallmentCenter.Prepare(Session, CreateCurrentCartSnapshot());
                SetCurrentScreen(InstallmentCenter);
            });
    }

    public void ApplySessionToScreens()
    {
        if (PosTerminal is not null)
        {
            PosTerminal.Session = Session;
        }

        if (CashPayment is not null)
        {
            CashPayment.Session = Session;
        }

        if (SpecialProducts is not null)
        {
            SpecialProducts.Session = Session;
        }

        if (ReceiptReturns is not null)
        {
            ReceiptReturns.Session = Session;
        }

        if (TransactionHistory is not null)
        {
            TransactionHistory.Session = Session;
        }

        if (DailyClose is not null)
        {
            DailyClose.Session = Session;
        }

        if (Settings is not null)
        {
            Settings.Session = Session;
        }

        InstallmentCenter?.Prepare(Session, CreateCurrentCartSnapshot());
        if (InstallmentCreate is not null)
        {
            InstallmentCreate.Session = Session;
        }
    }

    private static async Task EnsureSpecialProductsLoadedAsync(SpecialProductsViewModel specialProducts)
    {
        try
        {
            await specialProducts.EnsureLoadedAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ConsoleLog.Write("SpecialProducts", $"background load failed error={ex.Message}");
        }
    }
}
