using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class ManualCardPaymentViewModelTests
{
    [Fact]
    public async Task Opening_cancel_and_unchecked_confirmation_do_not_pay()
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        using var vm = fixture.CreatePaymentViewModel(fixture.CreateSaleCart(10.03m), paymentMethodSettingsService: new MutablePaymentMethodSettingsService(new(UseManualCard: true)));
        vm.OpenManualCardCommand.Execute(null);
        Assert.True(vm.IsManualCardDialogOpen);
        Assert.Equal(10.03m, vm.ManualCardAmount);
        Assert.False(vm.ConfirmManualCardCommand.CanExecute(null));
        Assert.False(vm.SelectCardCommand.CanExecute(null));
        Assert.False(vm.SelectCashCommand.CanExecute(null));
        Assert.False(vm.BackToPosCommand.CanExecute(null));
        await vm.ConfirmManualCardCommand.ExecuteAsync(null);
        await vm.SelectCardCommand.ExecuteAsync(null);
        Assert.Empty(vm.PaymentTenders);
        vm.CancelManualCardCommand.Execute(null);
        Assert.False(vm.IsManualCardDialogOpen);
        Assert.Empty(await fixture.OrderRepository.GetRecentOrdersAsync());
        Assert.Equal(0, fixture.CloudApi.SendCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirming_manual_card_completes_exact_balance_once_without_terminal(bool cashFirst)
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        var cart = fixture.CreateSaleCart(10.03m);
        using var vm = fixture.CreatePaymentViewModel(cart, paymentMethodSettingsService: new MutablePaymentMethodSettingsService(new(UseManualCard: true)));
        if (cashFirst)
        {
            vm.TenderAmountText = "5.00";
            await vm.SelectCashCommand.ExecuteAsync(null);
        }
        vm.OpenManualCardCommand.Execute(null);
        Assert.Equal(cashFirst ? 5.03m : 10.03m, vm.ManualCardAmount);
        vm.IsManualCardSuccessChecked = true;
        await vm.ConfirmManualCardCommand.ExecuteAsync(null);
        await vm.ConfirmManualCardCommand.ExecuteAsync(null);
        Assert.True(cart.IsEmpty);
        Assert.False(vm.IsManualCardSavePending);
        var summary = Assert.Single(await fixture.OrderRepository.GetRecentOrdersAsync());
        var order = await fixture.OrderRepository.GetOrderAsync(summary.OrderGuid);
        var card = Assert.Single(order!.Payments.Where(payment => payment.Method == PaymentMethodKind.Card));
        Assert.Equal(cashFirst ? 5.03m : 10.03m, card.Amount);
        Assert.True(ManualCardPaymentReference.IsManual(card.Reference));
        Assert.Equal("Manual", Assert.Single(card.CardTransactions!).Processor);
        Assert.Equal(0, fixture.CloudApi.SendCount);
    }

    [Fact]
    public async Task Stale_cart_confirmation_cannot_pay_a_different_order()
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        var cart = fixture.CreateSaleCart();
        using var vm = fixture.CreatePaymentViewModel(cart, paymentMethodSettingsService: new MutablePaymentMethodSettingsService(new(UseManualCard: true)));
        vm.OpenManualCardCommand.Execute(null);
        cart.Clear();
        vm.IsManualCardSuccessChecked = true;
        await vm.ConfirmManualCardCommand.ExecuteAsync(null);
        Assert.Empty(vm.PaymentTenders);
        Assert.Empty(await fixture.OrderRepository.GetRecentOrdersAsync());
        Assert.False(vm.IsManualCardSuccessChecked);
    }

    [Fact]
    public async Task Unknown_card_result_and_installments_do_not_allow_manual_bypass()
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        using var vm = fixture.CreatePaymentViewModel(fixture.CreateSaleCart(100m), paymentMethodSettingsService: new MutablePaymentMethodSettingsService(new(UseManualCard: true)));
        vm.SetCurrentCardRecoveryRequired(true);
        Assert.False(vm.OpenManualCardCommand.CanExecute(null));
        vm.OpenManualCardCommand.Execute(null);
        Assert.False(vm.IsManualCardDialogOpen);
        vm.SetCurrentCardRecoveryRequired(false);
        vm.IsInstallmentPaymentEnabled = true;
        Assert.False(vm.OpenManualCardCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_failure_retains_payment_and_retry_saves_once_without_charging(bool commitBeforeThrow)
    {
        await using var fixture = await PaymentFlowTestFixture.CreateAsync(new ScriptedLinklyCloudApi(false));
        var repository = new FailOnceOrders(fixture.OrderRepository, commitBeforeThrow);
        var workflow = new CashPaymentWorkflowService(new CashCheckoutService(), repository, fixture.SyncQueueRepository,
            cardTerminalClient: fixture.ConfiguredCard);
        var cart = fixture.CreateSaleCart();
        var settings = new MutablePaymentMethodSettingsService(new(UseManualCard: true));
        using var vm = new PaymentViewModel(cart, workflow, fixture.Session, paymentMethodSettingsService: settings);
        vm.OpenManualCardCommand.Execute(null);
        vm.IsManualCardSuccessChecked = true;
        await vm.ConfirmManualCardCommand.ExecuteAsync(null);
        Assert.True(vm.IsManualCardSavePending);
        Assert.True(vm.IsPaymentInteractionLocked);
        Assert.Single(vm.PaymentTenders);
        Assert.False(vm.OpenManualCardCommand.CanExecute(null));
        vm.PrepareForEntry(fixture.Session);
        Assert.Single(vm.PaymentTenders);
        // 已经人工确认收款后，切换配置只能阻止新收款，不能阻止原单重试保存。
        await settings.SaveAsync(new());
        Assert.False(vm.IsManualCardPaymentVisible);
        await vm.RetryManualCardSaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsManualCardSavePending);
        Assert.True(cart.IsEmpty);
        Assert.Single(await fixture.OrderRepository.GetRecentOrdersAsync());
        Assert.Equal(0, fixture.CloudApi.SendCount);
    }

    private sealed class FailOnceOrders(ILocalOrderRepository inner, bool commitBeforeThrow) : ILocalOrderRepository
    {
        private bool _failed;
        public async Task SavePendingOrderAsync(LocalOrder order, CancellationToken cancellationToken = default)
        {
            if (_failed) { await inner.SavePendingOrderAsync(order, cancellationToken); return; }
            _failed = true;
            if (commitBeforeThrow) await inner.SavePendingOrderAsync(order, cancellationToken);
            throw new IOException("Simulated save failure");
        }
        public Task<LocalOrder?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetOrderAsync(id, cancellationToken);
        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(int take = 50, CancellationToken cancellationToken = default) => inner.GetRecentOrdersAsync(take, cancellationToken);
        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(LocalOrderHistoryQuery query, int take = 50, CancellationToken cancellationToken = default) => inner.GetRecentOrdersAsync(query, take, cancellationToken);
    }
}
