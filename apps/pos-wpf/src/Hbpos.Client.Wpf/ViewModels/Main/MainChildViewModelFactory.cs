using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.ViewModels;

// 中文说明：Phase 4a 先把子 VM 的创建和事件挂接收束到工厂里，避免直接改动 MainViewModel 中高风险的导航、启动和同步逻辑。
internal sealed class MainChildViewModelFactory
{
    private readonly IDeviceRegistrationWorkflowService _deviceRegistrationWorkflowService;
    private readonly IReceiptQueryService _receiptQueryService;
    private readonly ISuspendedOrderService? _suspendedOrderService;
    private readonly IRemoteOrderHistoryService? _remoteOrderHistoryService;
    private readonly IReceiptTextFormatter _receiptTextFormatter;
    private readonly IReceiptPrinterSettingsStore? _receiptPrinterSettingsStore;
    private readonly IInstallmentOrderService _installmentOrderService;
    private readonly ILocalizationService _localization;
    private readonly ICardTerminalClient? _cardTerminalClient;

    // Phase 2: new dependencies for additional child VMs.
    private readonly LocalSellableItemIndex _priceIndex;
    private readonly PosCartService _cart;
    private readonly ILocalCatalogRepository _catalogRepository;
    private readonly ISpecialProductService _specialProductService;
    private readonly ISpecialProductsWorkflowService _specialProductsWorkflowService;
    private readonly IReceiptReturnsWorkflowService _receiptReturnsWorkflowService;
    private readonly ICashPaymentWorkflowService _cashPaymentWorkflowService;
    private readonly ICardTerminalSetupService? _cardTerminalSetupService;
    private readonly IRawScannerService _rawScannerService;
    private readonly IDailyCloseService _dailyCloseService;
    private readonly IDailyClosePrintService _dailyClosePrintService;
    private readonly IUserFeedbackService? _userFeedbackService;
    private readonly IPromotionEvaluationService? _promotionEvaluationService;
    private readonly IReceiptPrintService? _receiptPrintService;
    private readonly ICardRecoveryResultDialogService? _cardRecoveryResultDialogService;
    private readonly ICashierSessionContext? _cashierSessionContext;
    private readonly bool _enforceCashierPermissions;
    private readonly Func<CancellationToken, Task<AppUpdateCoordinatorResult>>? _checkForAppUpdateAsync;
    private readonly IAppUpdateChannelProvider? _appUpdateChannelProvider;
    private readonly IOperationAuditLogger? _operationAuditLogger;
    private readonly ApiServerSettingsViewModel? _apiServerSettings;
    private readonly IOperationAuthorizationService? _operationAuthorizationService;

