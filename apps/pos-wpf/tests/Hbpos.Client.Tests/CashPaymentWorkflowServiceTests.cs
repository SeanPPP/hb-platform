using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Linkly;
using Hbpos.Contracts.Orders;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Hbpos.Client.Tests;

public sealed class CashPaymentWorkflowServiceTests
{
    [Fact]
    public void Cash_payment_workflow_rounds_cash_due_and_change_for_7_82()
    {
        var workflow = CreateWorkflow();

        var parsed = workflow.TryParseTenderedAmount("10", out var tenderedAmount);
        var remaining = CashRoundingPolicy.GetCashPayableAmount(7.82m, []);
        var change = workflow.CalculateChange("10", 7.82m);

        Assert.True(parsed);
        Assert.Equal(10m, tenderedAmount);
        Assert.Equal(7.80m, remaining);
        Assert.Equal(2.20m, change);
    }

    [Fact]
    public void Cash_payment_workflow_rounds_cash_due_and_change_for_7_83()
    {
        var workflow = CreateWorkflow();

        var remaining = CashRoundingPolicy.GetCashPayableAmount(7.83m, []);
        var change = workflow.CalculateChange("10", 7.83m);

        Assert.Equal(7.85m, remaining);
        Assert.Equal(2.15m, change);
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
    public async Task Cash_payment_workflow_runs_blocking_local_save_off_the_calling_thread()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-UI-THREAD", "Thread Check Tea", "930UITHREAD", 4.4m));
        var orders = new RecordingOrderRepository
        {
            SaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            ContinueSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 0));
        var callingThreadId = Environment.CurrentManagedThreadId;

        var completion = workflow.CompleteAsync(
            cart,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            "5");
        await orders.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEqual(callingThreadId, orders.SaveThreadId);
        Assert.NotEmpty(cart.Lines);
        orders.ContinueSave.SetResult();

