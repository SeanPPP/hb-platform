using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class ManualCardPaymentWorkflowTests
{
    private static readonly PosSessionState Session = new("HB POS", "S001", "Store", "POS-01", "C001", "Alice", true, 0);

    [Fact]
    public async Task Manual_confirmation_records_card_tail_without_calling_terminal()
    {
        var terminal = new RejectTerminal();
        var workflow = CreateWorkflow(new Orders(), terminal);
        var id = Guid.NewGuid();
        var result = await workflow.AddManualCardTenderAsync(Session, 10.03m,
            [new PaymentTender(PaymentMethodKind.Cash, 5m)], "5.03", id);

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentMethodKind.Card, result.Tender!.Method);
        Assert.Equal(5.03m, result.Tender.Amount);
        Assert.Equal(ManualCardPaymentReference.Format(id), result.Tender.Reference);
        Assert.Equal(result.Tender.Reference, result.Tender.IdempotencyKey);
        Assert.Equal("Manual", Assert.Single(result.Tender.CardTransactions!).Processor);
        Assert.Equal(0, terminal.Calls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("9")]
    [InlineData("11")]
    [InlineData("10.001")]
    [InlineData("invalid")]
    public async Task Manual_confirmation_rejects_invalid_or_partial_amount(string amount)
    {
        var result = await CreateWorkflow(new Orders()).AddManualCardTenderAsync(Session, 10m, [], amount, Guid.NewGuid());
        Assert.False(result.Succeeded);
        Assert.Null(result.Tender);
    }

    [Fact]
    public async Task Manual_confirmation_rejects_refunds_empty_identity_and_existing_card()
    {
        var workflow = CreateWorkflow(new Orders());
        Assert.False((await workflow.AddManualCardTenderAsync(Session, -10m, [], "10", Guid.NewGuid())).Succeeded);
        Assert.False((await workflow.AddManualCardTenderAsync(Session, 10m, [], "10", Guid.Empty)).Succeeded);
        Assert.False((await workflow.AddManualCardTenderAsync(Session, 10m,
            [new PaymentTender(PaymentMethodKind.Card, 5m)], "5", Guid.NewGuid())).Succeeded);
    }

    [Theory]
    [InlineData("MANUAL:corrupt")]
    [InlineData(" manual:00000000000000000000000000000001 ")]
    public async Task Integrated_refund_rejects_manual_references_before_calling_terminal(string reference)
    {
        var terminal = new RejectTerminal();
        var result = await CreateWorkflow(new Orders(), terminal).AddTenderAsync(
            PaymentMethodKind.Card, Session, -10m, [], "10", reference);
        Assert.False(result.Succeeded);
        Assert.Equal(0, terminal.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_save_retry_uses_one_order_after_failure_before_or_after_commit(bool commitBeforeThrow)
    {
        var orders = new Orders { FailNextSave = true, CommitBeforeThrow = commitBeforeThrow };
        var terminal = new RejectTerminal();
        var workflow = CreateWorkflow(orders, terminal);
        var cart = CreateCart();
        var id = Guid.NewGuid();
        var tender = (await workflow.AddManualCardTenderAsync(Session, 10m, [], "10", id)).Tender!;

        var error = await Assert.ThrowsAsync<CardPaymentPersistenceUnknownException>(() =>
            workflow.CompletePaymentAsync(cart, Session, [tender], 0m));
        Assert.Equal(id, error.OrderGuid);
        Assert.False(cart.IsEmpty);
        var result = await workflow.CompletePaymentAsync(cart, Session, [tender], 0m);

        Assert.Equal(id, result.Order.OrderGuid);
        Assert.Single(orders.Saved);
        Assert.Equal(commitBeforeThrow ? 1 : 2, orders.SaveCalls);
        Assert.Same(orders.Saved[0], result.Order);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, terminal.Calls);
    }

    [Fact]
    public async Task Manual_retry_rejects_changed_cart_and_preserves_original_order()
    {
        var orders = new Orders { FailNextSave = true, CommitBeforeThrow = true };
        var workflow = CreateWorkflow(orders);
        var cart = CreateCart();
        var id = Guid.NewGuid();
        var tender = (await workflow.AddManualCardTenderAsync(Session, 10m, [], "10", id)).Tender!;
        await Assert.ThrowsAsync<CardPaymentPersistenceUnknownException>(() => workflow.CompletePaymentAsync(cart, Session, [tender], 0m));

        var changedCart = CreateCart("DIFFERENT-SKU");
        await Assert.ThrowsAsync<CardPaymentPersistenceUnknownException>(() => workflow.CompletePaymentAsync(changedCart, Session, [tender], 0m));

        Assert.Equal("SKU", Assert.Single(Assert.Single(orders.Saved).Lines).ProductCode);
        Assert.Equal(1, orders.SaveCalls);
        Assert.False(changedCart.IsEmpty);
    }

    [Fact]
    public async Task Manual_identity_survives_sqlite_persistence()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-manual-{Guid.NewGuid():N}.db");
        try
        {
            var store = new LocalSqliteStore(databasePath);
            await new LocalSchemaService(store).InitializeAsync();
            var repository = new LocalOrderRepository(store);
            var workflow = new CashPaymentWorkflowService(new CashCheckoutService(), repository, new SyncQueueRepository(store));
            var id = Guid.NewGuid();
            var tender = (await workflow.AddManualCardTenderAsync(Session, 10m, [], "10", id)).Tender!;
            await workflow.CompletePaymentAsync(CreateCart(), Session, [tender], 0m);
            var saved = await repository.GetOrderAsync(id);
            var payment = Assert.Single(saved!.Payments);
            Assert.Equal(tender.Reference, payment.Reference);
            Assert.Equal(tender.IdempotencyKey, payment.IdempotencyKey);
            Assert.Equal("Manual", Assert.Single(payment.CardTransactions!).Processor);
            var retry = await workflow.CompletePaymentAsync(CreateCart(), Session, [tender], 0m);
            Assert.Equal(payment.PaymentGuid, Assert.Single(retry.Order.Payments).PaymentGuid);
            Assert.Equal(1, await new SyncQueueRepository(store).CountPendingAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static CashPaymentWorkflowService CreateWorkflow(Orders orders, ICardTerminalClient? terminal = null) =>
        new(new CashCheckoutService(), orders, new Queue(), cardTerminalClient: terminal);

    private static PosCartService CreateCart(string productCode = "SKU")
    {
        var cart = new PosCartService();
        cart.AddItem(new SellableItemDto("S001", productCode, null, "Item", "123", productCode, "123", 10m,
            PriceSourceKind.StoreRetailPrice, "Store", 1m, DateTimeOffset.UtcNow));
        return cart;
    }

    private sealed class Orders : ILocalOrderRepository
    {
        public List<LocalOrder> Saved { get; } = [];
        public bool FailNextSave { get; set; }
        public bool CommitBeforeThrow { get; init; }
        public int SaveCalls { get; private set; }
        public Task SavePendingOrderAsync(LocalOrder order, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (FailNextSave)
            {
                FailNextSave = false;
                if (CommitBeforeThrow) Saved.Add(order);
                throw new IOException("Injected commit failure");
            }
            Saved.Add(order);
            return Task.CompletedTask;
        }
        public Task<LocalOrder?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.SingleOrDefault(order => order.OrderGuid == id));
        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(int take = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);
        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(LocalOrderHistoryQuery query, int take = 50, CancellationToken cancellationToken = default) =>
            GetRecentOrdersAsync(take, cancellationToken);
    }

    private sealed class Queue : ISyncQueueRepository
    {
        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<SyncQueueOverview> GetOverviewAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SyncQueueOverview(1, 0, 0, null));
        public Task<IReadOnlyList<SyncQueueListItem>> GetActiveItemsAsync(int take = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SyncQueueListItem>>([]);
    }

    private sealed class RejectTerminal : ICardTerminalClient
    {
        public int Calls { get; private set; }
        public Task<PaymentAuthorizationResult> AuthorizeAsync(decimal amount, PosSessionState session, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Manual payments must not call the terminal");
        }
        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, string? reference = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Manual refunds must not call the terminal");
        }
    }
}
