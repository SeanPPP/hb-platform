using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
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

    public MainChildViewModelFactory(
        IDeviceRegistrationWorkflowService deviceRegistrationWorkflowService,
        IReceiptQueryService receiptQueryService,
        ISuspendedOrderService? suspendedOrderService,
        IRemoteOrderHistoryService? remoteOrderHistoryService,
        IReceiptTextFormatter receiptTextFormatter,
        IReceiptPrinterSettingsStore? receiptPrinterSettingsStore,
        IInstallmentOrderService installmentOrderService,
        ILocalizationService localization,
        ICardTerminalClient? cardTerminalClient)
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
    }

    public DeviceRegistrationViewModel CreateDeviceRegistrationViewModel(
        Func<DeviceActivatedEventArgs, Task> activateDeviceAsync,
        Action applyDeviceReregistered,
        Action cancelDeviceReregistration)
    {
        // 中文说明：工厂只负责组装依赖和绑定回调，真正的状态切换仍由 MainViewModel 提供的委托处理。
        var viewModel = new DeviceRegistrationViewModel(_deviceRegistrationWorkflowService, _localization);
        viewModel.DeviceActivatedAsync += (_, args) => activateDeviceAsync(args);
        viewModel.DeviceReregistered += (_, _) => applyDeviceReregistered();
        viewModel.CancelRequested += (_, _) => cancelDeviceReregistration();
        return viewModel;
    }

    public TransactionHistoryViewModel CreateTransactionHistoryViewModel(
        PosSessionState session,
        Func<Task> onSuspendedOrderRecalledAsync,
        Action showPos,
        Func<TransactionHistoryViewModel, Task> printSelectedHistoryReceiptAsync)
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
            _receiptPrinterSettingsStore);
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
            _cardTerminalClient);
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
            _localization);
    }
}