    public MainChildViewModelFactory(
        IDeviceRegistrationWorkflowService deviceRegistrationWorkflowService,
        IReceiptQueryService receiptQueryService,
        ISuspendedOrderService? suspendedOrderService,
        IRemoteOrderHistoryService? remoteOrderHistoryService,
        IReceiptTextFormatter receiptTextFormatter,
        IReceiptPrinterSettingsStore? receiptPrinterSettingsStore,
        IInstallmentOrderService installmentOrderService,
        ILocalizationService localization,
        ICardTerminalClient? cardTerminalClient,
        // Phase 2: new dependencies.
        LocalSellableItemIndex priceIndex,
        PosCartService cart,
        ILocalCatalogRepository catalogRepository,
        ISpecialProductService specialProductService,
        ISpecialProductsWorkflowService specialProductsWorkflowService,
        IReceiptReturnsWorkflowService receiptReturnsWorkflowService,
        ICashPaymentWorkflowService cashPaymentWorkflowService,
        ICardTerminalSetupService? cardTerminalSetupService,
        IRawScannerService rawScannerService,
        IDailyCloseService dailyCloseService,
        IDailyClosePrintService dailyClosePrintService,
        IUserFeedbackService? userFeedbackService = null,
        IPromotionEvaluationService? promotionEvaluationService = null,
        IReceiptPrintService? receiptPrintService = null,
        ICardRecoveryResultDialogService? cardRecoveryResultDialogService = null,
        ICashierSessionContext? cashierSessionContext = null,
        bool enforceCashierPermissions = false,
        Func<CancellationToken, Task<AppUpdateCoordinatorResult>>? checkForAppUpdateAsync = null,
        IAppUpdateChannelProvider? appUpdateChannelProvider = null,
        IOperationAuditLogger? operationAuditLogger = null,
        ApiServerSettingsViewModel? apiServerSettings = null,
        IOperationAuthorizationService? operationAuthorizationService = null)
    {
        _deviceRegistrationWorkflowService = deviceRegistrationWorkflowService;
        _receiptQueryService = receiptQueryService;
        _suspendedOrderService = suspendedOrderService;
        _remoteOrderHistoryService = remoteOrderHistoryService;
        _receiptTextFormatter = receiptTextFormatter;
        _receiptPrinterSettingsStore = receiptPrinterSettingsStore;
        _installmentOrderService = installmentOrderService;
        _localization = localization;
        _cardTerminalClient = cardTerminalClient;

        _priceIndex = priceIndex;
        _cart = cart;
        _catalogRepository = catalogRepository;
        _specialProductService = specialProductService;
        _specialProductsWorkflowService = specialProductsWorkflowService;
        _receiptReturnsWorkflowService = receiptReturnsWorkflowService;
        _cashPaymentWorkflowService = cashPaymentWorkflowService;
        _cardTerminalSetupService = cardTerminalSetupService;
        _rawScannerService = rawScannerService;
        _dailyCloseService = dailyCloseService;
        _dailyClosePrintService = dailyClosePrintService;
        _userFeedbackService = userFeedbackService;
        _promotionEvaluationService = promotionEvaluationService;
        _receiptPrintService = receiptPrintService;
        _cardRecoveryResultDialogService = cardRecoveryResultDialogService;
        _cashierSessionContext = cashierSessionContext;
        _enforceCashierPermissions = enforceCashierPermissions;
        _checkForAppUpdateAsync = checkForAppUpdateAsync;
        _appUpdateChannelProvider = appUpdateChannelProvider;
        _operationAuditLogger = operationAuditLogger;
        _apiServerSettings = apiServerSettings;
        _operationAuthorizationService = operationAuthorizationService;
    }

    public DeviceRegistrationViewModel CreateDeviceRegistrationViewModel(
        Func<DeviceActivatedEventArgs, Task> activateDeviceAsync,
        Action applyDeviceReregistered,
        Action cancelDeviceReregistration)
    {
        // 中文说明：工厂只负责组装依赖和绑定回调，真正的状态切换仍由 MainViewModel 提供的委托处理。
        var viewModel = new DeviceRegistrationViewModel(
            _deviceRegistrationWorkflowService,
            _localization,
            apiServerSettings: _apiServerSettings);
        viewModel.DeviceActivatedAsync += (_, args) => activateDeviceAsync(args);
        viewModel.DeviceReregistered += (_, _) => applyDeviceReregistered();
        viewModel.CancelRequested += (_, _) => cancelDeviceReregistration();
        return viewModel;
    }

    public TransactionHistoryViewModel CreateTransactionHistoryViewModel(
        PosSessionState session,
        Func<Task> onSuspendedOrderRecalledAsync,
        Action showPos,
        Func<TransactionHistoryViewModel, Task> printSelectedHistoryReceiptAsync,
        Func<InstallmentOrderSummary, Task>? continueInstallmentPaymentAsync = null)
    {
        var viewModel = new TransactionHistoryViewModel(
            _receiptQueryService,
            _suspendedOrderService,
            _remoteOrderHistoryService,
            session,
            onSuspendedOrderRecalledAsync,
            showPos,
            _localization,
            _receiptTextFormatter,
            _receiptPrinterSettingsStore,
            _cashierSessionContext,
            _enforceCashierPermissions,
            _installmentOrderService,
            continueInstallmentPaymentAsync,
            _operationAuditLogger,
            _operationAuthorizationService);
        viewModel.ReprintRequested += async (_, _) => await printSelectedHistoryReceiptAsync(viewModel);
        return viewModel;
    }

