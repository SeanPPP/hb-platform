using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class PaymentMethodAvailabilityViewModelTests
{
    [Fact]
    public async Task Defaults_enable_integrated_card_and_hide_manual_card_and_voucher()
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        var workflow = new CountingWorkflow(fixture.Workflow);
        using var vm = new PaymentViewModel(fixture.CreateSaleCart(), workflow, fixture.Session);

        Assert.True(vm.IsIntegratedCardPaymentVisible);
        Assert.False(vm.IsManualCardPaymentVisible);
        Assert.False(vm.IsVoucherPaymentVisible);
        Assert.True(vm.SelectCashCommand.CanExecute(null));
        Assert.True(vm.SelectCardCommand.CanExecute(null));
        Assert.False(vm.OpenManualCardCommand.CanExecute(null));
        Assert.False(vm.OpenVoucherEntryCommand.CanExecute(null));
        Assert.False(vm.SelectVoucherCommand.CanExecute(null));
        vm.OpenManualCardCommand.Execute(null);
        vm.OpenVoucherEntryCommand.Execute(null);
        vm.VoucherCodeText = "VOUCHER-TEST";
        vm.VoucherEntryText = "VOUCHER-TEST";
        // 直接执行隐藏按钮对应的命令，也不能进入工作流。
        await vm.SelectVoucherCommand.ExecuteAsync(null);
        await vm.ConfirmVoucherEntryCommand.ExecuteAsync(null);
        Assert.False(vm.IsManualCardDialogOpen);
        Assert.False(vm.IsVoucherEntryDialogOpen);
        Assert.Empty(vm.PaymentTenders);
        Assert.Equal(0, workflow.AddCount);
    }

    [Fact]
    public async Task Persisted_manual_mode_replaces_integrated_card_and_blocks_direct_execution()
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        var settings = new MutablePaymentMethodSettingsService(new(UseManualCard: true, VoucherEnabled: true));
        var workflow = new CountingWorkflow(fixture.Workflow);
        using var vm = new PaymentViewModel(fixture.CreateSaleCart(), workflow, fixture.Session,
            paymentMethodSettingsService: settings);
        await vm.RefreshPaymentMethodSettingsAsync();

        Assert.False(vm.IsIntegratedCardPaymentVisible);
        Assert.True(vm.IsManualCardPaymentVisible);
        Assert.True(vm.IsVoucherPaymentVisible);
        Assert.False(vm.SelectCardCommand.CanExecute(null));
        await vm.SelectCardCommand.ExecuteAsync(null);
        Assert.Equal(0, workflow.AddCount);
        Assert.Equal(0, fixture.CloudApi.SendCount);
        vm.OpenManualCardCommand.Execute(null);
        Assert.True(vm.IsManualCardDialogOpen);
    }

    [Fact]
    public async Task Changed_settings_refresh_open_payment_page_without_recreating_it()
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        var settings = new MutablePaymentMethodSettingsService();
        using var vm = fixture.CreatePaymentViewModel(fixture.CreateSaleCart(), paymentMethodSettingsService: settings);
        var commandChangeCount = 0;
        vm.SelectCardCommand.CanExecuteChanged += (_, _) => commandChangeCount++;

        await settings.SaveAsync(new(UseManualCard: true, VoucherEnabled: true));
        Assert.True(vm.IsManualCardPaymentVisible);
        Assert.False(vm.IsIntegratedCardPaymentVisible);
        Assert.True(vm.IsVoucherPaymentVisible);
        Assert.True(commandChangeCount > 0);
        vm.OpenVoucherEntryCommand.Execute(null);
        Assert.True(vm.IsVoucherEntryDialogOpen);

        await settings.SaveAsync(new());
        Assert.False(vm.IsManualCardPaymentVisible);
        Assert.True(vm.IsIntegratedCardPaymentVisible);
        Assert.False(vm.IsVoucherPaymentVisible);
        Assert.False(vm.ConfirmVoucherEntryCommand.CanExecute(null));
        Assert.False(vm.SelectVoucherCommand.CanExecute(null));
    }

    [Fact]
    public async Task Changing_modes_preserves_unknown_result_lock()
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        var settings = new MutablePaymentMethodSettingsService();
        using var vm = fixture.CreatePaymentViewModel(fixture.CreateSaleCart(), paymentMethodSettingsService: settings);
        vm.SetCurrentCardRecoveryRequired(true);
        await settings.SaveAsync(new(UseManualCard: true, VoucherEnabled: true));
        Assert.True(vm.IsPaymentInteractionLocked);
        Assert.False(vm.SelectCardCommand.CanExecute(null));
        Assert.False(vm.OpenManualCardCommand.CanExecute(null));
        Assert.False(vm.SelectCashCommand.CanExecute(null));
        Assert.False(vm.SelectVoucherCommand.CanExecute(null));
        Assert.False(vm.BackToPosCommand.CanExecute(null));
        vm.OpenManualCardCommand.Execute(null);
        Assert.False(vm.IsManualCardDialogOpen);
    }

    [Fact]
    public async Task Loading_settings_blocks_new_payments_until_configuration_is_known()
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        var load = new TaskCompletionSource<PaymentMethodSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new MutablePaymentMethodSettingsService { LoadHandler = _ => load.Task };
        var workflow = new CountingWorkflow(fixture.Workflow);
        using var vm = new PaymentViewModel(fixture.CreateSaleCart(), workflow, fixture.Session,
            paymentMethodSettingsService: settings);

        Assert.False(vm.SelectCardCommand.CanExecute(null));
        Assert.False(vm.SelectCashCommand.CanExecute(null));
        await vm.SelectCardCommand.ExecuteAsync(null);
        await vm.SelectCashCommand.ExecuteAsync(null);
        Assert.Equal(0, workflow.AddCount);
        load.SetResult(new(UseManualCard: true));
        await vm.RefreshPaymentMethodSettingsAsync();
        Assert.True(vm.IsManualCardPaymentVisible);
        Assert.True(vm.SelectCashCommand.CanExecute(null));
        Assert.False(vm.SelectCardCommand.CanExecute(null));
    }

    [Fact]
    public async Task Failed_settings_load_cannot_start_terminal_or_voucher_payment()
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        var settings = new MutablePaymentMethodSettingsService
        {
            LoadHandler = _ => Task.FromException<PaymentMethodSettings>(new IOException("settings unavailable"))
        };
        var workflow = new CountingWorkflow(fixture.Workflow);
        using var vm = new PaymentViewModel(fixture.CreateSaleCart(), workflow, fixture.Session,
            paymentMethodSettingsService: settings);
        await vm.RefreshPaymentMethodSettingsAsync();
        await vm.SelectCardCommand.ExecuteAsync(null);
        await vm.SelectCashCommand.ExecuteAsync(null);
        await vm.SelectVoucherCommand.ExecuteAsync(null);
        vm.OpenManualCardCommand.Execute(null);
        Assert.Equal(0, workflow.AddCount);
        Assert.Equal(0, fixture.CloudApi.SendCount);
        Assert.False(vm.IsManualCardDialogOpen);
    }

    [Theory]
    [InlineData("Card")]
    [InlineData("Voucher")]
    [InlineData("Manual")]
    public async Task Method_disabled_while_authorization_is_pending_cannot_start_payment(string method)
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        var settings = new MutablePaymentMethodSettingsService(new(UseManualCard: method == "Manual", VoucherEnabled: true));
        var authorization = new PausedAuthorizationService();
        var workflow = new CountingWorkflow(fixture.Workflow);
        using var vm = new PaymentViewModel(fixture.CreateSaleCart(), workflow, fixture.Session,
            operationAuthorizationService: authorization, paymentMethodSettingsService: settings);

        Task operation;
        if (method == "Manual")
        {
            vm.OpenManualCardCommand.Execute(null);
            vm.IsManualCardSuccessChecked = true;
            operation = vm.ConfirmManualCardCommand.ExecuteAsync(null);
        }
        else
        {
            vm.VoucherCodeText = "VOUCHER-TEST";
            operation = method == "Card"
                ? vm.SelectCardCommand.ExecuteAsync(null)
                : vm.SelectVoucherCommand.ExecuteAsync(null);
        }
        await authorization.Started.Task.WaitAsync(AsyncTestWaitSupport.DefaultTimeout);
        await settings.SaveAsync(new(UseManualCard: method == "Card", VoucherEnabled: false));
        authorization.Continue.TrySetResult();
        await operation.WaitAsync(AsyncTestWaitSupport.DefaultTimeout);

        Assert.Equal(0, workflow.AddCount);
        Assert.Equal(0, workflow.ManualAddCount);
        Assert.Empty(vm.PaymentTenders);
        Assert.Empty(await fixture.OrderRepository.GetRecentOrdersAsync());
        Assert.Equal(0, fixture.CloudApi.SendCount);
    }

    private sealed class CountingWorkflow(ICashPaymentWorkflowService inner) : ICashPaymentWorkflowService
    {
        public int AddCount { get; private set; }
        public int ManualAddCount { get; private set; }
        public bool TryParseTenderedAmount(string? text, out decimal amount) => inner.TryParseTenderedAmount(text, out amount);
        public decimal CalculateChange(string? text, decimal amount) => inner.CalculateChange(text, amount);
        public decimal CalculateTenderedAmount(IReadOnlyList<PaymentTender> tenders) => inner.CalculateTenderedAmount(tenders);
        public decimal CalculateRemainingAmount(decimal amount, IReadOnlyList<PaymentTender> tenders) => inner.CalculateRemainingAmount(amount, tenders);
        public decimal CalculateChange(IReadOnlyList<PaymentTender> tenders, decimal amount) => inner.CalculateChange(tenders, amount);
        public Task<PaymentTenderAttemptResult> AddTenderAsync(PaymentMethodKind method, PosSessionState session, decimal actualAmount,
            IReadOnlyList<PaymentTender> tenders, string? text, string? referenceText = null,
            CancellationToken cancellationToken = default, PosCartSnapshot? cartSnapshot = null)
        {
            AddCount++;
            return inner.AddTenderAsync(method, session, actualAmount, tenders, text, referenceText, cancellationToken, cartSnapshot);
        }
        public Task<PaymentTenderAttemptResult> AddManualCardTenderAsync(PosSessionState session, decimal actualAmount,
            IReadOnlyList<PaymentTender> tenders, string? text, Guid confirmationId, CancellationToken cancellationToken = default)
        {
            ManualAddCount++;
            return inner.AddManualCardTenderAsync(session, actualAmount, tenders, text, confirmationId, cancellationToken);
        }
        public Task<CashPaymentWorkflowResult> CompleteAsync(PosCartService cart, PosSessionState session, string? text,
            CancellationToken cancellationToken = default) => inner.CompleteAsync(cart, session, text, cancellationToken);
        public Task<CashPaymentWorkflowResult> CompletePaymentAsync(PosCartService cart, PosSessionState session,
            IReadOnlyList<PaymentTender> tenders, decimal cashTenderedAmount, CancellationToken cancellationToken = default) =>
            inner.CompletePaymentAsync(cart, session, tenders, cashTenderedAmount, cancellationToken);
        public Task<CashPaymentWorkflowResult> RetryVoucherUploadAsync(Guid orderGuid, PosCartService cart, PosSessionState session,
            decimal tenderedAmount, decimal changeAmount, CancellationToken cancellationToken = default) =>
            inner.RetryVoucherUploadAsync(orderGuid, cart, session, tenderedAmount, changeAmount, cancellationToken);
    }

    private sealed class PausedAuthorizationService : IOperationAuthorizationService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public event EventHandler? StatusChanged { add { } remove { } }
        public string ScannerPageId => "Test";
        public bool IsPromptOpen => !Continue.Task.IsCompleted;
        public bool IsBusy => false;
        public string PromptMessage => string.Empty;
        public string StatusMessage => string.Empty;
        public string PermissionCode => string.Empty;
        public string Screen => string.Empty;
        public string Action => string.Empty;
        public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });
        public async Task<OperationAuthorizationScope?> AuthorizeAsync(string permissionCode, string screen, string action,
            PosSessionState session, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Continue.Task.WaitAsync(cancellationToken);
            var cashier = new CashierSessionDto(session.CashierId, session.CashierId, session.CashierName,
                session.StoreCode, session.DeviceCode, [], [], [session.StoreCode], false, false, false);
            return new OperationAuthorizationScope(cashier, permissionCode, screen, action);
        }
        public bool ProcessScannerBarcode(string barcode) => false;
        public void Cancel() { }
        public void RevokeAll() { }
    }
}
