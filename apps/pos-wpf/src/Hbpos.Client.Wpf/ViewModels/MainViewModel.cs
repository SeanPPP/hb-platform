using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.Services.Facades;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Linkly;
using Hbpos.Contracts.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed record DeviceReregistrationStartResult(bool Started, string StatusMessage)
{
    public static DeviceReregistrationStartResult StartedWith(string statusMessage) => new(true, statusMessage);

    public static DeviceReregistrationStartResult Blocked(string statusMessage) => new(false, statusMessage);
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const string DefaultTestStoreCode = "1002";

    private readonly LocalSellableItemIndex _priceIndex;
    private readonly PosCartService _cart;
    private readonly CashCheckoutService _checkout;
    private readonly ILocalSchemaService _schema;
    private readonly IPosCoreServices _core;
    private readonly IPaymentTerminalFacade _paymentTerminal;
    private readonly IPrintFacade _print;
    private readonly IPosInfrastructureFacade _infra;
    private readonly ILocalCatalogRepository _catalogRepository;
    private readonly IShellCultureService _shellCultureService;
    private readonly IShellCatalogService _shellCatalogService;
    private readonly IRemoteLookupRefreshService _remoteLookupRefresh;
    private readonly ISpecialProductService _specialProductService;
    private readonly IConnectivityApiClient _connectivityApiClient;
    private readonly IMainShellStartupService _mainShellStartupService;
    private readonly ILocalOrderRepository _orderRepository;
    private readonly IShellSyncCenterService _shellSyncCenterService;
    private readonly IOrderUploadExecutionService _orderUploadExecutionService;
    private readonly ILocalizationService _localization;
    private readonly ICustomerDisplayOrchestrator _customerDisplayOrchestrator;
    private readonly IRawScannerService _rawScannerService;
    private readonly IUserFeedbackService _userFeedbackService;
    private readonly IReceiptQueryService _receiptQueryService;
    private readonly IReceiptPrintService _receiptPrintService;
    private readonly IReceiptPrinterSettingsStore? _receiptPrinterSettingsStore;
    private readonly IReceiptTextFormatter _receiptTextFormatter;
    private readonly IInstallmentOrderService _installmentOrderService;
    private readonly ISuspendedOrderService? _suspendedOrderService;
    private readonly IRemoteOrderHistoryService? _remoteOrderHistoryService;
    private readonly ICashPaymentWorkflowService _cashPaymentWorkflowService;
    private readonly IVoucherApiClient? _voucherApiClient;
    private readonly ICardTerminalClient? _cardTerminalClient;
    private readonly ICardTerminalSetupService? _cardTerminalSetupService;
    private readonly IDeviceRegistrationWorkflowService _deviceRegistrationWorkflowService;
    private readonly ISpecialProductsWorkflowService _specialProductsWorkflowService;
    private readonly IReceiptReturnsWorkflowService _receiptReturnsWorkflowService;
    private readonly IDailyCloseService _dailyCloseService;
    private readonly IDailyClosePrintService _dailyClosePrintService;
    private readonly ICashDrawerService _cashDrawerService;
    private readonly IApplicationExitService _applicationExitService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ITestSalesDataResetService? _testSalesDataResetService;
    private readonly ILinklyTerminalDialogPresenter? _linklyTerminalDialogPresenter;
    private readonly ICardPaymentRecoveryService? _cardPaymentRecoveryService;
    private readonly ICardRecoveryResultDialogService? _cardRecoveryResultDialogService;
    private readonly ILinklyFallbackPromptCoordinator? _linklyFallbackPromptCoordinator;
    private readonly PosTerminalWorkflowFactory _posTerminalWorkflowFactory;
    private readonly MainChildViewModelFactory _mainChildViewModelFactory;
    private readonly ScreenNavigator _screenNavigator;
    private readonly IWindowOwnerProvider? _windowOwnerProvider;
    private readonly CustomerDisplayShellController _customerDisplayShellController;
    private readonly DeviceReregistrationCoordinator _deviceReregistrationCoordinator;
    private readonly CatalogStartupCoordinator _catalogStartupCoordinator;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _connectivityTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _catalogDownloadHideTimer = new();

    private bool _isApplyingCulture;
    private bool _isRefreshingConnectivity;
    private bool _isAutoOrderSyncRetrying;
    private bool _schemaReady;
    private LocalOrder? _lastCompletedOrder;
    private LocalDeviceCache? _pendingDeviceRegistrationCache;
    private Task? _deviceRegistrationStoreLoadTask;
    private Task? _posPostShowStartupTask;
    private AppStartupOptions? _startupOptions;
    private bool _disposed;

    private SyncOrchestrator? _syncOrchestrator;
    private CardRecoveryPresenter? _cardRecoveryPresenter;
    private readonly MainReceiptCoordinator _receiptCoordinator;

    [ObservableProperty]
    private PosSessionState _session = new("HB POS", DefaultTestStoreCode, "Main Branch", "Terminal 04", "C001", "Alice", false, 0);

    public object? CurrentScreen
    {
        get => _screenNavigator.CurrentScreen;
        set => _screenNavigator.SetCurrentScreen(value);
    }

    public PosTerminalViewModel? CachedPosTerminalScreen => _screenNavigator.CachedPosTerminalScreen;

    public PaymentViewModel? CachedCashPaymentScreen => _screenNavigator.CachedCashPaymentScreen;

    public SpecialProductsViewModel? CachedSpecialProductsScreen => _screenNavigator.CachedSpecialProductsScreen;

    [ObservableProperty]
    private string _selectedCultureName = LocalizationService.DefaultCultureName;

    [ObservableProperty]
    private string _onlineStateText = string.Empty;

    public string PendingSyncText
    {
        get => _syncOrchestrator?.PendingSyncText ?? string.Empty;
        set
        {
            if (_syncOrchestrator is not null)
            {
                _syncOrchestrator.PendingSyncText = value;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _currentTime = string.Empty;

    [ObservableProperty]
    private string _terminalInfo = string.Empty;

    [ObservableProperty]
    private string _storeInfo = string.Empty;

    [ObservableProperty]
    private string _cashierInfo = string.Empty;

    [ObservableProperty]
    private string _versionStatusText = string.Empty;

    public string OrderSyncStatusText
    {
        get => _syncOrchestrator?.OrderSyncStatusText ?? string.Empty;
        set
        {
            if (_syncOrchestrator is not null)
            {
                _syncOrchestrator.OrderSyncStatusText = value;
                OnPropertyChanged();
            }
        }
    }

    public string SyncCenterDetailTitle
    {
        get => _syncOrchestrator?.SyncCenterDetailTitle ?? string.Empty;
        set
        {
            if (_syncOrchestrator is not null)
            {
                _syncOrchestrator.SyncCenterDetailTitle = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastOrderSyncErrorText
    {
        get => _syncOrchestrator?.LastOrderSyncErrorText ?? string.Empty;
        set
        {
            if (_syncOrchestrator is not null)
            {
                _syncOrchestrator.LastOrderSyncErrorText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSyncCenterExpanded
    {
        get => _syncOrchestrator?.IsSyncCenterExpanded ?? false;
        set
        {
            if (_syncOrchestrator is not null)
            {
                _syncOrchestrator.IsSyncCenterExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    private bool _isDeviceReregistrationDialogOpen;

    public bool IsCardRecoveryResultDialogOpen
    {
        get => _cardRecoveryPresenter?.IsCardRecoveryResultDialogOpen ?? false;
        set
        {
            if (_cardRecoveryPresenter is not null)
            {
                _cardRecoveryPresenter.IsCardRecoveryResultDialogOpen = value;
                OnPropertyChanged();
            }
        }
    }

    public CardRecoveryResultDialogViewModel? CardRecoveryResultDialog
    {
        get => _cardRecoveryPresenter?.CardRecoveryResultDialog;
        set
        {
            if (_cardRecoveryPresenter is not null)
            {
                _cardRecoveryPresenter.CardRecoveryResultDialog = value;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    private bool _isCustomerDisplayOpen;

    [ObservableProperty]
    private CustomerDisplayWindowMode _customerDisplayWindowMode = CustomerDisplayWindowMode.Closed;

    public int PendingUploadCount
    {
        get => _syncOrchestrator?.PendingUploadCount ?? 0;
        set
        {
            if (_syncOrchestrator is not null)
            {
                _syncOrchestrator.PendingUploadCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int FailedUploadCount
    {
        get => _syncOrchestrator?.FailedUploadCount ?? 0;
        set
        {
            if (_syncOrchestrator is not null)
            {
                _syncOrchestrator.FailedUploadCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int SyncingOrderCount
    {
        get => _syncOrchestrator?.SyncingOrderCount ?? 0;
        set
        {
            if (_syncOrchestrator is not null)
            {
                _syncOrchestrator.SyncingOrderCount = value;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    private bool _isCatalogDownloadProgressVisible;

    [ObservableProperty]
    private double _catalogDownloadProgressValue;

    [ObservableProperty]
    private string _catalogDownloadProgressText = string.Empty;

    [ObservableProperty]
    private string _catalogDownloadProgressDetailText = string.Empty;

    [ObservableProperty]
    private bool _isCatalogDownloadProgressFailed;

    public bool IsOrderSyncRetrying
    {
        get => _syncOrchestrator?.IsOrderSyncRetrying ?? false;
        set
        {
            if (_syncOrchestrator is not null)
            {
                _syncOrchestrator.IsOrderSyncRetrying = value;
                OnPropertyChanged();
            }
        }
    }


    public MainViewModel(
        IPosCoreServices core,
        IPosInfrastructureFacade infra,
        IPaymentTerminalFacade paymentTerminal,
        IPrintFacade print,
        IShellCultureService shellCultureService,
        IShellCatalogService shellCatalogService,
        ILocalCatalogRepository catalogRepository,
        IRemoteLookupRefreshService remoteLookupRefresh,
        ISpecialProductService specialProductService,
        IMainShellStartupService mainShellStartupService,
        ILocalOrderRepository orderRepository,
        IShellSyncCenterService shellSyncCenterService,
        ILocalizationService localization,
        ICustomerDisplayOrchestrator customerDisplayOrchestrator,
        IReceiptQueryService receiptQueryService,
        ICashPaymentWorkflowService cashPaymentWorkflowService,
        IDeviceRegistrationWorkflowService deviceRegistrationWorkflowService,
        ISpecialProductsWorkflowService specialProductsWorkflowService,
        PosTerminalWorkflowFactory posTerminalWorkflowFactory,
        ISuspendedOrderService? suspendedOrderService = null,
        IRemoteOrderHistoryService? remoteOrderHistoryService = null,
        IReceiptReturnsWorkflowService? receiptReturnsWorkflowService = null,
        IOrderUploadExecutionService? orderUploadExecutionService = null,
        IDailyCloseService? dailyCloseService = null,
        IDailyClosePrintService? dailyClosePrintService = null,
        ICashDrawerService? cashDrawerService = null,
        IInstallmentOrderService? installmentOrderService = null,
        ITestSalesDataResetService? testSalesDataResetService = null,
        IWindowOwnerProvider? windowOwnerProvider = null)
    {
        _core = core;
        _infra = infra;
        _paymentTerminal = paymentTerminal;
        _print = print;
        _priceIndex = core.PriceIndex;
        _cart = core.Cart;
        _checkout = core.Checkout;
        _schema = core.Schema;
        _shellCultureService = shellCultureService;
        _shellCatalogService = shellCatalogService;
        _catalogRepository = catalogRepository;
        _remoteLookupRefresh = remoteLookupRefresh;
        _specialProductService = specialProductService;
        _connectivityApiClient = infra.ConnectivityApiClient;
        _mainShellStartupService = mainShellStartupService;
        _orderRepository = orderRepository;
        _shellSyncCenterService = shellSyncCenterService;
        _orderUploadExecutionService = orderUploadExecutionService ?? NoopOrderUploadExecutionService.Instance;
        _localization = localization;
        _customerDisplayOrchestrator = customerDisplayOrchestrator;
        _rawScannerService = infra.RawScannerService;
        _userFeedbackService = infra.UserFeedbackService ?? NoopUserFeedbackService.Instance;
        _receiptQueryService = receiptQueryService;
        _receiptPrintService = print.ReceiptPrintService ?? new NoopReceiptPrintService(_localization);
        _receiptPrinterSettingsStore = print.ReceiptPrinterSettingsStore;
        _receiptTextFormatter = print.ReceiptTextFormatter ?? new ReceiptTextFormatter();
        _installmentOrderService = installmentOrderService ?? NoopInstallmentOrderService.Instance;
        _suspendedOrderService = suspendedOrderService;
        _remoteOrderHistoryService = remoteOrderHistoryService;
        _cashPaymentWorkflowService = cashPaymentWorkflowService;
        _voucherApiClient = paymentTerminal.VoucherApiClient;
        _cardTerminalClient = paymentTerminal.CardTerminalClient;
        _cardTerminalSetupService = paymentTerminal.CardTerminalSetupService;
        _deviceRegistrationWorkflowService = deviceRegistrationWorkflowService;
        _specialProductsWorkflowService = specialProductsWorkflowService;
        _receiptReturnsWorkflowService = receiptReturnsWorkflowService ?? new ReceiptReturnsWorkflowService(
            _receiptQueryService,
            _orderRepository,
            _remoteOrderHistoryService,
            _priceIndex,
            _cart,
            _localization);
        _dailyCloseService = dailyCloseService ?? NoopDailyCloseService.Instance;
        _dailyClosePrintService = dailyClosePrintService ?? NoopDailyClosePrintService.Instance;
        _cashDrawerService = cashDrawerService ?? new NoopCashDrawerService(_localization);
        _applicationExitService = infra.ApplicationExitService ?? new WpfApplicationExitService();
        _confirmationDialogService = infra.ConfirmationDialogService ?? new WpfConfirmationDialogService();
        _testSalesDataResetService = testSalesDataResetService;
        _linklyTerminalDialogPresenter = paymentTerminal.LinklyTerminalDialogPresenter;
        _cardPaymentRecoveryService = paymentTerminal.CardPaymentRecoveryService;
        _cardRecoveryResultDialogService = paymentTerminal.CardRecoveryResultDialogService;
        _linklyFallbackPromptCoordinator = paymentTerminal.LinklyFallbackPromptCoordinator;
        _windowOwnerProvider = windowOwnerProvider;
        _posTerminalWorkflowFactory = posTerminalWorkflowFactory;
        _mainChildViewModelFactory = CreateMainChildViewModelFactory();

        _cardRecoveryPresenter = CreateCardRecoveryPresenter();

        _syncOrchestrator = CreateSyncOrchestrator();

        _screenNavigator = CreateScreenNavigator();

        _screenNavigator.PaymentSuccess = _mainChildViewModelFactory.CreatePaymentSuccessViewModel();
        PaymentSuccess.NewTransactionRequested += OnPaymentSuccessNewTransactionRequested;
        PaymentSuccess.PrintReceiptRequested += OnPaymentSuccessPrintReceiptRequested;

        _receiptCoordinator = new MainReceiptCoordinator(
            _receiptPrintService,
            _cashDrawerService,
            _localization,
            msg => StatusMessage = msg ?? string.Empty);

        _customerDisplayShellController = new CustomerDisplayShellController(
            _customerDisplayOrchestrator,
            _localization,
            () => CustomerDisplay,
            () => Session,
            () => _cart,
            mode => CustomerDisplayWindowMode = mode,
            () => CustomerDisplayWindowMode,
            msg => StatusMessage = msg ?? string.Empty);

        _deviceReregistrationCoordinator = new DeviceReregistrationCoordinator(
            _mainShellStartupService,
            _localization,
            _shellSyncCenterService,
            _cart,
            snapshot => ApplySyncCenterSnapshot(snapshot),
            msg => StatusMessage = msg ?? string.Empty,
            () => _startupOptions?.PreviewMode == true);

        _catalogStartupCoordinator = new CatalogStartupCoordinator(
            _shellCatalogService,
            _catalogRepository,
            _localization,
            msg => StatusMessage = msg ?? string.Empty);

        ShowPosCommand = _screenNavigator.ShowPosCommand;
        ShowCashPaymentCommand = _screenNavigator.ShowCashPaymentCommand;
        ShowReturnsCommand = _screenNavigator.ShowReturnsCommand;
        ShowPaymentSuccessCommand = _screenNavigator.ShowPaymentSuccessCommand;
        ShowHistoryCommand = _screenNavigator.ShowHistoryCommand;
        ShowDailyCloseCommand = _screenNavigator.ShowDailyCloseCommand;
        ShowCustomerDisplayCommand = _screenNavigator.ShowCustomerDisplayCommand;
        ShowSettingsCommand = _screenNavigator.ShowSettingsCommand;
        ToggleSyncCenterCommand = _syncOrchestrator.ToggleSyncCenterCommand;
        RetrySyncOrderCommand = _syncOrchestrator.RetrySyncOrderCommand;
        RetryAllSyncOrdersCommand = _syncOrchestrator.RetryAllSyncOrdersCommand;
        ToggleCustomerDisplayWindowCommand = new RelayCommand(ToggleCustomerDisplayWindow);
        CloseCustomerDisplayWindowCommand = new RelayCommand(() => _customerDisplayShellController.Close(CurrentOwner));
        ShowCustomerDisplayNormalCommand = new RelayCommand(() => _customerDisplayShellController.ShowNormal(CurrentOwner));
        ShowCustomerDisplayFullscreenCommand = new RelayCommand(() => _customerDisplayShellController.ShowFullscreen(CurrentOwner));
        ToggleCultureCommand = new AsyncRelayCommand(ToggleCultureAsync);
        ResetScannerBindingCommand = new AsyncRelayCommand(ResetScannerBindingAsync);
        CloseCardRecoveryResultDialogCommand = _cardRecoveryPresenter.CloseCardRecoveryResultDialogCommand;
        PrintRecoveredReceiptCommand = _cardRecoveryPresenter.PrintRecoveredReceiptCommand;

        _cart.CartChanged += OnCartChanged;
        _localization.CultureChanged += OnCultureChanged;
        _customerDisplayOrchestrator.Closed += OnCustomerDisplayClosed;
        _clockTimer.Tick += OnClockTimerTick;
        _connectivityTimer.Tick += OnConnectivityTimerTick;
        _catalogDownloadHideTimer.Tick += OnCatalogDownloadHideTimerTick;
        RefreshLocalizedShell(resetStatus: true);
    }

    private MainChildViewModelFactory CreateMainChildViewModelFactory() =>
        new(
            _deviceRegistrationWorkflowService,
            _receiptQueryService,
            _suspendedOrderService,
            _remoteOrderHistoryService,
            _receiptTextFormatter,
            _receiptPrinterSettingsStore,
            _installmentOrderService,
            _localization,
            _cardTerminalClient,
            _priceIndex,
            _cart,
            _catalogRepository,
            _specialProductService,
            _specialProductsWorkflowService,
            _receiptReturnsWorkflowService,
            _cashPaymentWorkflowService,
            _cardTerminalSetupService,
            _rawScannerService,
            _dailyCloseService,
            _dailyClosePrintService,
            _userFeedbackService,
            _receiptPrintService,
            _cardRecoveryResultDialogService);

    private CardRecoveryPresenter CreateCardRecoveryPresenter() =>
        new(
            _cardPaymentRecoveryService,
            _cardRecoveryResultDialogService,
            _receiptQueryService,
            _receiptPrinterSettingsStore,
            _receiptTextFormatter,
            _localization,
            _linklyFallbackPromptCoordinator,
            _mainChildViewModelFactory,
            _cart,
            setStatusMessage: msg => StatusMessage = msg ?? string.Empty,
            getOwner: () => CurrentOwner,
            navigateToPaymentOnDraft: () =>
            {
                _screenNavigator.PrepareCachedCashPaymentScreen();
                CashPayment?.PrepareForEntry(Session);
                CurrentScreen = CashPayment;
                return Task.CompletedTask;
            },
            getSession: () => Session,
            setSession: value => Session = value,
            onCardRecoveryOrderCompleted: order =>
            {
                _lastCompletedOrder = order;
                PaymentSuccess.LoadFromOrder(order);
                CurrentScreen = PaymentSuccess;
                PosTerminal?.RefreshCart();
                CashPayment?.RefreshCart();
            },
            onCardRecoveryDraftRestored: () =>
            {
                PosTerminal?.RefreshCart();
                CashPayment?.RefreshCart();
            },
            refreshPendingSyncAsync: () => RefreshPendingSyncAsync(),
            printReceiptAsync: (receipt, reason) => PrintReceiptAsync(receipt, reason),
            notifyShowCashPaymentCanExecuteChanged: () => ShowCashPaymentCommand!.NotifyCanExecuteChanged(),
            notifyPrintRecoveredReceiptCanExecuteChanged: () => PrintRecoveredReceiptCommand!.NotifyCanExecuteChanged(),
            notifyPropertyChanged: name => OnPropertyChanged(name));

    private SyncOrchestrator CreateSyncOrchestrator() =>
        new(
            _shellSyncCenterService,
            _orderUploadExecutionService,
            _localization,
            setStatusMessage: msg => StatusMessage = msg ?? string.Empty,
            onPendingSyncCountChanged: count => Session = Session with { PendingSyncCount = count },
            getPendingSyncCount: () => Session.PendingSyncCount,
            refreshShell: () => RefreshLocalizedShell(),
            notifyPropertyChanged: name => OnPropertyChanged(name));

    private ScreenNavigator CreateScreenNavigator() =>
        new(
            _mainChildViewModelFactory,
            _cart,
            _localization,
            _suspendedOrderService,
            _cardTerminalSetupService,
            _testSalesDataResetService,
            _confirmationDialogService,
            _customerDisplayOrchestrator,
            _linklyFallbackPromptCoordinator,
            SyncCatalogAndReloadAsync,
            ResetCatalogAndReloadAsync,
            BeginDeviceReregistrationAsync,
            () => _cardRecoveryPresenter!.RecoverActiveCardPaymentSessionFromPaymentAsync(),
            setScreen: OnScreenChanged,
            onPaymentCreated: vm =>
            {
                vm.PaymentCompleted += OnPaymentCompleted;
                vm.PropertyChanged += OnCashPaymentPropertyChanged;
            },
            onPaymentDisposed: vm =>
            {
                vm.PaymentCompleted -= OnPaymentCompleted;
                vm.PropertyChanged -= OnCashPaymentPropertyChanged;
            },
            printSelectedHistoryReceiptAsync: vm => PrintSelectedHistoryReceiptAsync(vm),
            setStatusMessage: msg => StatusMessage = msg,
            getLastCompletedOrder: () => _lastCompletedOrder,
            setLastCompletedOrder: value => _lastCompletedOrder = value);

    public PosTerminalViewModel? PosTerminal
    {
        get => _screenNavigator.PosTerminal;
        set => _screenNavigator.PosTerminal = value;
    }

    public SpecialProductsViewModel? SpecialProducts
    {
        get => _screenNavigator.SpecialProducts;
        set => _screenNavigator.SpecialProducts = value;
    }

    public PaymentViewModel? CashPayment
    {
        get => _screenNavigator.CashPayment;
        set => _screenNavigator.CashPayment = value;
    }

    public InstallmentCenterViewModel? InstallmentCenter
    {
        get => _screenNavigator.InstallmentCenter;
        set => _screenNavigator.InstallmentCenter = value;
    }

    public InstallmentCreateViewModel? InstallmentCreate
    {
        get => _screenNavigator.InstallmentCreate;
        set => _screenNavigator.InstallmentCreate = value;
    }

    public ReceiptReturnsViewModel? ReceiptReturns
    {
        get => _screenNavigator.ReceiptReturns;
        set => _screenNavigator.ReceiptReturns = value;
    }

    public PaymentSuccessViewModel PaymentSuccess
    {
        get => _screenNavigator.PaymentSuccess;
    }

    public TransactionHistoryViewModel? TransactionHistory
    {
        get => _screenNavigator.TransactionHistory;
        set => _screenNavigator.TransactionHistory = value;
    }

    public DailyCloseViewModel? DailyClose
    {
        get => _screenNavigator.DailyClose;
        set => _screenNavigator.DailyClose = value;
    }

    public CustomerDisplayViewModel CustomerDisplay => _screenNavigator.CustomerDisplay;

    private DeviceRegistrationViewModel? _deviceRegistration;

    public DeviceRegistrationViewModel? DeviceRegistration
    {
        get => _deviceRegistration;
        private set => SetProperty(ref _deviceRegistration, value);
    }

    public SettingsViewModel? Settings => _screenNavigator.Settings;

    public ILinklyTerminalDialogPresenter? LinklyTerminalDialog => _linklyTerminalDialogPresenter;

    public bool IsPosTerminalScreenActive => ReferenceEquals(CurrentScreen, CachedPosTerminalScreen);

    public bool IsCashPaymentScreenActive => ReferenceEquals(CurrentScreen, CachedCashPaymentScreen);

    public bool IsSpecialProductsScreenActive => ReferenceEquals(CurrentScreen, CachedSpecialProductsScreen);

    public bool IsFallbackScreenActive => CurrentScreen is not null &&
        !IsPosTerminalScreenActive &&
        !IsCashPaymentScreenActive &&
        !IsSpecialProductsScreenActive;

    public string ActivePageTitleText => GetActivePageTitleText();

    private static readonly ObservableCollection<SyncQueueListItem> _emptySyncOrders = [];

    public ObservableCollection<SyncQueueListItem> SyncCenterOrders => _syncOrchestrator?.SyncCenterOrders ?? _emptySyncOrders;

    public IRelayCommand ShowPosCommand { get; }

    public IRelayCommand CloseCardRecoveryResultDialogCommand { get; }

    public IAsyncRelayCommand PrintRecoveredReceiptCommand { get; }

    public IRelayCommand ShowCashPaymentCommand { get; }

    public IRelayCommand ShowReturnsCommand { get; }

    public IAsyncRelayCommand ShowPaymentSuccessCommand { get; }

    public IAsyncRelayCommand ShowHistoryCommand { get; }

    public IAsyncRelayCommand ShowDailyCloseCommand { get; }

    public IRelayCommand ShowCustomerDisplayCommand { get; }

    public IAsyncRelayCommand ShowSettingsCommand { get; }

    public IAsyncRelayCommand ToggleSyncCenterCommand { get; }

    public IAsyncRelayCommand<SyncQueueListItem?> RetrySyncOrderCommand { get; }

    public IAsyncRelayCommand RetryAllSyncOrdersCommand { get; }

    public IRelayCommand ToggleCustomerDisplayWindowCommand { get; }

    public IRelayCommand CloseCustomerDisplayWindowCommand { get; }

    public IRelayCommand ShowCustomerDisplayNormalCommand { get; }

    public IRelayCommand ShowCustomerDisplayFullscreenCommand { get; }

    public IAsyncRelayCommand ToggleCultureCommand { get; }

    public IAsyncRelayCommand ResetScannerBindingCommand { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _clockTimer.Stop();
        _connectivityTimer.Stop();
        _catalogDownloadHideTimer.Stop();
        _clockTimer.Tick -= OnClockTimerTick;
        _connectivityTimer.Tick -= OnConnectivityTimerTick;
        _catalogDownloadHideTimer.Tick -= OnCatalogDownloadHideTimerTick;

        _cart.CartChanged -= OnCartChanged;
        _localization.CultureChanged -= OnCultureChanged;
        _customerDisplayOrchestrator.Closed -= OnCustomerDisplayClosed;
        _cardRecoveryPresenter?.DetachDialogService();

        PaymentSuccess.NewTransactionRequested -= OnPaymentSuccessNewTransactionRequested;
        PaymentSuccess.PrintReceiptRequested -= OnPaymentSuccessPrintReceiptRequested;

        // 主壳销毁时统一释放当前缓存子页面，避免 singleton 服务事件继续持有旧页面。
        _screenNavigator.ClearScreens();
        CancelStartupCatalogIndexLoad();
    }

    public async Task InitializeAsync(AppStartupOptions startupOptions)
    {
        _startupOptions = startupOptions;
        await _schema.InitializeAsync();
        _schemaReady = true;

        await RestoreLanguageAsync(startupOptions);
        var startupResult = await _mainShellStartupService.EvaluateAsync(Session, startupOptions.PreviewMode);
        Session = startupResult.Session;
        if (startupResult.RequiresDeviceRegistration)
        {
            DeviceRegistration = CreateDeviceRegistrationViewModel(startupOptions);
            _pendingDeviceRegistrationCache = startupResult.CachedDevice;
            DeviceRegistration.Prepare(startupResult.CachedDevice);
            CurrentScreen = DeviceRegistration;
            RefreshClock();
            _clockTimer.Start();
            return;
        }

        await InitializePosExperienceAsync(startupOptions);
    }

    public Task ContinueStartupAfterShownAsync(AppStartupOptions startupOptions, Window? owner = null)
    {
        if (startupOptions.PreviewMode)
        {
            return Task.CompletedTask;
        }

        if (DeviceRegistration is not null && CurrentScreen == DeviceRegistration)
        {
            _deviceRegistrationStoreLoadTask ??= DeviceRegistration.LoadStoresAsync(_pendingDeviceRegistrationCache);
            return _deviceRegistrationStoreLoadTask;
        }

        return ContinuePosStartupAfterShownAsync(startupOptions, owner);
    }

    public bool TryProcessKeyboardScannerInput(string barcode)
    {
        if (CurrentScreen is IScannerInputTarget scannerInputTarget)
        {
            return scannerInputTarget.ProcessScannerBarcode(
                barcode,
                "keyboard-focus-fallback",
                "keyboard-fallback");
        }

        ConsoleLog.Write(
            "RawScanner",
            $"keyboard fallback scan ignored because active screen cannot handle scanner screen={CurrentScreen?.GetType().Name ?? "<none>"} barcode={barcode}");
        return true;
    }

    private async Task ActivateDeviceAsync(DeviceActivatedEventArgs args, AppStartupOptions startupOptions)
    {
        _deviceRegistrationStoreLoadTask = null;
        _posPostShowStartupTask = null;
        if (DeviceRegistration is not null)
        {
            DeviceRegistration.IsBusy = true;
            DeviceRegistration.StatusMessage = _localization.T("startup.stage.loadingProducts");
        }

        Session = Session with
        {
            StoreCode = args.StoreCode,
            StoreName = args.StoreName,
            DeviceCode = args.DeviceCode
        };
        if (!string.IsNullOrWhiteSpace(args.AuthorizationCode))
        {
            _mainShellStartupService.SetAuthorizedDevice(
                args.DeviceCode,
                args.StoreCode,
                args.HardwareId,
                args.AuthorizationCode);
        }

        await InitializePosExperienceAsync(startupOptions);
        IsDeviceReregistrationDialogOpen = false;
        DeviceRegistration = null;
        _ = ContinuePosStartupAfterShownAsync(startupOptions, CurrentOwner);
    }

    private async Task InitializePosExperienceAsync(AppStartupOptions startupOptions)
    {
        _screenNavigator.ClearScreens();
        _screenNavigator.SetCachedPosTerminalScreen(null);
        _screenNavigator.SetCachedSpecialProductsScreen(null);
        CancelStartupCatalogIndexLoad();
        IReadOnlyList<SellableItemDto> cachedItems = [];
        if (startupOptions.PreviewMode)
        {
            cachedItems = CreateStarterItems();
            await _shellCatalogService.ReplacePreviewCatalogAsync(cachedItems);
        }

        var posWorkflowService = _posTerminalWorkflowFactory(RefreshRemoteLookupAsync, ReloadCatalogIndexAsync);
        PosTerminal = _mainChildViewModelFactory.CreatePosTerminalViewModel(
            Session,
            _screenNavigator.ShowCashPayment,
            _screenNavigator.ShowSpecialProductsAsync,
            onHoldOrderAsync: _screenNavigator.SuspendCurrentOrderAsync,
            onRecallOrderAsync: _screenNavigator.ShowSuspendedHistoryAsync,
            onOpenHistoryAsync: _screenNavigator.ShowHistoryAsync,
            onOpenDailyCloseAsync: _screenNavigator.ShowDailyCloseAsync,
            onOpenSettingsAsync: _screenNavigator.ShowSettingsAsync,
            onOpenCustomerDisplay: _screenNavigator.ShowCustomerDisplay,
            syncCatalogAsync: SyncCatalogAndReloadAsync,
            resetCatalogAsync: ResetCatalogAndReloadAsync,
            refreshOnlineAsync: RefreshOnlineStateAsync,
            onReregisterDeviceAsync: BeginDeviceReregistrationFromPosAsync,
            workflowService: posWorkflowService,
            onOpenReturns: _screenNavigator.ShowReturns,
            onPrintLastReceiptAsync: PrintLatestReceiptAsync,
            onOpenCashDrawerAsync: OpenCashDrawerAsync,
            onExitApplicationAsync: ExitApplicationAsync);
        SpecialProducts = _mainChildViewModelFactory.CreateSpecialProductsViewModel(
            Session,
            _screenNavigator.ShowPos,
            line => PosTerminal?.RevealCartLine(line));
        _screenNavigator.SetCachedPosTerminalScreen(PosTerminal);
        ReceiptReturns = _mainChildViewModelFactory.CreateReceiptReturnsViewModel(
            Session,
            _screenNavigator.ShowPos,
            line => PosTerminal?.RevealCartLine(line));
        if (cachedItems.Count > 0)
        {
            PosTerminal.LoadMatches(cachedItems);
        }

        TransactionHistory = _screenNavigator.CreateTransactionHistoryViewModel();

        if (startupOptions.PreviewMode)
        {
            AddPreviewCartItems(cachedItems);
            _lastCompletedOrder = await CreatePreviewOrderAsync(cachedItems);
        }

        await RefreshPendingSyncAsync();
        RefreshClock();
        _clockTimer.Start();
        _screenNavigator.ApplySessionToScreens();
        _screenNavigator.PrepareCachedCashPaymentScreen();
        // 诊断启动卡顿时先关闭客显预热，避免启动阶段创建隐藏窗口。
        ConsoleLog.Write(
            "CustomerDisplay",
            $"startup prewarm skipped store={Session.StoreCode} device={Session.DeviceCode} reason=auto-open-disabled");
        await BeginStartupCatalogIndexLoadAsync(startupOptions);
        await PreloadStartupSpecialProductsDataAsync(startupOptions);
        _screenNavigator.NavigateFromStartup(startupOptions.InitialScreen);
    }

    private Task ContinuePosStartupAfterShownAsync(AppStartupOptions startupOptions, Window? owner)
    {
        if (startupOptions.PreviewMode)
        {
            return Task.CompletedTask;
        }

        _posPostShowStartupTask ??= ContinuePosStartupAfterShownCoreAsync(owner);
        return _posPostShowStartupTask;
    }

    private async Task ContinuePosStartupAfterShownCoreAsync(Window? owner)
    {
        var stopwatch = Stopwatch.StartNew();
        ConsoleLog.Write(
            "CustomerDisplay",
            $"post-show open start store={Session.StoreCode} device={Session.DeviceCode} ownerPresent={owner is not null}");
        try
        {
            // 诊断启动卡顿时关闭客显自动打开；手动客显按钮仍可正常打开。
            ConsoleLog.Write(
                "CustomerDisplay",
                $"post-show open skipped store={Session.StoreCode} device={Session.DeviceCode} ownerPresent={owner is not null} reason=auto-open-disabled");
            stopwatch.Stop();
            ConsoleLog.Write(
                "CustomerDisplay",
                $"post-show open completed store={Session.StoreCode} device={Session.DeviceCode} displayMode={CustomerDisplayWindowMode} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ConsoleLog.Write(
                "CustomerDisplay",
                $"post-show open failed store={Session.StoreCode} device={Session.DeviceCode} elapsedMs={stopwatch.ElapsedMilliseconds} error={ex.Message}");
            throw;
        }

        ConsoleLog.Write(
            "SpecialProducts",
            $"startup home preload skipped store={Session.StoreCode} reason=moved-before-main-window");
        await RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft: false);
        await RefreshOnlineStateAsync(CancellationToken.None, autoRetryOrders: true);
        _connectivityTimer.Start();
        BeginInitialCatalogSync();
    }

    private async Task<IReadOnlyList<SellableItemDto>> LoadStartupCatalogIndexAsync(CancellationToken cancellationToken)
    {
        return await _catalogStartupCoordinator.LoadLocalCatalogForStartupAsync(
            Session.StoreCode,
            cancellationToken,
            items => PosTerminal?.LoadMatches(items),
            () => { PosTerminal?.RefreshCart(); CashPayment?.RefreshCart(); });
    }

    private async Task<IReadOnlyList<SellableItemDto>> BeginStartupCatalogIndexLoadAsync(AppStartupOptions startupOptions)
        => await _catalogStartupCoordinator.LoadStartupCatalogIndexAsync(
            Session.StoreCode,
            startupOptions.PreviewMode,
            items => PosTerminal?.LoadMatches(items),
            () => { PosTerminal?.RefreshCart(); CashPayment?.RefreshCart(); });

    private void CancelStartupCatalogIndexLoad() => _catalogStartupCoordinator.CancelStartupLoad();

    partial void OnSessionChanged(PosSessionState value)
    {
        _screenNavigator.Session = value;
        RefreshLocalizedShell();
        _screenNavigator.ApplySessionToScreens();
    }

    /// <summary>
    /// 屏幕切换的统一入口：通知绑定刷新 CurrentScreen、缓存屏幕、active state、页面标题和扫码 active page。
    /// 集中在此方法内，避免多处分散通知导致的绑定遗漏（如"顶部显示收银但内容为空"）。
    /// </summary>
    private void OnScreenChanged(object? screen)
    {
        OnPropertyChanged(nameof(CurrentScreen));
        if (!ReferenceEquals(screen, _screenNavigator!.ReceiptReturns))
        {
            _screenNavigator.ReceiptReturns?.ResetToDefault();
        }

        RaiseScreenHostStateChanged();
        _rawScannerService.SetActivePage((screen as IScannerInputTarget)?.ScannerPageId);
    }

    private void RaiseScreenHostStateChanged()
    {
        // 缓存屏幕在 ScreenNavigator 内部创建，切换页面时必须通知宿主重新读取内容绑定。
        OnPropertyChanged(nameof(CachedPosTerminalScreen));
        OnPropertyChanged(nameof(CachedCashPaymentScreen));
        OnPropertyChanged(nameof(CachedSpecialProductsScreen));
        OnPropertyChanged(nameof(IsPosTerminalScreenActive));
        OnPropertyChanged(nameof(IsCashPaymentScreenActive));
        OnPropertyChanged(nameof(IsSpecialProductsScreenActive));
        OnPropertyChanged(nameof(IsFallbackScreenActive));
        OnPropertyChanged(nameof(ActivePageTitleText));
    }

    partial void OnCustomerDisplayWindowModeChanged(CustomerDisplayWindowMode value)
    {
        IsCustomerDisplayOpen = value != CustomerDisplayWindowMode.Closed;
    }

    partial void OnSelectedCultureNameChanged(string value)
    {
        if (_isApplyingCulture || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = ApplyLanguageAsync(value, persist: true);
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedShell();
        OnPropertyChanged(nameof(SelectedCultureName));
    }

    private async Task RestoreLanguageAsync(AppStartupOptions startupOptions)
    {
        ApplySelectedCultureName(await _shellCultureService.RestoreAsync(startupOptions, _schemaReady));
    }

    private async Task ApplyLanguageAsync(string cultureName, bool persist)
    {
        ApplySelectedCultureName(await _shellCultureService.ApplyAsync(cultureName, persist, _schemaReady));
    }

    private async Task ToggleCultureAsync()
    {
        ApplySelectedCultureName(await _shellCultureService.ToggleAsync(_schemaReady));
    }

    private void RefreshLocalizedShell(bool resetStatus = false)
    {
        OnlineStateText = _localization.T(Session.IsOnline ? "Online" : "Offline");
        _syncOrchestrator?.RefreshLocalizedText();
        TerminalInfo = string.Format(_localization.CurrentCulture, _localization.T("shell.footer.terminalInfo"), Session.DeviceCode);
        StoreInfo = string.Format(_localization.CurrentCulture, _localization.T("shell.top.storeInfo"), Session.StoreName, Session.StoreCode);
        CashierInfo = string.Format(_localization.CurrentCulture, _localization.T("shell.top.cashierInfo"), Session.CashierName);
        VersionStatusText = _localization.T("shell.footer.versionReady");
        OnPropertyChanged(nameof(ActivePageTitleText));
        if (resetStatus || string.IsNullOrWhiteSpace(StatusMessage))
        {
            StatusMessage = _localization.T("StatusOfflineReady");
        }
    }

    private string GetActivePageTitleText()
    {
        if (ReferenceEquals(CurrentScreen, InstallmentCenter))
        {
            return InstallmentCenter?.PageTitleText ?? "分期中心";
        }

        if (ReferenceEquals(CurrentScreen, InstallmentCreate))
        {
            return InstallmentCreate?.PageTitleText ?? "创建分期";
        }

        return _localization.T(GetActivePageTitleKey());
    }

    private string GetActivePageTitleKey()
    {
        if (ReferenceEquals(CurrentScreen, PosTerminal))
        {
            return "shell.page.pos";
        }

        if (ReferenceEquals(CurrentScreen, CashPayment))
        {
            return CashPayment?.PaymentMode switch
            {
                PaymentEntryMode.Refund => "shell.page.refund",
                PaymentEntryMode.ZeroSettlement => "shell.page.zeroSettlement",
                _ => "shell.page.payment"
            };
        }

        if (ReferenceEquals(CurrentScreen, SpecialProducts))
        {
            return "shell.page.specialProducts";
        }

        if (ReferenceEquals(CurrentScreen, ReceiptReturns))
        {
            return "shell.page.returns";
        }

        if (ReferenceEquals(CurrentScreen, PaymentSuccess))
        {
            return "shell.page.paymentSuccess";
        }

        if (ReferenceEquals(CurrentScreen, TransactionHistory))
        {
            return "shell.page.history";
        }

        if (ReferenceEquals(CurrentScreen, DailyClose))
        {
            return "shell.page.dailyClose";
        }

        if (ReferenceEquals(CurrentScreen, Settings))
        {
            return "shell.page.settings";
        }

        if (ReferenceEquals(CurrentScreen, CustomerDisplay))
        {
            return "shell.page.customerDisplay";
        }

        if (ReferenceEquals(CurrentScreen, DeviceRegistration))
        {
            return "shell.page.deviceRegistration";
        }

        return "shell.page.loading";
    }

    private async Task RefreshPendingSyncAsync()
    {
        if (_syncOrchestrator is not null)
        {
            await _syncOrchestrator.RefreshPendingSyncAsync();
        }
    }


    private DeviceRegistrationViewModel CreateDeviceRegistrationViewModel(AppStartupOptions startupOptions)
    {
        return _mainChildViewModelFactory.CreateDeviceRegistrationViewModel(
            args => ActivateDeviceAsync(args, startupOptions),
            ApplyDeviceReregistered,
            CancelDeviceReregistration);
    }

    private void ApplySelectedCultureName(string cultureName)
    {
        _isApplyingCulture = true;
        SelectedCultureName = cultureName;
        _isApplyingCulture = false;
    }

    private void ApplySyncCenterSnapshot(ShellSyncCenterSnapshot snapshot)
    {
        _syncOrchestrator?.ApplySyncCenterSnapshot(snapshot);
    }

    private void RefreshClock()
    {
        CurrentTime = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void BeginInitialCatalogSync()
    {
        if (!Session.IsOnline)
        {
            return;
        }

        _ = TryInitialCatalogSyncAsync();
    }

    private void BeginSpecialProductsHomePreload()
    {
        if (SpecialProducts is null)
        {
            return;
        }

        _screenNavigator.PrepareCachedSpecialProductsScreen();
        _ = TryPreloadSpecialProductsHomeAsync();
    }

    private async Task PreloadStartupSpecialProductsDataAsync(AppStartupOptions startupOptions)
    {
        if (startupOptions.PreviewMode || SpecialProducts is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        ConsoleLog.Write("SpecialProducts", $"startup data preload start store={Session.StoreCode}");
        try
        {
            _screenNavigator.PrepareCachedSpecialProductsScreen();
            // 启动阶段只预加载特殊商品数据，图片缩略图留给页面进入后异步加载。
            await SpecialProducts.PreloadAsync(CancellationToken.None);
            stopwatch.Stop();
            ConsoleLog.Write(
                "SpecialProducts",
                $"startup data preload completed store={Session.StoreCode} items={SpecialProducts.SpecialItems.Count} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ConsoleLog.Write(
                "SpecialProducts",
                $"startup data preload failed store={Session.StoreCode} elapsedMs={stopwatch.ElapsedMilliseconds} error={ex.Message}");
        }
    }

    private async Task TryPreloadSpecialProductsHomeAsync()
    {
        try
        {
            if (SpecialProducts is not null)
            {
                await SpecialProducts.PreloadFirstPageThumbnailsAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            ConsoleLog.Write("SpecialProducts", $"startup home preload failed store={Session.StoreCode} error={ex.Message}");
        }
    }

    private async Task<bool> RefreshOnlineStateAsync(CancellationToken cancellationToken)
    {
        return await RefreshOnlineStateAsync(cancellationToken, autoRetryOrders: false);
    }

    private async Task<bool> RefreshOnlineStateAsync(CancellationToken cancellationToken, bool autoRetryOrders)
    {
        if (_isRefreshingConnectivity)
        {
            return Session.IsOnline;
        }

        _isRefreshingConnectivity = true;
        try
        {
            var isOnline = await _connectivityApiClient.CheckOnlineAsync(cancellationToken);
            if (Session.IsOnline != isOnline)
            {
                Session = Session with { IsOnline = isOnline };
            }

            if (isOnline && autoRetryOrders)
            {
                await TryAutoRetryPendingOrdersAsync(cancellationToken);
            }

            return isOnline;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // API 探测失败只代表当前离线，不能阻断已授权设备继续使用本地缓存收银。
            ConsoleLog.Write("Connectivity", $"online check failed; fallback offline error={ex.Message}");
            if (Session.IsOnline)
            {
                Session = Session with { IsOnline = false };
            }

            return false;
        }
        finally
        {
            _isRefreshingConnectivity = false;
        }
    }

    private async Task TryInitialCatalogSyncAsync()
    {
        var hasLocalCatalogItems = await HasLocalCatalogItemsAsync();
        var syncMode = hasLocalCatalogItems ? "background-refresh" : "initial-full-download";
        try
        {
            // 启动后的商品同步已在 POS 显示之后执行，不能再用启动超时截断，避免半包缓存反复无法补全。
            ConsoleLog.Write(
                "CatalogSync",
                $"initial sync mode={syncMode} localItemCount={(hasLocalCatalogItems ? ">0" : "0")} timeoutSeconds=none");
            var cachedItems = await SyncCatalogAndReloadAsync(CancellationToken.None);
            PosTerminal?.LoadMatches(cachedItems);
            PosTerminal?.RefreshCart();
            CashPayment?.RefreshCart();
        }
        catch (OperationCanceledException)
        {
            ConsoleLog.Write("CatalogSync", $"initial sync canceled mode={syncMode} timeoutSeconds=none");
            StatusMessage = hasLocalCatalogItems
                ? _localization.T("main.catalogSync.timedOut")
                : _localization.T("main.catalogSync.initialDownloadTimedOut");
        }
        catch (Exception ex)
        {
            var statusKey = hasLocalCatalogItems
                ? "main.catalogSync.failed"
                : "main.catalogSync.initialDownloadFailed";
            StatusMessage = string.Format(_localization.CurrentCulture, _localization.T(statusKey), ex.Message);
        }
    }

    private async Task<bool> HasLocalCatalogItemsAsync()
    {
        try
        {
            var firstPage = await _catalogRepository.LoadSellableItemComparePageAsync(
                Session.StoreCode,
                afterLookupCodeNormalized: null,
                pageSize: 1);
            return firstPage.Count > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 本地探测失败只影响提示文案，不阻止后台同步继续尝试。
            ConsoleLog.Write("CatalogSync", $"initial sync local cache probe failed store={Session.StoreCode} error={ex.Message}");
            return true;
        }
    }

    private async Task TryAutoRetryPendingOrdersAsync(CancellationToken cancellationToken)
    {
        if (_isAutoOrderSyncRetrying || IsOrderSyncRetrying)
        {
            return;
        }

        _isAutoOrderSyncRetrying = true;
        try
        {
            var snapshot = await _shellSyncCenterService.GetSnapshotAsync();
            ApplySyncCenterSnapshot(snapshot);
            if (snapshot.Overview.PendingCount + snapshot.Overview.FailedCount == 0)
            {
                return;
            }

            ConsoleLog.Write("OrderSync", "auto retry pending start");
            var result = await _orderUploadExecutionService.ExecutePendingAsync(cancellationToken: cancellationToken);
            await RefreshPendingSyncAsync();
            ConsoleLog.Write(
                "OrderSync",
                $"auto retry pending completed attempted={result.AttemptedCount} uploaded={result.UploadedCount} failed={result.FailedCount}");
        }
        catch (OperationCanceledException)
        {
            ConsoleLog.Write("OrderSync", "auto retry pending canceled");
            throw;
        }
        catch (Exception ex)
        {
            ConsoleLog.Write("OrderSync", $"auto retry pending failed error={ex.GetType().Name} message={ex.Message}");
            try
            {
                await RefreshPendingSyncAsync();
            }
            catch (Exception refreshEx) when (refreshEx is not OperationCanceledException)
            {
                ConsoleLog.Write(
                    "OrderSync",
                    $"auto retry pending refresh failed error={refreshEx.GetType().Name} message={refreshEx.Message}");
            }
        }
        finally
        {
            _isAutoOrderSyncRetrying = false;
        }
    }

    private Task<RemoteLookupRefreshResult> RefreshRemoteLookupAsync(
        string storeCode,
        string lookupCode,
        CancellationToken cancellationToken)
    {
        return _remoteLookupRefresh.RefreshLookupAsync(storeCode, lookupCode, cancellationToken);
    }

    private Task<IReadOnlyList<SellableItemDto>> SyncCatalogAndReloadAsync(CancellationToken cancellationToken)
    {
        return SyncCatalogAndReloadAsync(forceFullDownload: false, cancellationToken);
    }

    private Task<IReadOnlyList<SellableItemDto>> ResetCatalogAndReloadAsync(CancellationToken cancellationToken)
    {
        return SyncCatalogAndReloadAsync(forceFullDownload: true, cancellationToken);
    }

    private async Task<IReadOnlyList<SellableItemDto>> SyncCatalogAndReloadAsync(
        bool forceFullDownload,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<CatalogSyncProgress>(ApplyCatalogDownloadProgress);
        return await _shellCatalogService.SyncCatalogAndReloadAsync(
            Session.StoreCode,
            forceFullDownload,
            progress,
            cancellationToken);
    }

    private void ApplyCatalogDownloadProgress(CatalogSyncProgress progress)
    {
        _catalogDownloadHideTimer.Stop();
        IsCatalogDownloadProgressVisible = true;
        IsCatalogDownloadProgressFailed = progress.Stage == CatalogSyncProgressStage.Failed;
        CatalogDownloadProgressValue = progress.Percent;

        if (progress.Stage == CatalogSyncProgressStage.Failed)
        {
            CatalogDownloadProgressText = string.Format(
                _localization.CurrentCulture,
                _localization.T("shell.catalogDownload.failed"),
                progress.Percent);
            CatalogDownloadProgressDetailText = progress.ErrorMessage ?? string.Empty;
            StartCatalogDownloadHideTimer(TimeSpan.FromSeconds(15));
            return;
        }

        var titleKey = progress.Stage == CatalogSyncProgressStage.Completed
            ? "shell.catalogDownload.completed"
            : "shell.catalogDownload.downloading";
        CatalogDownloadProgressText = string.Format(
            _localization.CurrentCulture,
            _localization.T(titleKey),
            progress.Percent);
        CatalogDownloadProgressDetailText = string.Format(
            _localization.CurrentCulture,
            _localization.T("shell.catalogDownload.detail"),
            progress.DownloadedCount,
            progress.TotalCount,
            progress.RemotePages,
            progress.UpsertedCount,
            progress.DeletedCount,
            FormatElapsed(progress.ElapsedMilliseconds));

        if (progress.Stage == CatalogSyncProgressStage.Completed)
        {
            StartCatalogDownloadHideTimer(TimeSpan.FromSeconds(5));
        }
    }

    private void StartCatalogDownloadHideTimer(TimeSpan interval)
    {
        _catalogDownloadHideTimer.Interval = interval;
        _catalogDownloadHideTimer.Start();
    }

    private string FormatElapsed(long elapsedMilliseconds)
    {
        return string.Format(
            _localization.CurrentCulture,
            _localization.T("shell.catalogDownload.elapsedSeconds"),
            elapsedMilliseconds / 1000d);
    }

    private async Task<IReadOnlyList<SellableItemDto>> ReloadCatalogIndexAsync(CancellationToken cancellationToken = default)
    {
        return await ReloadCatalogIndexAsync(Session.StoreCode, cancellationToken);
    }

    private async Task<IReadOnlyList<SellableItemDto>> ReloadCatalogIndexAsync(string storeCode, CancellationToken cancellationToken)
    {
        return await _shellCatalogService.LoadLocalCatalogAsync(storeCode, cancellationToken);
    }

    private void PrewarmCustomerDisplay() => _customerDisplayShellController.Prewarm();

    private async Task BeginDeviceReregistrationFromPosAsync()
    {
        await BeginDeviceReregistrationAsync();
    }

    private async Task<DeviceReregistrationStartResult> BeginDeviceReregistrationAsync()
    {
        var blocked = await _deviceReregistrationCoordinator.CheckCanBeginAsync();
        if (blocked is not null)
        {
            return blocked;
        }

        var startupOptions = _startupOptions ?? new AppStartupOptions([], false, null, null);
        DeviceRegistration = CreateDeviceRegistrationViewModel(startupOptions);
        _pendingDeviceRegistrationCache = null;
        _deviceRegistrationStoreLoadTask = null;
        _screenNavigator.ClearCashPaymentCache();
        DeviceRegistration.PrepareReregister(Session.StoreCode);
        IsDeviceReregistrationDialogOpen = true;
        // 弹窗打开后立即加载可切换分店，避免用户首次看到空列表。
        _deviceRegistrationStoreLoadTask = DeviceRegistration.LoadStoresAsync(null);
        await _deviceRegistrationStoreLoadTask;
        if (DeviceRegistration is null || !IsDeviceReregistrationDialogOpen)
        {
            // 用户可能在门店加载完成前已经取消，后台加载结束后不再恢复或读取弹窗状态。
            return DeviceReregistrationStartResult.Blocked(StatusMessage);
        }

        return DeviceReregistrationStartResult.StartedWith(DeviceRegistration.StatusMessage);
    }

    private void ApplyDeviceReregistered()
    {
        _deviceReregistrationCoordinator.ClearAuthorization();
        _posPostShowStartupTask = null;
        CancelStartupCatalogIndexLoad();
        _screenNavigator.ClearScreens();
        _screenNavigator.SetCachedPosTerminalScreen(null);
        _screenNavigator.SetCachedSpecialProductsScreen(null);
        _screenNavigator.PosTerminal = null;
        _screenNavigator.SpecialProducts = null;
        _screenNavigator.InstallmentCenter = null;
        _screenNavigator.InstallmentCreate = null;
        _screenNavigator.ReceiptReturns = null;
        _screenNavigator.TransactionHistory = null;
        _lastCompletedOrder = null;
        _cart.Clear();
        SetCustomerDisplayWindowMode(CustomerDisplayWindowMode.Closed, CurrentOwner);
        StatusMessage = _deviceReregistrationCoordinator.SubmittedStatusMessage;
    }

    private void CancelDeviceReregistration()
    {
        var wasCurrentScreen = ReferenceEquals(CurrentScreen, DeviceRegistration);
        if (PosTerminal is null)
        {
            IsDeviceReregistrationDialogOpen = false;
            DeviceRegistration = null;
            return;
        }

        DeviceRegistration = null;
        IsDeviceReregistrationDialogOpen = false;
        _deviceRegistrationStoreLoadTask = null;
        if (wasCurrentScreen)
        {
            _screenNavigator.ShowPos();
        }
        StatusMessage = _deviceReregistrationCoordinator.CancelStatusMessage;
    }

    private void OnCartChanged(object? sender, EventArgs e)
    {
        _screenNavigator.RefreshCartRelatedState();
        _screenNavigator.LoadCustomerDisplayFromCart();
    }

    private Task<bool> RecoverCardPaymentAttemptAsync(bool navigateToPaymentOnDraft) =>
        _cardRecoveryPresenter?.RecoverCardPaymentAttemptAsync(navigateToPaymentOnDraft) ?? Task.FromResult(false);

    private Window? CurrentOwner => _windowOwnerProvider?.CurrentOwner;

    private void OnPaymentSuccessNewTransactionRequested(object? sender, EventArgs e)
    {
        _screenNavigator.ResetForNewTransaction();
    }

    private async void OnPaymentSuccessPrintReceiptRequested(object? sender, EventArgs e)
    {
        await PrintPaymentSuccessReceiptAsync();
    }

    private void OnCustomerDisplayClosed(object? sender, EventArgs e)
    {
        CustomerDisplayWindowMode = CustomerDisplayWindowMode.Closed;
    }

    private void OnClockTimerTick(object? sender, EventArgs e)
    {
        RefreshClock();
    }

    private async void OnConnectivityTimerTick(object? sender, EventArgs e)
    {
        await RefreshOnlineStateAsync(CancellationToken.None, autoRetryOrders: true);
    }

    private void OnCatalogDownloadHideTimerTick(object? sender, EventArgs e)
    {
        _catalogDownloadHideTimer.Stop();
        IsCatalogDownloadProgressVisible = false;
    }

    private async Task ShowInstallmentCreateAsync(PosCartServiceSnapshot? cartSnapshot) =>
        await _screenNavigator.ShowInstallmentCreateAsync(cartSnapshot);

    private void OnCashPaymentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaymentViewModel.PaymentMode) &&
            ReferenceEquals(CurrentScreen, CashPayment))
        {
            OnPropertyChanged(nameof(ActivePageTitleText));
        }
    }

    private async void OnPaymentCompleted(object? sender, PaymentCompletedEventArgs e)
    {
        _lastCompletedOrder = e.Order;
        await RefreshPendingSyncAsync();
        await PaymentSuccess.LoadFromOrderAsync(e.Order);
        CurrentScreen = PaymentSuccess;

        ShowCashPaymentCommand.NotifyCanExecuteChanged();
        if (MainReceiptCoordinator.ContainsCashPayment(e.Order))
        {
            var cashDrawerResult = await OpenCashDrawerAsync();
            if (!cashDrawerResult.Succeeded)
            {
                StatusMessage = cashDrawerResult.Message;
            }
        }

        if (MainReceiptCoordinator.ContainsCardPayment(e.Order))
        {
            await _receiptCoordinator.PrintReceiptAsync(ReceiptQueryService.CreateReceipt(e.Order), ReceiptPrintReason.CardAuto);
        }
    }

    private async Task<ReceiptPrintResult> PrintLatestReceiptAsync() =>
        await _receiptCoordinator.PrintLatestAsync();

    private async Task<ReceiptPrintResult> OpenCashDrawerAsync() =>
        await _receiptCoordinator.OpenCashDrawerAsync();

    private Task ExitApplicationAsync()
    {
        if (!_confirmationDialogService.ConfirmExitApplication())
        {
            return Task.CompletedTask;
        }

        SetCustomerDisplayWindowMode(CustomerDisplayWindowMode.Closed, CurrentOwner);
        _applicationExitService.Exit();
        return Task.CompletedTask;
    }

    private async Task PrintPaymentSuccessReceiptAsync()
    {
        if (PaymentSuccess.TransactionId is Guid orderGuid)
        {
            await _receiptCoordinator.PrintSuccessAsync(orderGuid);
        }
    }

    private async Task PrintSelectedHistoryReceiptAsync(TransactionHistoryViewModel history)
    {
        if (history.SelectedOrder is not null)
        {
            await _receiptCoordinator.PrintHistoryAsync(history.SelectedOrder.OrderGuid);
        }
    }

    private async Task<ReceiptPrintResult> PrintReceiptAsync(Guid orderGuid, ReceiptPrintReason reason) =>
        await _receiptCoordinator.PrintReceiptAsync(orderGuid, reason);

    private async Task<ReceiptPrintResult> PrintReceiptAsync(ReceiptDetails receipt, ReceiptPrintReason reason) =>
        await _receiptCoordinator.PrintReceiptAsync(receipt, reason);

    private void ToggleCustomerDisplayWindow()
    {
        var owner = CurrentOwner;
        if (owner is null)
        {
            return;
        }

        _customerDisplayShellController.Toggle(owner);
    }

    public void ToggleCustomerDisplayWindow(Window? owner) => _customerDisplayShellController.Toggle(owner);

    public void SetCustomerDisplayWindowMode(CustomerDisplayWindowMode mode, Window? owner) => _customerDisplayShellController.SetMode(mode, owner);

    private void OpenCustomerDisplayWindow(Window? owner) => _customerDisplayShellController.Open(owner);

    private async Task ResetScannerBindingAsync()
    {
        await _rawScannerService.ResetBindingAsync();
        StatusMessage = _localization.T("main.scannerBindingReset");
    }

    private void AddPreviewCartItems(IReadOnlyList<SellableItemDto> items)
    {
        _cart.Clear();
        foreach (var item in items.Take(3))
        {
            _cart.AddItem(item);
        }

        if (items.Count > 1)
        {
            _cart.AddItem(items[1]);
        }

        ShowCashPaymentCommand.NotifyCanExecuteChanged();
    }

    private async Task<LocalOrder> CreatePreviewOrderAsync(IReadOnlyList<SellableItemDto> items)
    {
        var previewCart = new PosCartService();
        foreach (var item in items.Take(2))
        {
            previewCart.AddItem(item);
        }

        if (items.Count > 0)
        {
            previewCart.AddItem(items[0]);
        }

        var result = _checkout.CreateCashOrder(previewCart, Session, previewCart.ActualAmount);
        await _orderRepository.SavePendingOrderAsync(result.Order);
        PaymentSuccess.LoadFromOrder(result.Order);
        return result.Order;
    }

    private static IReadOnlyList<SellableItemDto> CreateStarterItems()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new(DefaultTestStoreCode, "SKU-001", null, "Organic Fuji Apple", "690001", "SKU-001", "690001", 4.50m, PriceSourceKind.StoreRetailPrice, "Store Price", 1m, now),
            new(DefaultTestStoreCode, "SKU-002", null, "Whole Milk 1L", "690002", "SKU-002", "690002", 3.20m, PriceSourceKind.ProductBase, "Base Price", 1m, now),
            new(DefaultTestStoreCode, "SKU-003", "SET-003", "Greek Yogurt Blueberry", "690003", "SKU-003", "690003", 3.75m, PriceSourceKind.StoreMultiCodeProduct, "Multi-code Store Price", 1m, now),
            new(DefaultTestStoreCode, "SKU-004", "CLR-004", "Cold Brew Concentrate", "690004", "SKU-004", "690004", 12.90m, PriceSourceKind.StoreClearancePrice, "Clearance Price", 1m, now)
        ];
    }
}

internal static class ObservableCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