        await completion;
        Assert.Empty(cart.Lines);
    }

    [Fact]
    public async Task Cash_payment_workflow_returns_completed_warning_when_pending_sync_refresh_fails()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-301W", "Workflow Warning Tea", "930301W", 4.4m));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 7);
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new ThrowingPendingSyncQueueRepository());

        var result = await workflow.CompleteAsync(cart, session, "5");

        Assert.True(result.HasPostCommitWarning);
        Assert.Equal(7, result.PendingSyncCount);
        Assert.Equal(7, result.UpdatedSession.PendingSyncCount);
        Assert.Empty(cart.Lines);
    }

    [Fact]
    public async Task Cash_payment_workflow_returns_completed_warning_when_cart_clear_notification_fails()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-301C", "Cart Warning Tea", "930301C", 4.4m));
        cart.CartChanged += (_, _) => throw new InvalidOperationException("cart listener failed");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0));

        var result = await workflow.CompleteAsync(
            cart,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            "5");

        Assert.True(result.HasPostCommitWarning);
        Assert.Empty(cart.Lines);
    }

    [Fact]
    public async Task Cash_payment_workflow_returns_completed_warning_when_card_attempt_marking_fails()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-301A", "Attempt Warning Tea", "930301A", 10m));
        var attemptGuid = Guid.NewGuid();
        var attempts = new RecordingCardPaymentAttemptRepository
        {
            MarkOrderCompletedException = new InvalidOperationException("attempt write failed")
        };
        await attempts.CreateAsync(new LocalCardPaymentAttempt(
            attemptGuid,
            null,
            "TXN-WARN",
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            10m,
            LocalCardPaymentAttemptStatus.Approved,
            "{}",
            "S001",
            "POS-01",
            "C001",
            null,
            null,
            "ANZ:TXN-WARN",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null));
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardPaymentAttemptRepository: attempts);

        var result = await workflow.CompletePaymentAsync(
            cart,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            [new PaymentTender(
                PaymentMethodKind.Card,
                10m,
                "ANZ:TXN-WARN",
                IdempotencyKey: $"CARD_ATTEMPT:{attemptGuid:N}")],
            cashTenderedAmount: 0m);

        Assert.True(result.HasPostCommitWarning);
        Assert.Empty(cart.Lines);
        Assert.Single(result.Order.Payments);
    }

    [Fact]
    public async Task Cash_payment_workflow_persists_rounded_cash_order_without_overstating_local_payment()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-302", "Workflow Soda", "930302", 7.82m));
        var orders = new RecordingOrderRepository();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 2));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var result = await workflow.CompleteAsync(cart, session, "10");

        var savedOrder = Assert.Single(orders.SavedOrders);
        var payment = Assert.Single(savedOrder.Payments);
        Assert.Equal(10m, result.TenderedAmount);
        Assert.Equal(2.20m, result.ChangeAmount);
        Assert.Equal(PaymentMethodKind.Cash, payment.Method);
        Assert.Equal(7.82m, payment.Amount);
    }

    [Fact]
    public async Task Card_tender_persists_linkly_backend_session_and_txn_ref_immediately_after_authorization()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-397", "Recoverable Card Latte", "930397", 10m));
        var orders = new RecordingOrderRepository();
        var attempts = new RecordingCardPaymentAttemptRepository();
        var linklyAttemptContextAccessor = new LinklyPaymentAttemptContextAccessor();
        var authorization = new PaymentAuthorizationResult(
            true,
            "ANZBACKEND:TXN-EARLY:session=backend-session-early:environment=Sandbox",
            "APPROVED",
            10m,
            [
                new CardTransactionDto(
                    "ANZ",
                    "TXN-EARLY",
                    null,
                    null,
                    null,
                    null,
                    null,
                    "00",
                    "APPROVED",
                    null,
                    DateTimeOffset.UtcNow,
                    10m,
                    null)
            ],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.CloudBackendAsync.ToString(),
            "P",
            "backend-session-early",
            "TXN-EARLY",
            "00",
            "APPROVED");
        var terminal = new BindingCardTerminalClient(
            linklyAttemptContextAccessor,
            authorization,
            beforeBind: () =>
            {
                var attempt = Assert.Single(attempts.Attempts);
                Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempt.Status);
                Assert.Null(attempt.SessionId);
                Assert.Null(attempt.TxnRef);
            },
            afterBind: () =>
            {
                var attempt = Assert.Single(attempts.Attempts);
                Assert.Equal("backend-session-early", attempt.SessionId);
                Assert.Equal("TXN-EARLY", attempt.TxnRef);
                Assert.Equal(LocalCardPaymentAttemptStatus.SessionStarted, attempt.Status);
            });
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: linklyAttemptContextAccessor);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());

        Assert.True(tenderResult.Succeeded);
        var attempt = Assert.Single(attempts.Attempts);
        Assert.Equal("backend-session-early", attempt.SessionId);
        Assert.Equal("TXN-EARLY", attempt.TxnRef);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempt.Status);
        Assert.Equal("ANZBACKEND:TXN-EARLY:session=backend-session-early:environment=Sandbox", attempt.PaymentReference);
        Assert.Empty(orders.SavedOrders);
    }

    [Fact]
    public async Task Card_tender_result_unknown_keeps_local_attempt_recoverable()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-397U", "Unknown Card Latte", "930397U", 10m));
        var orders = new RecordingOrderRepository();
        var attempts = new RecordingCardPaymentAttemptRepository();
        var linklyAttemptContextAccessor = new LinklyPaymentAttemptContextAccessor();
        var authorization = new PaymentAuthorizationResult(
            false,
            Message: "ANZ Linkly Cloud transaction timed out. Result unknown.",
            Processor: "ANZ",
            Environment: "Sandbox",
            ConnectionMode: LinklyConnectionMode.CloudBackendAsync.ToString(),
            TxnType: "P",
            SessionId: "backend-session-unknown",
            TxnRef: "TXN-UNKNOWN",
            StatusKey: "linkly.backend.resultUnknown",
            ResultUnknown: true);
        var terminal = new BindingCardTerminalClient(
            linklyAttemptContextAccessor,
            authorization,
            beforeBind: () => { },
            afterBind: () => { });
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: linklyAttemptContextAccessor);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        var attempt = Assert.Single(attempts.Attempts);
        Assert.Equal("backend-session-unknown", attempt.SessionId);
        Assert.Equal("TXN-UNKNOWN", attempt.TxnRef);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempt.Status);
        Assert.Null(attempt.CompletedAt);
        Assert.Empty(orders.SavedOrders);
    }

    [Fact]
    public async Task Card_tender_cancelled_before_linkly_terminal_submission_is_cancelled_and_unlocked()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-CANCEL-PRE", "Cancelled Before Submit", "930CANCELPRE", 10m));
        var attempts = new RecordingCardPaymentAttemptRepository();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new CancelledCardTerminalClient(),
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: new LinklyPaymentAttemptContextAccessor());

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cancellationToken: cancellation.Token,
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.status.cardCancelled", tenderResult.StatusKey);
        Assert.Equal(CardPaymentTerminalOutcome.Cancelled, tenderResult.CardResult?.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Cancelled, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Card_tender_cancelled_with_local_linkly_txn_ref_requires_recovery()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-CANCEL-LOCAL", "Cancelled Local Linkly", "930CANCELLOCAL", 10m));
        var attempts = new RecordingCardPaymentAttemptRepository();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new CancelledCardTerminalClient(),
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateLocalLinklySettings()),
            linklyPaymentAttemptContextAccessor: new LinklyPaymentAttemptContextAccessor());

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cancellationToken: cancellation.Token,
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        var attempt = Assert.Single(attempts.Attempts);
        Assert.False(string.IsNullOrWhiteSpace(attempt.TxnRef));
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempt.Status);
    }

    [Fact]
    public async Task Card_tender_cancelled_when_attempt_lookup_fails_requires_recovery()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-CANCEL-READ", "Cancelled Attempt Read", "930CANCELREAD", 10m));
        var attempts = new RecordingCardPaymentAttemptRepository
        {
            GetAttemptException = new InvalidOperationException("local read failed")
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new CancelledCardTerminalClient(),
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: new LinklyPaymentAttemptContextAccessor());

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cancellationToken: cancellation.Token,
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        Assert.Single(attempts.Attempts);
    }

    [Fact]
    public async Task Card_tender_cancelled_after_linkly_session_starts_requires_recovery()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-CANCEL-LINKLY", "Cancelled Linkly Session", "930CANCELLINKLY", 10m));
        var attempts = new RecordingCardPaymentAttemptRepository();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new CancellingCardTerminalClient(async cancellationToken =>
            {
                var context = accessor.Current;
                Assert.NotNull(context);
                await context!.BindSessionAsync("session-after-submit", "TXN-CANCEL", DateTimeOffset.UtcNow, cancellationToken);
            }),
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: accessor);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Card_tender_cancelled_after_square_checkout_created_requires_recovery()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-CANCEL-SQUARE", "Cancelled Square Checkout", "930CANCELSQUARE", 10m));
        var attempts = new RecordingSquarePaymentAttemptRepository();
        var accessor = new SquarePaymentAttemptContextAccessor();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new CancellingCardTerminalClient(async cancellationToken =>
            {
                var context = accessor.Current;
                Assert.NotNull(context);
                await attempts.MarkCheckoutCreatedAsync(
                    context!.AttemptGuid,
                    "checkout-after-submit",
                    "PENDING",
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }),
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: attempts,
            squarePaymentAttemptContextAccessor: accessor);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Card_tender_terminal_exception_after_linkly_submission_requires_recovery()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-TERMINAL-EXCEPTION", "Terminal Exception Tea", "930TERMINALEX", 10m));
        var attempts = new RecordingCardPaymentAttemptRepository();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new ThrowingCardTerminalClient(async cancellationToken =>
            {
                var context = accessor.Current;
                Assert.NotNull(context);
                await context!.BindSessionAsync("session-after-submit", "TXN-EXCEPTION", DateTimeOffset.UtcNow, cancellationToken);
            }),
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: accessor);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Card_tender_terminal_exception_before_submission_marks_attempt_failed_and_throws()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-TERMINAL-FAILED", "Terminal Failed Tea", "930TERMINALFAIL", 10m));
        var attempts = new RecordingCardPaymentAttemptRepository();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new ThrowingCardTerminalClient(),
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: new LinklyPaymentAttemptContextAccessor());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot()));

        Assert.Equal("terminal transport failed", exception.Message);
        Assert.Equal(LocalCardPaymentAttemptStatus.Failed, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Square_card_tender_terminal_exception_after_checkout_requires_recovery()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-SQUARE-EXCEPTION", "Square Exception Tea", "930SQUAREEX", 10m));
        var attempts = new RecordingSquarePaymentAttemptRepository();
        var accessor = new SquarePaymentAttemptContextAccessor();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new ThrowingCardTerminalClient(async cancellationToken =>
            {
                var context = accessor.Current;
                Assert.NotNull(context);
                await attempts.MarkCheckoutCreatedAsync(
                    context!.AttemptGuid,
                    "checkout-after-exception",
                    "PENDING",
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }),
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: attempts,
            squarePaymentAttemptContextAccessor: accessor);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Card_tender_linkly_submission_binding_ignores_caller_cancellation()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-LINKLY-BIND-CANCEL", "Linkly Binding Tea", "930LINKLYBIND", 10m));
        var attempts = new RecordingCardPaymentAttemptRepository { RejectCancelledTokens = true };
        var accessor = new LinklyPaymentAttemptContextAccessor();
        using var cancellation = new CancellationTokenSource();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new CancellingCardTerminalClient(async cancellationToken =>
            {
                cancellation.Cancel();
                var context = accessor.Current;
                Assert.NotNull(context);
                await context!.BindSessionAsync("session-bind-cancel", "TXN-BIND-CANCEL", DateTimeOffset.UtcNow, cancellationToken);
            }),
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: accessor);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cancellationToken: cancellation.Token,
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Square_card_tender_checkout_binding_ignores_caller_cancellation()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-SQUARE-BIND-CANCEL", "Square Binding Tea", "930SQUAREBIND", 10m));
        var attempts = new RecordingSquarePaymentAttemptRepository { RejectCancelledTokens = true };
        var accessor = new SquarePaymentAttemptContextAccessor();
        using var cancellation = new CancellationTokenSource();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new CancellingCardTerminalClient(async cancellationToken =>
            {
                cancellation.Cancel();
                var context = accessor.Current;
                Assert.NotNull(context);
                var bindCheckout = Assert.IsType<Func<string, string?, DateTimeOffset, CancellationToken, Task>>(context!.BindCheckoutAsync);
                await bindCheckout("checkout-bind-cancel", "PENDING", DateTimeOffset.UtcNow, cancellationToken);
            }),
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: attempts,
            squarePaymentAttemptContextAccessor: accessor);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cancellationToken: cancellation.Token,
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Square_card_tender_checkout_binding_write_failure_requires_recovery()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-SQUARE-BIND-FAIL", "Square Binding Failure Tea", "930SQUAREBINDFAIL", 10m));
        var attempts = new RecordingSquarePaymentAttemptRepository
        {
            MarkCheckoutCreatedException = new InvalidOperationException("checkout persistence failed")
        };
        var accessor = new SquarePaymentAttemptContextAccessor();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new ThrowingCardTerminalClient(async cancellationToken =>
            {
                var context = accessor.Current;
                Assert.NotNull(context);
                var bindCheckout = Assert.IsType<Func<string, string?, DateTimeOffset, CancellationToken, Task>>(context!.BindCheckoutAsync);
                await bindCheckout("checkout-bind-fail", "PENDING", DateTimeOffset.UtcNow, cancellationToken);
            }),
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: attempts,
            squarePaymentAttemptContextAccessor: accessor);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
    }

    [Fact]
    public async Task Square_sale_checkout_binding_rejects_a_stale_worker_token()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-SQUARE-STALE-SALE", "Square Stale Sale Tea", "930SQUARESTALESALE", 10m));
        var attempts = new RecordingSquarePaymentAttemptRepository();
        var accessor = new SquarePaymentAttemptContextAccessor();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new ThrowingCardTerminalClient(async cancellationToken =>
            {
                var context = Assert.IsType<SquarePaymentAttemptContext>(accessor.Current);
                var persisted = Assert.Single(attempts.Attempts);
                Assert.False(string.IsNullOrWhiteSpace(context.SubmissionToken));
                Assert.Equal(persisted.SubmissionToken, context.SubmissionToken);
                attempts.SimulateSupervisorRetryAndNewClaim("replacement-sale-token");
                await context.BindCheckoutAsync!(
                    "stale-sale-checkout",
                    "PENDING",
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }),
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: attempts,
            squarePaymentAttemptContextAccessor: accessor);

        var result = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());

        var saved = Assert.Single(attempts.Attempts);
        Assert.False(result.Succeeded);
        Assert.Equal("payment.card.resultUnknown", result.StatusKey);
        Assert.True(result.CardResult?.RequiresRecovery);
        Assert.Equal("replacement-sale-token", saved.SubmissionToken);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, saved.Status);
        Assert.Null(saved.CheckoutId);
    }

    [Fact]
    public async Task Square_card_tender_unknown_local_attempt_result_requires_recovery()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-SQUARE-UNKNOWN", "Square Unknown Tea", "930SQUAREUNKNOWN", 10m));
        var attempts = new RecordingSquarePaymentAttemptRepository();
        var context = new SquarePaymentAttemptContextAccessor();
        var terminal = new AsyncObservingCardTerminalClient(async () =>
        {
            var attempt = Assert.Single(attempts.Attempts);
            await attempts.MarkFailedAsync(
                attempt.AttemptGuid,
                LocalSquarePaymentAttemptStatus.Unknown,
                null,
                null,
                null,
                "Square transport result was not confirmed.",
                DateTimeOffset.UtcNow);
        }, new PaymentAuthorizationResult(
            false,
            null,
            "Square transport result was not confirmed.",
            StatusKey: "payment.card.squareCommunicationFailed"));
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: terminal,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: attempts,
            squarePaymentAttemptContextAccessor: context);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Square_card_tender_explicit_checkout_cancellation_does_not_require_recovery()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-SQUARE-CANCELED", "Square Canceled Tea", "930SQUARECANCELED", 10m));
        var attempts = new RecordingSquarePaymentAttemptRepository();
        var terminal = new AsyncObservingCardTerminalClient(async () =>
        {
            var attempt = Assert.Single(attempts.Attempts);
            await attempts.MarkCheckoutCreatedAsync(
                attempt.AttemptGuid,
                "checkout-canceled",
                "CANCELED",
                DateTimeOffset.UtcNow);
            await attempts.MarkFailedAsync(
                attempt.AttemptGuid,
                LocalSquarePaymentAttemptStatus.Canceled,
                "CANCELED",
                null,
                null,
                "Customer canceled the Square payment.",
                DateTimeOffset.UtcNow);
        }, new PaymentAuthorizationResult(
            false,
            null,
            "Customer canceled the Square payment.",
            StatusKey: "payment.card.squareCanceled"));
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: terminal,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: attempts,
            squarePaymentAttemptContextAccessor: new SquarePaymentAttemptContextAccessor());

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.squareCanceled", tenderResult.StatusKey);
        Assert.False(tenderResult.CardResult?.RequiresRecovery);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Canceled, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Card_tender_authorized_amount_mismatch_marks_local_attempt_requires_review()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-398", "Mismatch Card Latte", "930398", 10m));
        var orders = new RecordingOrderRepository();
        var attempts = new RecordingCardPaymentAttemptRepository();
        var linklyAttemptContextAccessor = new LinklyPaymentAttemptContextAccessor();
        var authorization = new PaymentAuthorizationResult(
            true,
            "ANZBACKEND:TXN-MISMATCH:session=backend-session-mismatch:environment=Sandbox",
            "APPROVED",
            5m,
            [
                new CardTransactionDto(
                    "ANZ",
                    "TXN-MISMATCH",
                    null,
                    null,
                    null,
                    null,
                    null,
                    "00",
                    "APPROVED",
                    null,
                    DateTimeOffset.UtcNow,
                    5m,
                    null)
            ],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.CloudBackendAsync.ToString(),
            "P",
            "backend-session-mismatch",
            "TXN-MISMATCH",
            "00",
            "APPROVED");
        var terminal = new BindingCardTerminalClient(
            linklyAttemptContextAccessor,
            authorization,
            beforeBind: () =>
            {
                var attempt = Assert.Single(attempts.Attempts);
                Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempt.Status);
                Assert.Null(attempt.SessionId);
                Assert.Null(attempt.TxnRef);
            },
            afterBind: () =>
            {
                var attempt = Assert.Single(attempts.Attempts);
                Assert.Equal("backend-session-mismatch", attempt.SessionId);
                Assert.Equal("TXN-MISMATCH", attempt.TxnRef);
                Assert.Equal(LocalCardPaymentAttemptStatus.SessionStarted, attempt.Status);
            });
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: linklyAttemptContextAccessor);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        Assert.Equal("Card terminal authorized amount did not match the requested amount.", tenderResult.StatusMessage);
        var attempt = Assert.Single(attempts.Attempts);
        Assert.Equal("backend-session-mismatch", attempt.SessionId);
        Assert.Equal("TXN-MISMATCH", attempt.TxnRef);
        Assert.Equal(LocalCardPaymentAttemptStatus.RequiresReview, attempt.Status);
        Assert.Equal("Card terminal authorized amount did not match the requested amount.", attempt.ResponseText);
        Assert.Empty(orders.SavedOrders);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(5)]
    public async Task Square_card_tender_approved_invalid_amount_requires_recovery(decimal authorizedAmount)
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-SQUARE-AMOUNT", "Square Invalid Amount Tea", "930SQUAREAMOUNT", 10m));
        var attempts = new RecordingSquarePaymentAttemptRepository();
        var authorization = new PaymentAuthorizationResult(
            true,
            "SQ:invalid-amount",
            "APPROVED",
            authorizedAmount,
            [new CardTransactionDto("Square", "payment-invalid", null, null, null, null, null, "00", "APPROVED", null, DateTimeOffset.UtcNow, authorizedAmount, null)],
            ResponseCode: "00",
            ResponseText: "APPROVED");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new ObservingCardTerminalClient(() => { }, authorization),
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: attempts,
            squarePaymentAttemptContextAccessor: new SquarePaymentAttemptContextAccessor());

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());

        var expectedMessage = authorizedAmount <= 0m
            ? "Card terminal approved a non-positive amount. Supervisor review is required."
            : authorizedAmount > 10m
                ? "Card terminal authorized amount exceeded the remaining amount."
                : "Card terminal authorized amount did not match the requested amount.";
        Assert.False(tenderResult.Succeeded);
        Assert.Equal("payment.card.resultUnknown", tenderResult.StatusKey);
        Assert.True(tenderResult.CardResult?.RequiresRecovery);
        var attempt = Assert.Single(attempts.Attempts);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, attempt.Status);
        Assert.Equal(expectedMessage, attempt.ResponseText);
    }

    [Fact]
    public async Task Card_tender_approval_after_caller_cancellation_persists_attempt_outcome()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-CANCEL-APPROVED", "Canceled Approved Tea", "930CANCELAPPROVED", 10m));
        var attempts = new RecordingCardPaymentAttemptRepository { RejectCancelledTokens = true };
        var accessor = new LinklyPaymentAttemptContextAccessor();
        using var cancellation = new CancellationTokenSource();
        var authorization = new PaymentAuthorizationResult(
            true,
            "ANZBACKEND:TXN-CANCEL:session=session-cancel:environment=Sandbox",
            "APPROVED",
            10m,
            [new CardTransactionDto("ANZ", "TXN-CANCEL", null, null, null, null, null, "00", "APPROVED", null, DateTimeOffset.UtcNow, 10m, null)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.CloudBackendAsync.ToString(),
            "P",
            "session-cancel",
            "TXN-CANCEL",
            "00",
            "APPROVED");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new CancellingApprovedCardTerminalClient(accessor, cancellation, authorization),
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: accessor);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cancellationToken: cancellation.Token,
            cartSnapshot: cart.CreateSnapshot());

        Assert.True(tenderResult.Succeeded);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Card_tender_approved_attempt_persistence_failure_returns_result_unknown()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-APPROVED-PERSIST", "Approved Persist Tea", "930APPROVEDPERSIST", 10m));
        var attempts = new RecordingCardPaymentAttemptRepository
        {
            UpdateOutcomeException = new InvalidOperationException("attempt outcome write failed")
        };
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var authorization = new PaymentAuthorizationResult(
            true,
            "ANZBACKEND:TXN-PERSIST:session=session-persist:environment=Sandbox",
            "APPROVED",
            10m,
            [new CardTransactionDto("ANZ", "TXN-PERSIST", null, null, null, null, null, "00", "APPROVED", null, DateTimeOffset.UtcNow, 10m, null)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.CloudBackendAsync.ToString(),
            "P",
            "session-persist",
            "TXN-PERSIST",
            "00",
            "APPROVED");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new BindingCardTerminalClient(accessor, authorization, () => { }, () => { }),
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: accessor);

        var result = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(result.Succeeded);
        Assert.Equal("payment.card.resultUnknown", result.StatusKey);
        Assert.True(result.CardResult?.RequiresRecovery);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Card_tender_approved_order_persists_after_caller_cancellation()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-CANCEL-SAVE", "Canceled Save Tea", "930CANCELSAVE", 10m));
        var orders = new RecordingOrderRepository { RejectCancelledTokens = true };
        var attempts = new RecordingCardPaymentAttemptRepository { RejectCancelledTokens = true };
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var authorization = new PaymentAuthorizationResult(
            true,
            "ANZBACKEND:TXN-SAVE:session=session-save:environment=Sandbox",
            "APPROVED",
            10m,
            [new CardTransactionDto("ANZ", "TXN-SAVE", null, null, null, null, null, "00", "APPROVED", null, DateTimeOffset.UtcNow, 10m, null)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.CloudBackendAsync.ToString(),
            "P",
            "session-save",
            "TXN-SAVE",
            "00",
            "APPROVED");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: new BindingCardTerminalClient(accessor, authorization, () => { }, () => { }),
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: accessor);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var completion = await workflow.CompletePaymentAsync(
            cart,
            session,
            [tenderResult.Tender!],
            cashTenderedAmount: 0m,
            cancellationToken: cancellation.Token);

        Assert.Single(orders.SavedOrders);
        Assert.Equal(completion.Order.OrderGuid, orders.SavedOrders.Single().OrderGuid);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, Assert.Single(attempts.Attempts).Status);
    }

    [Theory]
    [InlineData("prepare-io")]
    [InlineData("save-sqlite-busy")]
    public async Task Approved_card_local_persistence_failure_requires_review_without_second_terminal_call(string failureStage)
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-APPROVED-ORDER-FAIL", "Approved Order Fail Tea", "930APPROVEDORDERFAIL", 10m));
        var orders = new RecordingOrderRepository();
        var attempts = new RecordingCardPaymentAttemptRepository();
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var authorization = new PaymentAuthorizationResult(
            true,
            "ANZBACKEND:TXN-ORDER-FAIL:session=session-order-fail:environment=Sandbox",
            "APPROVED",
            10m,
            [new CardTransactionDto("ANZ", "TXN-ORDER-FAIL", null, null, null, null, null, "00", "APPROVED", null, DateTimeOffset.UtcNow, 10m, null)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.CloudBackendAsync.ToString(),
            "P",
            "session-order-fail",
            "TXN-ORDER-FAIL",
            "00",
            "APPROVED");
        var terminal = new BindingCardTerminalClient(accessor, authorization, () => { }, () => { });
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 0),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: accessor);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cartSnapshot: cart.CreateSnapshot());
        Assert.True(tenderResult.Succeeded);
        Assert.Equal(1, terminal.AuthorizeCallCount);

        if (failureStage == "prepare-io")
        {
            attempts.GetAttemptException = new IOException("attempt read failed");
        }
        else
        {
            orders.SaveException = new SqliteException("database is locked", 5);
        }

        var exception = await Assert.ThrowsAsync<CardPaymentPersistenceUnknownException>(() =>
            workflow.CompletePaymentAsync(
                cart,
                session,
                [tenderResult.Tender!],
                cashTenderedAmount: 0m));

        Assert.Equal(1, terminal.AuthorizeCallCount);
        Assert.IsType(
            failureStage == "prepare-io" ? typeof(IOException) : typeof(SqliteException),
            exception.InnerException);
        var attempt = Assert.Single(attempts.Attempts);
        Assert.Equal(LocalCardPaymentAttemptStatus.RequiresReview, attempt.Status);
        Assert.Equal(tenderResult.Tender!.Reference, attempt.PaymentReference);
        Assert.Empty(orders.SavedOrders);
        Assert.Single(cart.Lines);
    }

    [Fact]
    public async Task Card_tender_creates_local_attempt_before_terminal_request_and_marks_order_completed_after_save()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-399", "Recoverable Card Tea", "930399", 10m));
        var orders = new RecordingOrderRepository();
        var attempts = new RecordingCardPaymentAttemptRepository();
        var linklyAttemptContextAccessor = new LinklyPaymentAttemptContextAccessor();
        var terminal = new BindingCardTerminalClient(
            linklyAttemptContextAccessor,
            new PaymentAuthorizationResult(
                true,
                "ANZBACKEND:TXN-1:session=backend-session-1:environment=Sandbox",
                "APPROVED",
                10m,
                [
                    new CardTransactionDto(
                        "ANZ",
                        "TXN-1",
                        null,
                        null,
                        null,
                        null,
                        null,
                        "00",
                        "APPROVED",
                        null,
                        DateTimeOffset.UtcNow,
                        10m,
                        null)
                ],
                "ANZ",
                "Sandbox",
                LinklyConnectionMode.CloudBackendAsync.ToString(),
                "P",
                "backend-session-1",
                "TXN-1",
                "00",
                "APPROVED"),
            beforeBind: () =>
            {
                var attempt = Assert.Single(attempts.Attempts);
                Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempt.Status);
                Assert.Contains("\"cardAmount\":10", attempt.OrderDraftJson, StringComparison.OrdinalIgnoreCase);
            },
            afterBind: () =>
            {
                var attempt = Assert.Single(attempts.Attempts);
                Assert.Equal("backend-session-1", attempt.SessionId);
                Assert.Equal("TXN-1", attempt.TxnRef);
                Assert.Equal(LocalCardPaymentAttemptStatus.SessionStarted, attempt.Status);
            });
        var backend = new RecordingLinklyBackendTerminalClient();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()),
            linklyPaymentAttemptContextAccessor: linklyAttemptContextAccessor,
            linklyBackendTerminalClient: backend);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());
        var completion = await workflow.CompletePaymentAsync(
            cart,
            session,
            [tenderResult.Tender!],
            cashTenderedAmount: 0m);

        Assert.True(tenderResult.Succeeded);
        Assert.Equal("backend-session-1", attempts.Attempts.Single().SessionId);
        Assert.Equal("TXN-1", attempts.Attempts.Single().TxnRef);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Attempts.Single().Status);
        Assert.NotNull(attempts.Attempts.Single().AcknowledgedAt);
        Assert.Equal("backend-session-1", backend.AcknowledgedSessionId);
        Assert.Equal(CardTerminalEnvironment.Sandbox, backend.AcknowledgedSettings?.Environment);
        var draft = JsonSerializer.Deserialize<CardPaymentOrderDraft>(
            attempts.Attempts.Single().OrderDraftJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(draft!.OrderGuid, completion.Order.OrderGuid);
        Assert.Equal("ANZBACKEND:TXN-1:session=backend-session-1:environment=Sandbox", completion.Order.Payments.Single().Reference);
    }

    [Fact]
    public async Task Local_ip_card_tender_creates_recoverable_attempt_before_terminal_request_and_reuses_txn_ref()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-397", "Local Recoverable Tea", "930397", 10m));
        var orders = new RecordingOrderRepository();
        var attempts = new RecordingCardPaymentAttemptRepository();
        var linklyAttemptContextAccessor = new LinklyPaymentAttemptContextAccessor();
        var terminal = new LocalReferenceCardTerminalClient(
            linklyAttemptContextAccessor,
            () =>
            {
                var attempt = Assert.Single(attempts.Attempts);
                Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempt.Status);
                Assert.Equal(LinklyConnectionMode.LocalIp.ToString(), attempt.ConnectionMode);
                Assert.False(string.IsNullOrWhiteSpace(attempt.TxnRef));
                Assert.Contains("\"cardAmount\":10", attempt.OrderDraftJson, StringComparison.OrdinalIgnoreCase);
            });
        var backend = new RecordingLinklyBackendTerminalClient();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateLocalLinklySettings()),
            linklyPaymentAttemptContextAccessor: linklyAttemptContextAccessor,
            linklyBackendTerminalClient: backend);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());
        var completion = await workflow.CompletePaymentAsync(
            cart,
            session,
            [tenderResult.Tender!],
            cashTenderedAmount: 0m);

        var attemptAfterCompletion = Assert.Single(attempts.Attempts);
        Assert.True(tenderResult.Succeeded);
        Assert.Equal(attemptAfterCompletion.TxnRef, terminal.SeenTxnRef);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attemptAfterCompletion.Status);
        Assert.Null(attemptAfterCompletion.AcknowledgedAt);
        Assert.Null(backend.AcknowledgedSessionId);
        Assert.Equal($"ANZ:{attemptAfterCompletion.TxnRef}", completion.Order.Payments.Single().Reference);
    }

    [Fact]
    public async Task Square_card_tender_creates_dedicated_attempt_and_marks_order_completed_after_save()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-398", "Square Recoverable Tea", "930398", 10m));
        var orders = new RecordingOrderRepository();
        var squareAttempts = new RecordingSquarePaymentAttemptRepository();
        var squareContext = new SquarePaymentAttemptContextAccessor();
        var terminal = new ObservingCardTerminalClient(() =>
        {
            var attempt = Assert.Single(squareAttempts.Attempts);
            Assert.Equal(LocalSquarePaymentAttemptStatus.Pending, attempt.Status);
            Assert.Contains("\"cardAmount\":10", attempt.OrderDraftJson, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(attempt.AttemptGuid, squareContext.Current?.AttemptGuid);
            Assert.Equal(attempt.IdempotencyKey, squareContext.Current?.IdempotencyKey);
        }, new PaymentAuthorizationResult(
            true,
            "SQ:payment-1",
            "Square",
            10m,
            [new CardTransactionDto("Square", "payment-1", null, null, null, null, null, null, "COMPLETED", null, DateTimeOffset.UtcNow, 10m, null)]));
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: squareAttempts,
            squarePaymentAttemptContextAccessor: squareContext);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());
        var completion = await workflow.CompletePaymentAsync(
            cart,
            session,
            [tenderResult.Tender!],
            cashTenderedAmount: 0m);

        Assert.True(tenderResult.Succeeded);
        var savedAttempt = Assert.Single(squareAttempts.Attempts);
        Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, savedAttempt.Status);
        Assert.Equal("SQUARE_ATTEMPT:", tenderResult.Tender!.IdempotencyKey![..15]);
        var draft = JsonSerializer.Deserialize<CardPaymentOrderDraft>(
            savedAttempt.OrderDraftJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(draft!.OrderGuid, completion.Order.OrderGuid);
        Assert.Equal("SQ:payment-1", completion.Order.Payments.Single().Reference);
    }

    [Theory]
    [InlineData(
        "payment.card.squareTimedOut",
        "Square checkout timed out before the customer completed payment.",
        LocalSquarePaymentAttemptStatus.TimedOut)]
    [InlineData(
        "payment.card.squareTerminalNotPickedUp",
        "Square terminal did not pick up this checkout.",
        LocalSquarePaymentAttemptStatus.TimedOut)]
    [InlineData(
        "payment.card.squareCanceled",
        "Square checkout was not completed. Please try again.",
        LocalSquarePaymentAttemptStatus.Canceled)]
    [InlineData(
        "payment.card.squareCanceledBuyer",
        "Customer canceled the Square payment.",
        LocalSquarePaymentAttemptStatus.Canceled)]
    [InlineData(
        "payment.card.squareCanceledSeller",
        "Square checkout was canceled.",
        LocalSquarePaymentAttemptStatus.Canceled)]
    [InlineData(
        "payment.card.squareTerminalOffline",
        "Square terminal is offline. Check the terminal network and try again.",
        LocalSquarePaymentAttemptStatus.Failed)]
    public async Task Square_card_tender_preserves_friendly_failure_status_on_local_attempt(
        string statusKey,
        string message,
        LocalSquarePaymentAttemptStatus expectedAttemptStatus)
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-397", "Square Failure Tea", "930397", 10m));
        var squareAttempts = new RecordingSquarePaymentAttemptRepository();
        var terminal = new ObservingCardTerminalClient(() => { }, new PaymentAuthorizationResult(
            false,
            null,
            message,
            StatusKey: statusKey));
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: squareAttempts,
            squarePaymentAttemptContextAccessor: new SquarePaymentAttemptContextAccessor());
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(tenderResult.Succeeded);
        Assert.Equal(statusKey, tenderResult.StatusKey);
        Assert.Equal(message, tenderResult.StatusMessage);
        var savedAttempt = Assert.Single(squareAttempts.Attempts);
        Assert.Equal(expectedAttemptStatus, savedAttempt.Status);
        Assert.Equal(message, savedAttempt.ResponseText);
    }

    [Fact]
    public async Task Card_tender_fallback_success_tells_supervisor_to_change_primary_mode_in_settings()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-399", "Fallback Card Tea", "930399", 10m));
        var orders = new RecordingOrderRepository();
        var terminal = new ObservingCardTerminalClient(() => { }, new PaymentAuthorizationResult(
            true,
            "ANZCLOUD:DIRECT-FALLBACK",
            "APPROVED",
            10m,
            Processor: "ANZ",
            Environment: "Sandbox",
            ConnectionMode: LinklyConnectionMode.CloudDirectSync.ToString(),
            RequestedConnectionMode: LinklyConnectionMode.CloudBackendAsync.ToString(),
            ActualConnectionMode: LinklyConnectionMode.CloudDirectSync.ToString(),
            FallbackAttemptedModes:
            [
                LinklyConnectionMode.CloudBackendAsync.ToString(),
                LinklyConnectionMode.CloudDirectSync.ToString()
            ],
            FallbackSucceeded: true));
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateBackendLinklySettings()));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tenderResult = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());

        Assert.True(tenderResult.Succeeded);
        Assert.Equal("payment.linklyFallback.succeeded", tenderResult.StatusKey);
        Assert.Contains("Cloud backend async", tenderResult.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Cloud direct sync", tenderResult.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("CloudBackendAsync", tenderResult.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("CloudDirectSync", tenderResult.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Settings", tenderResult.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cash_payment_workflow_keeps_local_payment_total_aligned_when_cash_rounds_down()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-304", "Rounded Down Soda", "930304", 7.82m));
        var orders = new RecordingOrderRepository();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 2));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var result = await workflow.CompletePaymentAsync(
            cart,
            session,
            [new PaymentTender(PaymentMethodKind.Cash, 7.80m)],
            cashTenderedAmount: 7.80m);

        var savedOrder = Assert.Single(orders.SavedOrders);
        var payment = Assert.Single(savedOrder.Payments);
        Assert.Equal(7.80m, result.TenderedAmount);
        Assert.Equal(0m, result.ChangeAmount);
        Assert.Equal(7.82m, payment.Amount);
    }

    [Fact]
    public async Task Payment_workflow_allocates_cash_change_without_overstating_local_payments()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-303", "Workflow Soda", "930303", 7.83m));
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
                new PaymentTender(PaymentMethodKind.Cash, 2.85m)
            ],
            cashTenderedAmount: 2.85m);

        var savedOrder = Assert.Single(orders.SavedOrders);
        Assert.Equal(7.85m, result.TenderedAmount);
        Assert.Equal(0m, result.ChangeAmount);
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
                Assert.Equal(2.83m, payment.Amount);
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
    public async Task Payment_workflow_add_tender_normalizes_cash_input()
    {
        var workflow = CreateWorkflow();
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var roundedDown = await workflow.AddTenderAsync(
            PaymentMethodKind.Cash,
            session,
            actualAmount: 20m,
            currentTenders: [],
            amountText: "10.02");
        var roundedUp = await workflow.AddTenderAsync(
            PaymentMethodKind.Cash,
            session,
            actualAmount: 20m,
            currentTenders: [],
            amountText: "10.03");

        Assert.True(roundedDown.Succeeded);
        Assert.NotNull(roundedDown.Tender);
        Assert.Equal(10.00m, roundedDown.Tender.Amount);
        Assert.True(roundedUp.Succeeded);
        Assert.NotNull(roundedUp.Tender);
        Assert.Equal(10.05m, roundedUp.Tender.Amount);
    }

    [Fact]
    public void Payment_workflow_uses_cash_rounding_after_non_cash_tender()
    {
        var workflow = CreateWorkflow();
        var remaining = CashRoundingPolicy.GetCashPayableAmount(
            7.83m,
            [new PaymentTender(PaymentMethodKind.Card, 5m, "CARD-001")]);

        Assert.Equal(2.85m, remaining);
    }

    [Fact]
    public async Task Payment_workflow_does_not_round_down_pure_non_cash_underpayment()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-305", "Card Boundary Tea", "930305", 7.82m));
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: new ApprovedCardTerminalClient("CARD-305"));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var remaining = workflow.CalculateRemainingAmount(
            7.82m,
            [new PaymentTender(PaymentMethodKind.Card, 7.80m, "CARD-305")]);

        Assert.Equal(0.02m, remaining);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.CompletePaymentAsync(
            cart,
            session,
            [new PaymentTender(PaymentMethodKind.Card, 7.80m, "CARD-305")],
            cashTenderedAmount: 0m));
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
    public async Task Payment_workflow_releases_voucher_reservation_from_tender_reference()
    {
        var vouchers = new ApprovedVoucherTenderClient("VOUCHER:VC100:LOCK-1:15.00");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            voucherTenderClient: vouchers);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var released = await workflow.ReleaseVoucherTenderAsync(
            new PaymentTender(PaymentMethodKind.Voucher, 5m, "VOUCHER:VC100:LOCK-1:15.00"),
            session);

        Assert.True(released);
        Assert.Equal(1, vouchers.ReleaseCallCount);
        Assert.Equal("VC100", vouchers.LastReleaseVoucherCode);
        Assert.Equal("LOCK-1", vouchers.LastReleaseReservationToken);
    }

    [Fact]
    public async Task Payment_workflow_returns_false_when_voucher_release_throws()
    {
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            voucherTenderClient: new ThrowingVoucherTenderClient());
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var released = await workflow.ReleaseVoucherTenderAsync(
            new PaymentTender(PaymentMethodKind.Voucher, 5m, "VOUCHER:VC100:LOCK-1:15.00"),
            session);

        Assert.False(released);
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

    [Fact]
    public async Task Payment_workflow_adds_negative_cash_tender_for_refund()
    {
        var workflow = CreateWorkflow();
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tender = await workflow.AddTenderAsync(
            PaymentMethodKind.Cash,
            session,
            actualAmount: -7.82m,
            currentTenders: [],
            amountText: "7.82");

        Assert.True(tender.Succeeded);
        Assert.NotNull(tender.Tender);
        Assert.Equal(-7.80m, tender.Tender.Amount);
    }

    [Fact]
    public async Task Payment_workflow_blocks_partial_card_before_terminal_authorization()
    {
        var cardTerminal = new ApprovedCardTerminalClient("CARD-PARTIAL");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: cardTerminal);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tender = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            actualAmount: 10m,
            currentTenders: [],
            amountText: "5");

        Assert.False(tender.Succeeded);
        Assert.Equal("payment.status.cardMustBeFinalTender", tender.StatusKey);
        Assert.Equal(0, cardTerminal.AuthorizeCallCount);
    }

    [Fact]
    public async Task Payment_workflow_adds_negative_card_tender_for_refund()
    {
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: new ApprovedCardTerminalClient("CARD-REFUND"));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tender = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            actualAmount: -10m,
            currentTenders: [],
            amountText: "4",
            referenceText: "SQ:payment-1");

        Assert.True(tender.Succeeded);
        Assert.NotNull(tender.Tender);
        Assert.Equal(-4m, tender.Tender.Amount);
        Assert.True(CardRefundReference.TryGetOriginalReference(tender.Tender.Reference, out var originalReference));
        Assert.Equal("SQ:payment-1", originalReference);
        Assert.Equal("REFUND:SQ:payment-1", CardRefundReference.GetDisplayReference(tender.Tender.Reference));
    }

    [Fact]
    public async Task Card_refund_persists_new_linkly_txn_ref_and_passes_it_as_idempotency_key()
    {
        var cart = CreateReturnCart(4m);
        var attempts = new RecordingCardPaymentAttemptRepository();
        var terminal = new RecordingIdempotentCardRefundClient(
            new PaymentAuthorizationResult(true, "ANZ:REFUND-1", "APPROVED", AuthorizedAmount: 4m));
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateLocalLinklySettings()));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tender = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            actualAmount: -4m,
            currentTenders: [],
            amountText: "4",
            referenceText: "ANZ:SALE-1",
            cartSnapshot: cart.CreateSnapshot());

        var attempt = Assert.Single(attempts.Attempts);
        var draft = JsonSerializer.Deserialize<CardPaymentOrderDraft>(
            attempt.OrderDraftJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(tender.Succeeded);
        Assert.Equal("Refund", attempt.OperationKind);
        Assert.Equal("R", attempt.TxnType);
        Assert.False(string.IsNullOrWhiteSpace(attempt.TxnRef));
        Assert.NotEqual("SALE-1", attempt.TxnRef);
        Assert.Equal(attempt.TxnRef, terminal.LastIdempotencyKey);
        Assert.Equal("ANZ:SALE-1", terminal.LastOriginalReference);
        Assert.Equal("ANZ:SALE-1", draft!.OriginalReference);
        Assert.Equal(draft.OrderGuid, attempt.OperationGuid);
    }

    [Fact]
    public async Task Square_refund_reuses_pending_attempt_idempotency_key_after_restart()
    {
        var cart = CreateReturnCart(4m);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);
        var now = DateTimeOffset.UtcNow;
        var draft = new CardPaymentOrderDraft(
            Guid.NewGuid(),
            session,
            cart.CreateSnapshot(),
            [],
            -4m,
            4m,
            "R",
            "SQ:payment-1",
            now);
        var attempts = new RecordingSquarePaymentAttemptRepository();
        await attempts.CreateAsync(new LocalSquarePaymentAttempt(
            Guid.NewGuid(),
            null,
            "persisted-square-refund-key",
            "DEV-1",
            "LOC-1",
            "Production",
            4m,
            400,
            "AUD",
            LocalSquarePaymentAttemptStatus.Pending,
            null,
            null,
            JsonSerializer.Serialize(draft, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            session.StoreCode,
            session.DeviceCode,
            session.CashierId,
            null,
            null,
            null,
            null,
            now,
            now,
            null,
            null,
            null,
            "Refund",
            draft.OrderGuid));
        var terminal = new RecordingIdempotentCardRefundClient(
            new PaymentAuthorizationResult(true, "SQRF:refund-1", "APPROVED", AuthorizedAmount: 4m));
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: attempts);

        var tender = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            actualAmount: -4m,
            currentTenders: [],
            amountText: "4",
            referenceText: "SQ:payment-1",
            cartSnapshot: cart.CreateSnapshot());

        Assert.True(tender.Succeeded);
        Assert.Single(attempts.Attempts);
        Assert.Equal("persisted-square-refund-key", terminal.LastIdempotencyKey);
        Assert.Equal(1, terminal.IdempotentRefundCallCount);
    }

    [Fact]
    public async Task Square_refund_result_unknown_blocks_restart_without_second_terminal_call()
    {
        var cart = CreateReturnCart(4m);
        var attempts = new RecordingSquarePaymentAttemptRepository();
        var terminal = new RecordingIdempotentCardRefundClient(
            new PaymentAuthorizationResult(
                false,
                null,
                "Square refund result is unknown.",
                AuthorizedAmount: 4m,
                ResultUnknown: true));
        var settings = new StaticCardTerminalSettingsProvider(CreateSquareSettings());
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);
        var firstWorkflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardTerminalSettingsProvider: settings,
            squarePaymentAttemptRepository: attempts);

        var first = await firstWorkflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            actualAmount: -4m,
            currentTenders: [],
            amountText: "4",
            referenceText: "SQ:payment-1",
            cartSnapshot: cart.CreateSnapshot());
        var persistedAttempt = Assert.Single(attempts.Attempts);
        var restartedWorkflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardTerminalSettingsProvider: settings,
            squarePaymentAttemptRepository: attempts);

        var afterRestart = await restartedWorkflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            actualAmount: -4m,
            currentTenders: [],
            amountText: "4",
            referenceText: "SQ:payment-1",
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(first.Succeeded);
        Assert.False(afterRestart.Succeeded);
        Assert.Equal("payment.card.resultUnknown", afterRestart.StatusKey);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, persistedAttempt.Status);
        Assert.Equal(persistedAttempt.IdempotencyKey, terminal.LastIdempotencyKey);
        Assert.Equal(1, terminal.IdempotentRefundCallCount);
        Assert.Single(attempts.Attempts);
    }

    [Fact]
    public async Task Square_refund_late_approved_worker_cannot_return_tender_after_new_claim()
    {
        var cart = CreateReturnCart(4m);
        var attempts = new RecordingSquarePaymentAttemptRepository();
        var terminal = new RecordingIdempotentCardRefundClient(
            new PaymentAuthorizationResult(
                true,
                "SQRF:late-refund",
                "COMPLETED",
                AuthorizedAmount: 4m,
                CardTransactions:
                [
                    new CardTransactionDto(
                        "Square",
                        "late-refund",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "COMPLETED",
                        null,
                        DateTimeOffset.UtcNow,
                        4m,
                        null)
                ]),
            beforeResult: () => attempts.SimulateSupervisorRetryAndNewClaim("new-worker-token"));
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateSquareSettings()),
            squarePaymentAttemptRepository: attempts);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var result = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            actualAmount: -4m,
            currentTenders: [],
            amountText: "4",
            referenceText: "SQ:payment-1",
            cartSnapshot: cart.CreateSnapshot());

        var saved = Assert.Single(attempts.Attempts);
        Assert.False(result.Succeeded);
        Assert.Null(result.Tender);
        Assert.Equal("payment.card.resultUnknown", result.StatusKey);
        Assert.Equal("new-worker-token", saved.SubmissionToken);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, saved.Status);
        Assert.Null(saved.PaymentId);
    }

    [Fact]
    public async Task Approved_card_refund_order_and_attempt_completion_ignore_cancelled_ui_token()
    {
        var cart = CreateReturnCart(4m);
        var attempts = new RecordingCardPaymentAttemptRepository { RejectCancelledTokens = true };
        var orders = new RecordingOrderRepository { RejectCancelledTokens = true };
        using var cancellation = new CancellationTokenSource();
        var terminal = new RecordingIdempotentCardRefundClient(
            new PaymentAuthorizationResult(true, "ANZ:REFUND-CANCEL", "APPROVED", AuthorizedAmount: 4m),
            cancellation.Cancel);
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(CreateLocalLinklySettings()));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tender = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            actualAmount: -4m,
            currentTenders: [],
            amountText: "4",
            referenceText: "ANZ:SALE-CANCEL",
            cancellationToken: cancellation.Token,
            cartSnapshot: cart.CreateSnapshot());
        var completion = await workflow.CompletePaymentAsync(
            cart,
            session,
            [tender.Tender!],
            cashTenderedAmount: 0m,
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(tender.Succeeded);
        Assert.Single(orders.SavedOrders);
        Assert.Equal(completion.Order.OrderGuid, orders.SavedOrders[0].OrderGuid);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, Assert.Single(attempts.Attempts).Status);
    }

    [Fact]
    public async Task Payment_workflow_rejects_card_refund_without_original_reference()
    {
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            cardTerminalClient: new ApprovedCardTerminalClient("CARD-REFUND"));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tender = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            actualAmount: -10m,
            currentTenders: [],
            amountText: "4");

        Assert.False(tender.Succeeded);
        Assert.Equal("payment.status.cardDeclined", tender.StatusKey);
    }

    [Fact]
    public async Task Payment_workflow_adds_negative_voucher_tender_for_refund()
    {
        var vouchers = new ApprovedVoucherTenderClient("VOUCHER_REFUND:RF123");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new RecordingOrderRepository(),
            new StubSyncQueueRepository(pendingCount: 1),
            voucherTenderClient: vouchers);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var tender = await workflow.AddTenderAsync(
            PaymentMethodKind.Voucher,
            session,
            actualAmount: -10m,
            currentTenders: [],
            amountText: "6");

        Assert.True(tender.Succeeded);
        Assert.NotNull(tender.Tender);
        Assert.Equal(-6m, tender.Tender.Amount);
        Assert.Equal("VOUCHER_REFUND_PENDING", tender.Tender.Reference);
        Assert.Equal(0, vouchers.IssueRefundCallCount);
    }

    [Fact]
    public void Payment_workflow_calculates_refund_remaining_and_change_without_over_refunding()
    {
        var workflow = CreateWorkflow();

        var remainingAfterCash = workflow.CalculateRemainingAmount(
            -7.82m,
            [new PaymentTender(PaymentMethodKind.Cash, -7.80m)]);
        var remainingAfterCard = workflow.CalculateRemainingAmount(
            -10m,
            [new PaymentTender(PaymentMethodKind.Card, -4m, "SQ:payment-1")]);
        var change = workflow.CalculateChange(
            [new PaymentTender(PaymentMethodKind.Cash, -7.80m)],
            -7.82m);

        Assert.Equal(0m, remainingAfterCash);
        Assert.Equal(-6m, remainingAfterCard);
        Assert.Equal(0m, change);
    }

    [Fact]
    public async Task Payment_workflow_completes_refund_order_with_negative_payments()
    {
        var cart = new PosCartService();
        cart.AddReturnLine(new ReturnCartLineRequest(
            "S001",
            "SKU-RET",
            null,
            "Returned Tea",
            "930500",
            "ITEM-RET",
            null,
            1m,
            7.82m,
            PriceSourceKind.StoreRetailPrice,
            PriceSourceKind.StoreRetailPrice.ToString(),
            "RETURN-500",
            Guid.NewGuid(),
            Guid.NewGuid()));
        var orders = new RecordingOrderRepository();
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var result = await workflow.CompletePaymentAsync(
            cart,
            session,
            [new PaymentTender(PaymentMethodKind.Cash, -7.80m)],
            cashTenderedAmount: -7.80m);

        var payment = Assert.Single(result.Order.Payments);
        Assert.Equal(-7.80m, payment.Amount);
        Assert.Equal(-7.80m, result.TenderedAmount);
        Assert.Equal(0m, result.ChangeAmount);
    }

    [Fact]
    public async Task Payment_workflow_issues_refund_voucher_after_order_guid_exists()
    {
        var cart = new PosCartService();
        cart.AddReturnLine(new ReturnCartLineRequest(
            "S001",
            "SKU-VR",
            null,
            "Voucher Refund Tea",
            "930501",
            "ITEM-VR",
            null,
            1m,
            6m,
            PriceSourceKind.StoreRetailPrice,
            PriceSourceKind.StoreRetailPrice.ToString(),
            "RETURN-VOUCHER-1",
            Guid.NewGuid(),
            Guid.NewGuid()));
        var orders = new RecordingOrderRepository();
        var vouchers = new ApprovedVoucherTenderClient("VOUCHER_REFUND:RF123");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            voucherTenderClient: vouchers);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var result = await workflow.CompletePaymentAsync(
            cart,
            session,
            [new PaymentTender(PaymentMethodKind.Voucher, -6m, "VOUCHER_REFUND_PENDING")],
            cashTenderedAmount: 0m);

        var saved = Assert.Single(orders.SavedOrders);
        var payment = Assert.Single(saved.Payments);
        Assert.Equal(result.Order.OrderGuid.ToString("D"), vouchers.LastOrderReference);
        Assert.Equal(saved.OrderGuid.ToString("D"), vouchers.LastOrderReference);
        Assert.False(string.IsNullOrWhiteSpace(vouchers.LastIdempotencyKey));
        Assert.Equal(-6m, payment.Amount);
        Assert.Equal("VOUCHER_REFUND:RF123", payment.Reference);
        Assert.Equal(1, vouchers.IssueRefundCallCount);
    }

    [Fact]
    public async Task Payment_workflow_does_not_issue_refund_voucher_before_local_save_succeeds()
    {
        var cart = new PosCartService();
        cart.AddReturnLine(new ReturnCartLineRequest(
            "S001",
            "SKU-VR-RETRY",
            null,
            "Voucher Refund Retry",
            "930503",
            "ITEM-VR-RETRY",
            null,
            1m,
            6m,
            PriceSourceKind.StoreRetailPrice,
            PriceSourceKind.StoreRetailPrice.ToString(),
            "RETURN-VOUCHER-RETRY",
            Guid.NewGuid(),
            Guid.NewGuid()));
        var orders = new FailingOnceOrderRepository();
        var vouchers = new ApprovedVoucherTenderClient("VOUCHER_REFUND:RF123");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            voucherTenderClient: vouchers);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);
        var tender = (await workflow.AddTenderAsync(
            PaymentMethodKind.Voucher,
            session,
            actualAmount: -6m,
            currentTenders: [],
            amountText: "6")).Tender!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.CompletePaymentAsync(
            cart,
            session,
            [tender],
            cashTenderedAmount: 0m));
        Assert.Equal(0, vouchers.IssueRefundCallCount);

        await workflow.CompletePaymentAsync(
            cart,
            session,
            [tender],
            cashTenderedAmount: 0m);

        Assert.False(string.IsNullOrWhiteSpace(vouchers.LastIdempotencyKey));
        Assert.Equal(tender.IdempotencyKey, vouchers.LastIdempotencyKey);
        Assert.Equal(1, vouchers.IssueRefundCallCount);
        Assert.Single(orders.SavedOrders);
    }

    [Fact]
    public async Task Payment_workflow_persists_pending_voucher_refund_order_when_issue_fails()
    {
        var cart = new PosCartService();
        cart.AddReturnLine(new ReturnCartLineRequest(
            "S001",
            "SKU-VR-FAIL",
            null,
            "Voucher Refund Fail",
            "930502",
            "ITEM-VR-FAIL",
            null,
            1m,
            6m,
            PriceSourceKind.StoreRetailPrice,
            PriceSourceKind.StoreRetailPrice.ToString(),
            "RETURN-VOUCHER-FAIL",
            Guid.NewGuid(),
            Guid.NewGuid()));
        var orders = new RecordingOrderRepository();
        var vouchers = new ApprovedVoucherTenderClient("VOUCHER_REFUND:RF123", approveRefund: false);
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            voucherTenderClient: vouchers);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        await Assert.ThrowsAsync<PaymentUploadFailedException>(() => workflow.CompletePaymentAsync(
            cart,
            session,
            [new PaymentTender(PaymentMethodKind.Voucher, -6m, "VOUCHER_REFUND_PENDING")],
            cashTenderedAmount: 0m));

        var saved = Assert.Single(orders.SavedOrders);
        var payment = Assert.Single(saved.Payments);
        Assert.Equal("VOUCHER_REFUND_PENDING", payment.Reference);
        Assert.False(string.IsNullOrWhiteSpace(payment.IdempotencyKey));
        Assert.Equal(1, vouchers.IssueRefundCallCount);
    }

    [Fact]
    public async Task Payment_workflow_retry_reuses_pending_refund_voucher_idempotency_key_and_updates_local_reference()
    {
        var cart = new PosCartService();
        cart.AddReturnLine(new ReturnCartLineRequest(
            "S001",
            "SKU-VR-RECOVER",
            null,
            "Voucher Refund Recover",
            "930504",
            "ITEM-VR-RECOVER",
            null,
            1m,
            6m,
            PriceSourceKind.StoreRetailPrice,
            PriceSourceKind.StoreRetailPrice.ToString(),
            "RETURN-VOUCHER-RECOVER",
            Guid.NewGuid(),
            Guid.NewGuid()));
        var orders = new RecordingOrderRepository();
        var vouchers = new RetriableVoucherTenderClient("VOUCHER_REFUND:RF123");
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            orders,
            new StubSyncQueueRepository(pendingCount: 1),
            voucherTenderClient: vouchers);
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var failed = await Assert.ThrowsAsync<PaymentUploadFailedException>(() => workflow.CompletePaymentAsync(
            cart,
            session,
            [new PaymentTender(PaymentMethodKind.Voucher, -6m, "VOUCHER_REFUND_PENDING")],
            cashTenderedAmount: 0m));
        vouchers.FailIssueRefund = false;

        var savedBeforeRetry = Assert.Single(orders.SavedOrders);
        var pendingPayment = Assert.Single(savedBeforeRetry.Payments);
        var result = await workflow.RetryVoucherUploadAsync(
            savedBeforeRetry.OrderGuid,
            cart,
            session,
            tenderedAmount: 0m,
            changeAmount: 0m);

        Assert.Contains("issue failed", failed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, vouchers.IssueRefundCallCount);
        Assert.Equal(2, vouchers.IssueRefundIdempotencyKeys.Count);
        Assert.Equal(vouchers.IssueRefundIdempotencyKeys[0], vouchers.IssueRefundIdempotencyKeys[1]);
        Assert.Equal(pendingPayment.IdempotencyKey, vouchers.IssueRefundIdempotencyKeys[0]);
        Assert.Equal("VOUCHER_REFUND:RF123", Assert.Single(result.Order.Payments).Reference);
        Assert.Equal("VOUCHER_REFUND:RF123", Assert.Single(Assert.Single(orders.SavedOrders).Payments).Reference);
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

    private static PosCartService CreateReturnCart(decimal amount)
    {
        var cart = new PosCartService();
        cart.AddReturnLine(new ReturnCartLineRequest(
            "S001",
            "SKU-REFUND",
            null,
            "Returned Item",
            "930599",
            "ITEM-REFUND",
            null,
            1m,
            amount,
            PriceSourceKind.StoreRetailPrice,
            PriceSourceKind.StoreRetailPrice.ToString(),
            "RETURN-REFUND",
            Guid.NewGuid(),
            Guid.NewGuid()));
        return cart;
    }

    private static CardTerminalSettings CreateBackendLinklySettings()
    {
        return new CardTerminalSettings(
            CardProcessorKind.Linkly,
            CardTerminalEnvironment.Sandbox,
            "127.0.0.1",
            2011,
            null,
            null,
            null,
            CardTerminalSettings.GetSquareApiBaseUrl(CardTerminalEnvironment.Sandbox),
            TimeSpan.FromSeconds(90),
            LinklyConnectionMode.CloudBackendAsync);
    }

    private static CardTerminalSettings CreateLocalLinklySettings()
    {
        return new CardTerminalSettings(
            CardProcessorKind.Linkly,
            CardTerminalEnvironment.Sandbox,
            "127.0.0.1",
            2011,
            null,
            null,
            null,
            CardTerminalSettings.GetSquareApiBaseUrl(CardTerminalEnvironment.Sandbox),
            TimeSpan.FromSeconds(90),
            LinklyConnectionMode.LocalIp);
    }

    private static CardTerminalSettings CreateSquareSettings()
    {
        return new CardTerminalSettings(
            CardProcessorKind.Square,
            CardTerminalEnvironment.Production,
            "127.0.0.1",
            2011,
            "square-token",
            "LOC-1",
            "DEV-1",
            CardTerminalSettings.GetSquareApiBaseUrl(CardTerminalEnvironment.Production),
            TimeSpan.FromSeconds(90));
    }

    private sealed class RecordingOrderRepository : ILocalOrderRepository
    {
        public List<LocalOrder> SavedOrders { get; } = [];

        public bool RejectCancelledTokens { get; init; }

        public TaskCompletionSource? SaveStarted { get; init; }

        public TaskCompletionSource? ContinueSave { get; init; }

        public int SaveThreadId { get; private set; }

        public Exception? SaveException { get; set; }

        public async Task SavePendingOrderAsync(LocalOrder order, CancellationToken cancellationToken = default)
        {
            if (RejectCancelledTokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            SaveThreadId = Environment.CurrentManagedThreadId;
            SaveStarted?.TrySetResult();
            if (ContinueSave is not null)
            {
                await ContinueSave.Task.WaitAsync(cancellationToken);
            }

            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedOrders.Add(order);
        }

        public Task UpdatePaymentReferenceAsync(
            Guid paymentGuid,
            string? reference,
            CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < SavedOrders.Count; index++)
            {
                var order = SavedOrders[index];
                var paymentIndex = order.Payments
                    .ToList()
                    .FindIndex(payment => payment.PaymentGuid == paymentGuid);
                if (paymentIndex < 0 || paymentIndex >= order.Payments.Count)
                {
                    continue;
                }

                var updatedPayments = order.Payments.ToList();
                updatedPayments[paymentIndex] = updatedPayments[paymentIndex] with { Reference = reference };
                SavedOrders[index] = order with { Payments = updatedPayments };
                break;
            }

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

    private sealed class FailingOnceOrderRepository : ILocalOrderRepository
    {
        private bool _hasFailed;

        public List<LocalOrder> SavedOrders { get; } = [];

        public Task SavePendingOrderAsync(LocalOrder order, CancellationToken cancellationToken = default)
        {
            if (!_hasFailed)
            {
                _hasFailed = true;
                throw new InvalidOperationException("local save failed");
            }

            SavedOrders.Add(order);
            return Task.CompletedTask;
        }

        public Task UpdatePaymentReferenceAsync(
            Guid paymentGuid,
            string? reference,
            CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < SavedOrders.Count; index++)
            {
                var order = SavedOrders[index];
                var paymentIndex = order.Payments
                    .ToList()
                    .FindIndex(payment => payment.PaymentGuid == paymentGuid);
                if (paymentIndex < 0 || paymentIndex >= order.Payments.Count)
                {
                    continue;
                }

                var updatedPayments = order.Payments.ToList();
                updatedPayments[paymentIndex] = updatedPayments[paymentIndex] with { Reference = reference };
                SavedOrders[index] = order with { Payments = updatedPayments };
                break;
            }

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

    private sealed class ThrowingPendingSyncQueueRepository : ISyncQueueRepository
    {
        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<int>(new InvalidOperationException("pending sync refresh failed"));

        public Task<SyncQueueOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncQueueOverview(0, 0, 0, null));

        public Task<IReadOnlyList<SyncQueueListItem>> GetActiveItemsAsync(int take = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SyncQueueListItem>>([]);
    }

    private sealed class RecordingCardPaymentAttemptRepository : ILocalCardPaymentAttemptRepository
    {
        public List<LocalCardPaymentAttempt> Attempts { get; } = [];

        public Exception? MarkOrderCompletedException { get; init; }

        public Exception? GetAttemptException { get; set; }

        public Exception? UpdateOutcomeException { get; init; }

        public bool RejectCancelledTokens { get; init; }

        public Task CreateAsync(LocalCardPaymentAttempt attempt, CancellationToken cancellationToken = default)
        {
            Attempts.Add(attempt);
            return Task.CompletedTask;
        }

        public Task UpdateSessionAsync(
            Guid attemptGuid,
            string sessionId,
            string? txnRef,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            Update(attemptGuid, attempt => attempt with
            {
                SessionId = sessionId,
                TxnRef = txnRef,
                Status = LocalCardPaymentAttemptStatus.SessionStarted,
                UpdatedAt = updatedAt
            });
            return Task.CompletedTask;
        }

        public Task UpdateOutcomeAsync(
            Guid attemptGuid,
            LocalCardPaymentAttemptStatus status,
            string? responseCode,
            string? responseText,
            string? paymentReference,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            if (RejectCancelledTokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (UpdateOutcomeException is not null)
            {
                throw UpdateOutcomeException;
            }

            Update(attemptGuid, attempt => attempt with
            {
                Status = status,
                ResponseCode = responseCode,
                ResponseText = responseText,
                PaymentReference = paymentReference,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            });
            return Task.CompletedTask;
        }

        public Task MarkOrderCompletedAsync(
            Guid attemptGuid,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            if (RejectCancelledTokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (MarkOrderCompletedException is not null)
            {
                throw MarkOrderCompletedException;
            }

            Update(attemptGuid, attempt => attempt with
            {
                Status = LocalCardPaymentAttemptStatus.OrderCompleted,
                CompletedAt = attempt.CompletedAt ?? completedAt,
                UpdatedAt = completedAt
            });
            return Task.CompletedTask;
        }

        public Task MarkAcknowledgedAsync(
            Guid attemptGuid,
            DateTimeOffset acknowledgedAt,
            CancellationToken cancellationToken = default)
        {
            Update(attemptGuid, attempt => attempt with
            {
                AcknowledgedAt = acknowledgedAt,
                UpdatedAt = acknowledgedAt
            });
            return Task.CompletedTask;
        }

        public Task MarkRecoveringAsync(
            Guid attemptGuid,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            Update(attemptGuid, attempt => attempt with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                UpdatedAt = updatedAt
            });
            return Task.CompletedTask;
        }

        public Task<LocalCardPaymentAttempt?> GetLatestOpenAttemptAsync(
            string storeCode,
            string deviceCode,
            string? cashierId,
            string environment,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LocalCardPaymentAttempt?>(Attempts.LastOrDefault());
        }

        public Task<IReadOnlyList<LocalCardPaymentAttempt>> GetOpenRefundAttemptsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalCardPaymentAttempt>>(Attempts
                .Where(attempt =>
                    attempt.StoreCode == storeCode &&
                    attempt.DeviceCode == deviceCode &&
                    attempt.Environment == environment &&
                    attempt.OperationKind == "Refund" &&
                    attempt.Status is not (
                        LocalCardPaymentAttemptStatus.Declined or
                        LocalCardPaymentAttemptStatus.TimedOut or
                        LocalCardPaymentAttemptStatus.Cancelled or
                        LocalCardPaymentAttemptStatus.Failed or
                        LocalCardPaymentAttemptStatus.OrderCompleted or
                        LocalCardPaymentAttemptStatus.Abandoned))
                .OrderByDescending(attempt => attempt.UpdatedAt)
                .ToArray());
        }

        public Task<LocalCardPaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default)
        {
            if (RejectCancelledTokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (GetAttemptException is not null)
            {
                throw GetAttemptException;
            }

            return Task.FromResult<LocalCardPaymentAttempt?>(Attempts.SingleOrDefault(attempt => attempt.AttemptGuid == attemptGuid));
        }

        private void Update(Guid attemptGuid, Func<LocalCardPaymentAttempt, LocalCardPaymentAttempt> update)
        {
            var index = Attempts.FindIndex(attempt => attempt.AttemptGuid == attemptGuid);
            Assert.True(index >= 0);
            Attempts[index] = update(Attempts[index]);
        }
    }

    private sealed class RecordingSquarePaymentAttemptRepository : ILocalSquarePaymentAttemptRepository
    {
        public List<LocalSquarePaymentAttempt> Attempts { get; } = [];

        public bool RejectCancelledTokens { get; init; }

        public Exception? MarkCheckoutCreatedException { get; init; }

        public Task<bool> TryBeginRefundSubmissionAsync(
            Guid attemptGuid,
            DateTimeOffset expectedUpdatedAt,
            string submissionToken,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            var attempt = Attempts.Single(candidate => candidate.AttemptGuid == attemptGuid);
            if (attempt.UpdatedAt != expectedUpdatedAt)
            {
                return Task.FromResult(false);
            }

            Update(attemptGuid, current => current with
            {
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                ResponseCode = null,
                ResponseText = null,
                SubmissionToken = submissionToken,
                UpdatedAt = updatedAt
            });
            return Task.FromResult(true);
        }

        public Task<bool> TryMarkRefundPaymentVerifiedAsync(
            Guid attemptGuid,
            string submissionToken,
            string paymentId,
            string paymentStatus,
            string? responseCode,
            string? responseText,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            var attempt = Attempts.Single(candidate => candidate.AttemptGuid == attemptGuid);
            if (!string.Equals(attempt.SubmissionToken, submissionToken, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            Update(attemptGuid, current => current with
            {
                Status = LocalSquarePaymentAttemptStatus.PaymentVerified,
                PaymentId = paymentId,
                PaymentStatus = paymentStatus,
                ResponseCode = responseCode,
                ResponseText = responseText,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            });
            return Task.FromResult(true);
        }

        public void SimulateSupervisorRetryAndNewClaim(string submissionToken)
        {
            var attempt = Assert.Single(Attempts);
            Update(attempt.AttemptGuid, current => current with
            {
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                ResponseCode = null,
                ResponseText = null,
                SubmissionToken = submissionToken,
                UpdatedAt = current.UpdatedAt.AddTicks(1)
            });
        }

        public Task CreateAsync(LocalSquarePaymentAttempt attempt, CancellationToken cancellationToken = default)
        {
            Attempts.Add(attempt);
            return Task.CompletedTask;
        }

        public Task MarkCheckoutCreatedAsync(
            Guid attemptGuid,
            string checkoutId,
            string? checkoutStatus,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            if (RejectCancelledTokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (MarkCheckoutCreatedException is not null)
            {
                throw MarkCheckoutCreatedException;
            }

            Update(attemptGuid, attempt => attempt with
            {
                CheckoutId = checkoutId,
                CheckoutStatus = checkoutStatus,
                Status = LocalSquarePaymentAttemptStatus.CheckoutCreated,
                UpdatedAt = updatedAt
            });
            return Task.CompletedTask;
        }

        public Task<bool> TryMarkCheckoutCreatedAsync(
            Guid attemptGuid,
            string submissionToken,
            string checkoutId,
            string? checkoutStatus,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            var attempt = Attempts.Single(candidate => candidate.AttemptGuid == attemptGuid);
            if (!string.Equals(attempt.SubmissionToken, submissionToken, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            if (MarkCheckoutCreatedException is not null)
            {
                throw MarkCheckoutCreatedException;
            }

            Update(attemptGuid, current => current with
            {
                CheckoutId = checkoutId,
                CheckoutStatus = checkoutStatus,
                Status = LocalSquarePaymentAttemptStatus.CheckoutCreated,
                UpdatedAt = updatedAt
            });
            return Task.FromResult(true);
        }

        public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
        {
            Update(attemptGuid, attempt => attempt with
            {
                Status = LocalSquarePaymentAttemptStatus.Recovering,
                UpdatedAt = updatedAt
            });
            return Task.CompletedTask;
        }

        public Task UpdateCheckoutStatusAsync(
            Guid attemptGuid,
            LocalSquarePaymentAttemptStatus status,
            string? checkoutStatus,
            string? cancelReason,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            Update(attemptGuid, attempt => attempt with
            {
                Status = status,
                CheckoutStatus = checkoutStatus ?? attempt.CheckoutStatus,
                CancelReason = cancelReason ?? attempt.CancelReason,
                UpdatedAt = updatedAt
            });
            return Task.CompletedTask;
        }

        public Task MarkPaymentVerifiedAsync(
            Guid attemptGuid,
            string paymentId,
            string paymentStatus,
            string? responseCode,
            string? responseText,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            Update(attemptGuid, attempt => attempt with
            {
                Status = LocalSquarePaymentAttemptStatus.PaymentVerified,
                PaymentId = paymentId,
                PaymentStatus = paymentStatus,
                ResponseCode = responseCode,
                ResponseText = responseText,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            });
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            Guid attemptGuid,
            LocalSquarePaymentAttemptStatus status,
            string? checkoutStatus,
            string? paymentStatus,
            string? responseCode,
            string? responseText,
            DateTimeOffset resolvedAt,
            CancellationToken cancellationToken = default,
            string? cancelReason = null)
        {
            Update(attemptGuid, attempt => attempt with
            {
                Status = status,
                CheckoutStatus = checkoutStatus ?? attempt.CheckoutStatus,
                CancelReason = cancelReason ?? attempt.CancelReason,
                PaymentStatus = paymentStatus ?? attempt.PaymentStatus,
                ResponseCode = responseCode,
                ResponseText = responseText,
                ResolvedAt = resolvedAt,
                UpdatedAt = resolvedAt
            });
            return Task.CompletedTask;
        }

        public Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
        {
            Update(attemptGuid, attempt => attempt with
            {
                Status = LocalSquarePaymentAttemptStatus.OrderCompleted,
                OrderCompletedAt = completedAt,
                UpdatedAt = completedAt
            });
            return Task.CompletedTask;
        }

        public Task<LocalSquarePaymentAttempt?> GetLatestOpenAttemptAsync(
            string storeCode,
            string deviceCode,
            string? cashierId,
            string environment,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LocalSquarePaymentAttempt?>(Attempts.LastOrDefault());
        }

        public Task<IReadOnlyList<LocalSquarePaymentAttempt>> GetOpenRefundAttemptsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalSquarePaymentAttempt>>(Attempts
                .Where(attempt =>
                    attempt.StoreCode == storeCode &&
                    attempt.DeviceCode == deviceCode &&
                    attempt.Environment == environment &&
                    attempt.OperationKind == "Refund" &&
                    attempt.Status is not (
                        LocalSquarePaymentAttemptStatus.Canceled or
                        LocalSquarePaymentAttemptStatus.TimedOut or
                        LocalSquarePaymentAttemptStatus.Failed or
                        LocalSquarePaymentAttemptStatus.OrderCompleted or
                        LocalSquarePaymentAttemptStatus.Abandoned))
                .OrderByDescending(attempt => attempt.UpdatedAt)
                .ToArray());
        }

        public Task<LocalSquarePaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LocalSquarePaymentAttempt?>(Attempts.SingleOrDefault(attempt => attempt.AttemptGuid == attemptGuid));
        }

        private void Update(Guid attemptGuid, Func<LocalSquarePaymentAttempt, LocalSquarePaymentAttempt> update)
        {
            var index = Attempts.FindIndex(attempt => attempt.AttemptGuid == attemptGuid);
            Assert.True(index >= 0);
            Attempts[index] = update(Attempts[index]);
        }
    }

    private sealed class ObservingCardTerminalClient(
        Action beforeResult,
        PaymentAuthorizationResult? result = null) : ICardTerminalClient
    {
        public Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            beforeResult();
            return Task.FromResult(result ?? new PaymentAuthorizationResult(
                true,
                "ANZBACKEND:TXN-1:session=backend-session-1:environment=Sandbox",
                "APPROVED",
                amount,
                [
                    new CardTransactionDto(
                        "ANZ",
                        "TXN-1",
                        null,
                        null,
                        null,
                        null,
                        null,
                        "00",
                        "APPROVED",
                        null,
                        DateTimeOffset.UtcNow,
                        amount,
                        null)
                ],
                "ANZ",
                "Sandbox",
                LinklyConnectionMode.CloudBackendAsync.ToString(),
                "P",
                "backend-session-1",
                "TXN-1",
                "00",
                "APPROVED"));
        }

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class AsyncObservingCardTerminalClient(
        Func<Task> beforeResult,
        PaymentAuthorizationResult result) : ICardTerminalClient
    {
        public async Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            await beforeResult();
            return result;
        }

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CancelledCardTerminalClient : ICardTerminalClient
    {
        public Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default) =>
            Task.FromCanceled<PaymentAuthorizationResult>(cancellationToken);

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default) =>
            Task.FromCanceled<PaymentAuthorizationResult>(cancellationToken);
    }

    private sealed class CancellingCardTerminalClient(
        Func<CancellationToken, Task> beforeCancellation) : ICardTerminalClient
    {
        public async Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            await beforeCancellation(cancellationToken);
            throw new OperationCanceledException();
        }

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingCardTerminalClient(
        Func<CancellationToken, Task>? beforeThrow = null) : ICardTerminalClient
    {
        public async Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            if (beforeThrow is not null)
            {
                await beforeThrow(cancellationToken);
            }

            throw new InvalidOperationException("terminal transport failed");
        }

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CancellingApprovedCardTerminalClient(
        ILinklyPaymentAttemptContextAccessor accessor,
        CancellationTokenSource cancellation,
        PaymentAuthorizationResult result) : ICardTerminalClient
    {
        public async Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            var context = accessor.Current;
            Assert.NotNull(context);
            await context!.BindSessionAsync(
                result.SessionId ?? throw new InvalidOperationException("Expected a session id."),
                result.TxnRef,
                DateTimeOffset.UtcNow,
                cancellationToken);
            cancellation.Cancel();
            return result;
        }

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BindingCardTerminalClient(
        ILinklyPaymentAttemptContextAccessor accessor,
        PaymentAuthorizationResult result,
        Action beforeBind,
        Action afterBind) : ICardTerminalClient
    {
        public int AuthorizeCallCount { get; private set; }

        public async Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            AuthorizeCallCount++;
            beforeBind();
            var context = accessor.Current;
            Assert.NotNull(context);
            await context!.BindSessionAsync(
                result.SessionId ?? throw new InvalidOperationException("Expected a session id."),
                result.TxnRef,
                DateTimeOffset.UtcNow,
                cancellationToken);
            afterBind();
            return result;
        }

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class LocalReferenceCardTerminalClient(
        ILinklyPaymentAttemptContextAccessor accessor,
        Action beforeResult) : ICardTerminalClient
    {
        public string? SeenTxnRef { get; private set; }

        public Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            beforeResult();
            var context = accessor.Current;
            Assert.NotNull(context);
            Assert.False(string.IsNullOrWhiteSpace(context!.TxnRef));
            SeenTxnRef = context.TxnRef;
            return Task.FromResult(new PaymentAuthorizationResult(
                true,
                $"ANZ:{SeenTxnRef}",
                "APPROVED",
                amount,
                [
                    new CardTransactionDto(
                        "ANZ",
                        SeenTxnRef,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "00",
                        "APPROVED",
                        null,
                        DateTimeOffset.UtcNow,
                        amount,
                        null)
                ],
                "ANZ",
                "Sandbox",
                LinklyConnectionMode.LocalIp.ToString(),
                "P",
                null,
                SeenTxnRef,
                "00",
                "APPROVED"));
        }

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingLinklyBackendTerminalClient : ILinklyBackendTerminalClient
    {
        public string? AcknowledgedSessionId { get; private set; }

        public CardTerminalSettings? AcknowledgedSettings { get; private set; }

        public Task<LinklyConnectionTestResult> TestConnectionAsync(
            CardTerminalEnvironment environment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LinklyConnectionTestResult> TestTransactionStatusAsync(
            CardTerminalEnvironment environment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentAuthorizationResult> PurchaseAsync(
            decimal amount,
            PosSessionState session,
            CardTerminalSettings settings,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            CardTerminalSettings settings,
            string? originalReference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LinklyCloudBackendSessionResponse?> GetResumableSessionAsync(
            CardTerminalSettings settings,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LinklyCloudBackendSessionResponse> RecoverSessionAsync(
            CardTerminalSettings settings,
            string sessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LinklyCloudBackendSessionResponse> ResumeSessionUntilFinalAsync(
            CardTerminalSettings settings,
            LinklyCloudBackendSessionResponse activeStatus,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LinklyCloudBackendSessionResponse> GetSessionStatusAsync(
            CardTerminalSettings settings,
            string sessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AcknowledgeSessionAsync(
            CardTerminalSettings settings,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            AcknowledgedSettings = settings;
            AcknowledgedSessionId = sessionId;
            return Task.CompletedTask;
        }
    }

    private sealed class ApprovedCardTerminalClient(string reference) : ICardTerminalClient
    {
        public int AuthorizeCallCount { get; private set; }

        public Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            AuthorizeCallCount++;
            return Task.FromResult(new PaymentAuthorizationResult(true, reference));
        }

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaymentAuthorizationResult(true, $"REFUND:{originalReference}", AuthorizedAmount: amount));
        }
    }

    private sealed class ApprovedVoucherTenderClient(
        string reference,
        decimal? authorizedAmount = null,
        bool approveRefund = true) : IVoucherTenderClient
    {
        public int IssueRefundCallCount { get; private set; }

        public int ReleaseCallCount { get; private set; }

        public string? LastOrderReference { get; private set; }

        public string? LastIdempotencyKey { get; private set; }

        public string? LastReleaseVoucherCode { get; private set; }

        public string? LastReleaseReservationToken { get; private set; }

        public Task<PaymentAuthorizationResult> RedeemAsync(
            decimal amount,
            PosSessionState session,
            string? voucherCode,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("ABC123", voucherCode);
            return Task.FromResult(new PaymentAuthorizationResult(true, reference, AuthorizedAmount: authorizedAmount));
        }

        public Task<PaymentAuthorizationResult> IssueRefundAsync(
            decimal amount,
            PosSessionState session,
            string orderReference,
            string idempotencyKey,
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            IssueRefundCallCount++;
            LastOrderReference = orderReference;
            LastIdempotencyKey = idempotencyKey;
            return Task.FromResult(approveRefund
                ? new PaymentAuthorizationResult(true, reference, AuthorizedAmount: authorizedAmount ?? amount)
                : new PaymentAuthorizationResult(false, null, "issue failed"));
        }

        public Task<bool> ReleaseAsync(
            PosSessionState session,
            string voucherCode,
            string reservationToken,
            CancellationToken cancellationToken = default)
        {
            ReleaseCallCount++;
            LastReleaseVoucherCode = voucherCode;
            LastReleaseReservationToken = reservationToken;
            return Task.FromResult(true);
        }
    }

    private sealed class RetriableVoucherTenderClient(string reference) : IVoucherTenderClient
    {
        public bool FailIssueRefund { get; set; } = true;

        public int IssueRefundCallCount { get; private set; }

        public List<string> IssueRefundIdempotencyKeys { get; } = [];

        public Task<PaymentAuthorizationResult> RedeemAsync(
            decimal amount,
            PosSessionState session,
            string? voucherCode,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentAuthorizationResult> IssueRefundAsync(
            decimal amount,
            PosSessionState session,
            string orderReference,
            string idempotencyKey,
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            IssueRefundCallCount++;
            IssueRefundIdempotencyKeys.Add(idempotencyKey);
            return Task.FromResult(FailIssueRefund
                ? new PaymentAuthorizationResult(false, null, "issue failed")
                : new PaymentAuthorizationResult(true, reference, AuthorizedAmount: amount));
        }

        public Task<bool> ReleaseAsync(
            PosSessionState session,
            string voucherCode,
            string reservationToken,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingIdempotentCardRefundClient(
        PaymentAuthorizationResult result,
        Action? beforeResult = null) : ICardTerminalClient, IIdempotentCardRefundClient
    {
        public int IdempotentRefundCallCount { get; private set; }

        public string? LastOriginalReference { get; private set; }

        public string? LastIdempotencyKey { get; private set; }

        public Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Durable refunds must use the idempotent overload.");

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            IdempotentRefundCallCount++;
            LastOriginalReference = originalReference;
            LastIdempotencyKey = idempotencyKey;
            beforeResult?.Invoke();
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingVoucherTenderClient : IVoucherTenderClient
    {
        public Task<PaymentAuthorizationResult> RedeemAsync(
            decimal amount,
            PosSessionState session,
            string? voucherCode,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentAuthorizationResult> IssueRefundAsync(
            decimal amount,
            PosSessionState session,
            string orderReference,
            string idempotencyKey,
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ReleaseAsync(
            PosSessionState session,
            string voucherCode,
            string reservationToken,
            CancellationToken cancellationToken = default)
        {
            throw new HttpRequestException("release unavailable");
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