    public InstallmentCenterViewModel CreateInstallmentCenterViewModel(
        PosSessionState session,
        Func<PosCartServiceSnapshot?, Task> showInstallmentCreateAsync,
        Action showCashPayment)
    {
        return new InstallmentCenterViewModel(
            _installmentOrderService,
            session,
            showInstallmentCreateAsync,
            showCashPayment,
            _localization,
            _cardTerminalClient,
            _cashierSessionContext,
            _enforceCashierPermissions,
            _operationAuditLogger,
            _operationAuthorizationService);
    }

    public InstallmentCreateViewModel CreateInstallmentCreateViewModel(
        PosSessionState session,
        Func<InstallmentOrderSummary, Task> onCreatedAsync,
        Action backToCenter)
    {
        return new InstallmentCreateViewModel(
            _installmentOrderService,
            session,
            onCreatedAsync,
            backToCenter,
            _localization,
            _cashierSessionContext,
            _enforceCashierPermissions,
            _operationAuditLogger,
            _operationAuthorizationService);
    }

    public PaymentSuccessViewModel CreatePaymentSuccessViewModel()
    {
        return new PaymentSuccessViewModel(
            _receiptQueryService,
            _receiptTextFormatter,
            _receiptPrinterSettingsStore);
    }

    public PosTerminalViewModel CreatePosTerminalViewModel(
        PosSessionState session,
        Action? onOpenPayment,
        Func<Task>? onOpenSpecialProductsAsync = null,
        Func<Task>? onHoldOrderAsync = null,
        Func<Task>? onRecallOrderAsync = null,
        Func<Task>? onOpenHistoryAsync = null,
        Func<Task>? onOpenDailyCloseAsync = null,
        Func<Task>? onOpenSettingsAsync = null,
        Action? onOpenCustomerDisplay = null,
        Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? syncCatalogAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<SellableItemDto>>>? resetCatalogAsync = null,
        Func<CancellationToken, Task<bool>>? refreshOnlineAsync = null,
        Func<Task>? onReregisterDeviceAsync = null,
        IPosTerminalWorkflowService? workflowService = null,
        Action? onOpenReturns = null,
        Func<Task<ReceiptPrintResult>>? onPrintLastReceiptAsync = null,
        Func<Task<ReceiptPrintResult>>? onOpenCashDrawerAsync = null,
        Func<Task>? onExitApplicationAsync = null,
        Func<string, CancellationToken, Task<bool>>? tryLoginCashierFromScannerFallbackAsync = null,
        Func<Task>? onLockCashierAsync = null)
    {
        return new PosTerminalViewModel(
            _priceIndex,
            _cart,
            session,
            onOpenPayment,
            onOpenSpecialProductsAsync,
            _localization,
            userFeedbackService: _userFeedbackService,
            promotionEvaluationService: _promotionEvaluationService,
            onHoldOrderAsync: onHoldOrderAsync,
            onRecallOrderAsync: onRecallOrderAsync,
            onOpenHistoryAsync: onOpenHistoryAsync,
            onOpenDailyCloseAsync: onOpenDailyCloseAsync,
            onOpenSettingsAsync: onOpenSettingsAsync,
            onOpenCustomerDisplay: onOpenCustomerDisplay,
            syncCatalogAsync: syncCatalogAsync,
            resetCatalogAsync: resetCatalogAsync,
            refreshOnlineAsync: refreshOnlineAsync,
            rawScannerService: _rawScannerService,
            onReregisterDeviceAsync: onReregisterDeviceAsync,
            workflowService: workflowService,
            onOpenReturns: onOpenReturns,
            onPrintLastReceiptAsync: onPrintLastReceiptAsync,
            onOpenCashDrawerAsync: onOpenCashDrawerAsync,
            onExitApplicationAsync: onExitApplicationAsync,
            tryLoginCashierFromScannerFallbackAsync: tryLoginCashierFromScannerFallbackAsync,
            cashierSessionContext: _cashierSessionContext,
            enforcePermissionsWhenNoCashier: _enforceCashierPermissions,
            operationAuditLogger: _operationAuditLogger,
            onLockCashierAsync: onLockCashierAsync,
            operationAuthorizationService: _operationAuthorizationService);
    }

