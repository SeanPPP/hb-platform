using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class CashPaymentWorkflowServiceTests
{
    [Fact]
    public void Cash_payment_workflow_parses_tendered_amount_and_calculates_change()
    {
        var workflow = CreateWorkflow();

        var parsed = workflow.TryParseTenderedAmount("10.5", out var tenderedAmount);
        var change = workflow.CalculateChange("10.5", 7.81m);

        Assert.True(parsed);
        Assert.Equal(10.5m, tenderedAmount);
        Assert.Equal(2.69m, change);
    }

    [Fact]
    public void Cash_payment_workflow_rejects_invalid_tendered_amount()
    {
        var workflow = CreateWorkflow();

        var parsed = workflow.TryParseTenderedAmount("cash", out var tenderedAmount);
        var change = workflow.CalculateChange("cash", 7.81m);

        Assert.False(parsed);
        Assert.Equal(0m, tenderedAmount);
        Assert.Equal(0m, change);
    }

    [Fact]
    public async Task Cash_payment_workflow_persists_order_clears_cart_and_refreshes_pending_sync()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-301", "Workflow Tea", "930301", 4.4m));
        var orders = new RecordingOrderRepository();
        var syncQueue = new StubSyncQueueRepository(pendingCount: 3);
        var workflow = new CashPaymentWorkflowService(new CashCheckoutService(), orders, syncQueue);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var result = await workflow.CompleteAsync(cart, session, "5");

        var savedOrder = Assert.Single(orders.SavedOrders);
        Assert.Same(savedOrder, result.Order);
        Assert.Equal(4.4m, savedOrder.ActualAmount);
        Assert.Equal(5m, result.TenderedAmount);
        Assert.Equal(0.6m, result.ChangeAmount);
        Assert.Empty(cart.Lines);
        Assert.Equal(3, result.PendingSyncCount);
        Assert.Equal(3, result.UpdatedSession.PendingSyncCount);
        Assert.Equal(savedOrder.OrderGuid, result.Order.OrderGuid);
    }

    [Fact]
    public async Task Payment_workflow_allocates_cash_change_without_overstating_local_payments()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-302", "Workflow Soda", "930302", 10m));
        var orders = new RecordingOrderRepository();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 2),
            cardTerminalClient: new ApprovedCardTerminalClient("CARD-001"));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var result = await workflow.CompletePaymentAsync(
            cart,
            session,
            [
                new PaymentTender(PaymentMethodKind.Card, 5m, "CARD-001"),
                new PaymentTender(PaymentMethodKind.Cash, 6m)
            ],
            cashTenderedAmount: 6m);

        var savedOrder = Assert.Single(orders.SavedOrders);
        Assert.Equal(11m, result.TenderedAmount);
        Assert.Equal(1m, result.ChangeAmount);
        Assert.Collection(
            savedOrder.Payments,
            payment =>
            {
                Assert.Equal(PaymentMethodKind.Card, payment.Method);
                Assert.Equal(5m, payment.Amount);
            },
            payment =>
            {
                Assert.Equal(PaymentMethodKind.Cash, payment.Method);
                Assert.Equal(5m, payment.Amount);
            });
    }

    [Fact]
    public async Task Payment_workflow_add_tender_blocks_non_cash_over_remaining_and_accepts_voucher_code()
    {
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            voucherTenderClient: new ApprovedVoucherTenderClient("VOUCHER-ABC"));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var blocked = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            actualAmount: 10m,
            currentTenders: [new PaymentTender(PaymentMethodKind.Card, 8m, "CARD-001")],
            amountText: "3");
        var voucher = await workflow.AddTenderAsync(
            PaymentMethodKind.Voucher,
            session,
            actualAmount: 10m,
            currentTenders: [],
            amountText: "4",
            referenceText: "ABC123");

        Assert.False(blocked.Succeeded);
        Assert.Equal("payment.status.cardExceedsRemaining", blocked.StatusKey);
        Assert.True(voucher.Succeeded);
        Assert.NotNull(voucher.Tender);
        Assert.Equal(PaymentMethodKind.Voucher, voucher.Tender.Method);
        Assert.Equal(4m, voucher.Tender.Amount);
        Assert.Equal("VOUCHER-ABC", voucher.Tender.Reference);
    }

    [Fact]
    public async Task Payment_workflow_uses_authorized_voucher_amount_for_partial_redemption()
    {
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            voucherTenderClient: new ApprovedVoucherTenderClient("VOUCHER-PARTIAL", authorizedAmount: 3m));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var voucher = await workflow.AddTenderAsync(
            PaymentMethodKind.Voucher,
            session,
            actualAmount: 10m,
            currentTenders: [],
            amountText: "5",
            referenceText: "ABC123");

        Assert.True(voucher.Succeeded);
        Assert.NotNull(voucher.Tender);
        Assert.Equal(3m, voucher.Tender.Amount);
        Assert.Equal("VOUCHER-PARTIAL", voucher.Tender.Reference);
    }

    [Fact]
    public async Task Payment_workflow_blocks_duplicate_voucher_code()
    {
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            voucherTenderClient: new ApprovedVoucherTenderClient("VOUCHER:ABC123:token-2"));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var duplicate = await workflow.AddTenderAsync(
            PaymentMethodKind.Voucher,
            session,
            actualAmount: 10m,
            currentTenders: [new PaymentTender(PaymentMethodKind.Voucher, 3m, "VOUCHER:ABC123:token-1")],
            amountText: "2",
            referenceText: "abc123");

        Assert.False(duplicate.Succeeded);
        Assert.Equal("payment.status.duplicateVoucher", duplicate.StatusKey);
    }

    [Fact]
    public async Task Payment_workflow_retries_failed_voucher_upload_without_saving_duplicate_order()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-303", "Voucher Retry Tea", "930303", 8m));
        var orders = new RecordingOrderRepository();
        var uploads = new FailingOnceOrderUploadService();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            orderUploadService: uploads);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);
        var tenders = new[]
        {
            new PaymentTender(PaymentMethodKind.Voucher, 3m, "VOUCHER:ABC123:token-1"),
            new PaymentTender(PaymentMethodKind.Cash, 5m)
        };

        var failed = await Assert.ThrowsAsync<PaymentUploadFailedException>(() => workflow.CompletePaymentAsync(
            cart,
            session,
            tenders,
            cashTenderedAmount: 5m));
        var result = await workflow.RetryVoucherUploadAsync(
            failed.OrderGuid,
            cart,
            session,
            failed.TenderedAmount,
            failed.ChangeAmount);

        Assert.Single(orders.SavedOrders);
        Assert.Equal(failed.OrderGuid, result.Order.OrderGuid);
        Assert.Equal([failed.OrderGuid, failed.OrderGuid], uploads.AttemptedOrderGuids);
        Assert.Empty(cart.Lines);
    }

    private static ICashPaymentWorkflowService CreateWorkflow()
    {
        return new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1));
    }

    private static SellableItemDto CreateItem(string productCode, string name, string lookupCode, decimal price)
    {
        return new SellableItemDto(
            StoreCode: "S001",
            ProductCode: productCode,
            ReferenceCode: null,
            DisplayName: name,
            LookupCode: lookupCode,
            ItemNumber: productCode,
            Barcode: lookupCode,
            RetailPrice: price,
            PriceSource: PriceSourceKind.StoreRetailPrice,
            PriceSourceLabel: PriceSourceKind.StoreRetailPrice.ToString(),
            QuantityFactor: 1m,
            UpdatedAt: DateTimeOffset.UtcNow);
    }

    private sealed class RecordingOrderRepository : ILocalOrderRepository
    {
        public List<LocalOrder> SavedOrders { get; } = [];

        public Task SavePendingOrderAsync(LocalOrder order, CancellationToken cancellationToken = default)
        {
            SavedOrders.Add(order);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(int take = 50, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);
        }

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(
            LocalOrderHistoryQuery query,
            int take = 50,
            CancellationToken cancellationToken = default)
        {
            return GetRecentOrdersAsync(take, cancellationToken);
        }

        public Task<LocalOrder?> GetOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LocalOrder?>(SavedOrders.LastOrDefault(order => order.OrderGuid == orderGuid));
        }
    }

    private sealed class StubSyncQueueRepository(int pendingCount) : ISyncQueueRepository
    {
        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(pendingCount);
        }

        public Task<SyncQueueOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SyncQueueOverview(pendingCount, 0, 0, null));
        }

        public Task<IReadOnlyList<SyncQueueListItem>> GetActiveItemsAsync(int take = 20, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SyncQueueListItem>>([]);
        }
    }

    private sealed class ApprovedCardTerminalClient(string reference) : ICardTerminalClient
    {
        public Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaymentAuthorizationResult(true, reference));
        }
    }

    private sealed class ApprovedVoucherTenderClient(string reference, decimal? authorizedAmount = null) : IVoucherTenderClient
    {
        public Task<PaymentAuthorizationResult> RedeemAsync(
            decimal amount,
            PosSessionState session,
            string? voucherCode,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("ABC123", voucherCode);
            return Task.FromResult(new PaymentAuthorizationResult(true, reference, AuthorizedAmount: authorizedAmount));
        }
    }

    private sealed class FailingOnceOrderUploadService : IOrderUploadService
    {
        private bool _hasFailed;

        public List<Guid> AttemptedOrderGuids { get; } = [];

        public Task UploadOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            AttemptedOrderGuids.Add(orderGuid);
            if (!_hasFailed)
            {
                _hasFailed = true;
                throw new InvalidOperationException("network unavailable");
            }

            return Task.CompletedTask;
        }
    }
}