    public SpecialProductsViewModel CreateSpecialProductsViewModel(
        PosSessionState session,
        Action onBack,
        Action<CartLine>? onCartLineAdded = null)
    {
        return new SpecialProductsViewModel(
            _priceIndex,
            _cart,
            _catalogRepository,
            _specialProductService,
            session,
            _localization,
            onBack,
            onCartLineAdded,
            _specialProductsWorkflowService,
            _rawScannerService,
            cashierSessionContext: _cashierSessionContext,
            enforcePermissionsWhenNoCashier: _enforceCashierPermissions,
            operationAuthorizationService: _operationAuthorizationService);
    }

    public ReceiptReturnsViewModel CreateReceiptReturnsViewModel(
        PosSessionState session,
        Action onBack,
        Action<CartLine>? onReturnLineAdded = null)
    {
        return new ReceiptReturnsViewModel(
            _receiptReturnsWorkflowService,
            session,
            onBack,
            onReturnLineAdded,
            _rawScannerService,
            _localization,
            _cashierSessionContext,
            _enforceCashierPermissions,
            _operationAuthorizationService);
    }

    public PaymentViewModel CreatePaymentViewModel(
        PosSessionState session,
        Action? onBackToPos = null,
        Action? onShowInstallmentCenter = null,
        Func<Task<bool>>? recoverPreviousCardTransactionAsync = null,
        ILinklyFallbackPromptCoordinator? linklyFallbackPromptCoordinator = null,
        Func<InstallmentOrderSummary, Task>? onInstallmentOrderCreatedAsync = null,
        Func<Task<bool>>? confirmInstallmentFullFirstPaymentAsync = null)
    {
        return new PaymentViewModel(
            _cart,
            _cashPaymentWorkflowService,
            session,
            _localization,
            onBackToPos,
            onShowInstallmentCenter,
            recoverPreviousCardTransactionAsync,
            linklyFallbackPromptCoordinator,
            _cashierSessionContext,
            _enforceCashierPermissions,
            _installmentOrderService,
            onInstallmentOrderCreatedAsync,
            confirmInstallmentFullFirstPaymentAsync,
            _operationAuditLogger,
            _operationAuthorizationService);
    }

    public DailyCloseViewModel CreateDailyCloseViewModel(
        PosSessionState session,
        Action? returnToPos = null)
    {
        return new DailyCloseViewModel(
            _dailyCloseService,
            _dailyClosePrintService,
            session,
            _localization,
            returnToPos,
            _cashierSessionContext,
            _enforceCashierPermissions,
            _operationAuditLogger,
            _operationAuthorizationService);
    }

    public SettingsViewModel CreateSettingsViewModel(
        PosSessionState? session = null,
        Func<CancellationToken, Task>? downloadCatalogAsync = null,
        Func<CancellationToken, Task>? resetCatalogAsync = null,
        Func<Task<DeviceReregistrationStartResult>>? reregisterDeviceAsync = null,
        Action? returnToPos = null,
        Func<CancellationToken, Task>? resetTestSalesDataAsync = null,
        Func<Task<bool>>? confirmResetTestSalesDataAsync = null,
        Func<CancellationToken, Task<AppUpdateCoordinatorResult>>? checkForAppUpdateAsync = null)
    {
        return new SettingsViewModel(
            _cardTerminalSetupService!,
            _localization,
            downloadCatalogAsync,
            resetCatalogAsync,
            reregisterDeviceAsync,
            returnToPos,
            _receiptPrinterSettingsStore,
            _receiptPrintService,
            resetTestSalesDataAsync: resetTestSalesDataAsync,
            confirmResetTestSalesDataAsync: confirmResetTestSalesDataAsync,
            cardRecoveryResultDialogService: _cardRecoveryResultDialogService,
            checkForAppUpdateAsync: checkForAppUpdateAsync ?? _checkForAppUpdateAsync,
            appUpdateChannel: _appUpdateChannelProvider?.CurrentChannel,
            cashierSessionContext: _cashierSessionContext,
            enforcePermissionsWhenNoCashier: _enforceCashierPermissions,
            apiServerSettings: _apiServerSettings,
            operationAuthorizationService: _operationAuthorizationService,
            session: session);
    }

    public CustomerDisplayViewModel CreateCustomerDisplayViewModel()
    {
        return new CustomerDisplayViewModel();
    }
}
