using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Linkly;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class CashPaymentWorkflowServiceTask2BTests
{
    [Fact]
    public async Task Takeover_creates_independent_active_session_and_persists_before_ack_then_starts_new()
    {
        var events = new List<string>();
        var attempts = new RecordingAttemptRepository(events);
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var backend = new RecordingBackendTerminalClient(events, FinalApprovedSession("active-session-1", "TXN-OLD"));
        var settings = CreateBackendLinklySettings();
        var terminal = new TakeoverInvokingCardTerminalClient(
            accessor,
            settings,
            ActivePendingSession("active-session-1", "TXN-OLD"),
            events);
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new StubOrderRepository(),
            new StubSyncQueueRepository(),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(settings),
            linklyPaymentAttemptContextAccessor: accessor,
            linklyBackendTerminalClient: backend);
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-T2B", "Takeover Tea", "930T2B", 10m));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var result = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());

        Assert.True(result.Succeeded);
        Assert.True(terminal.TakeoverResult?.Succeeded);

        // 新付款 attempt 身份不变，ActiveSession attempt 使用独立 AttemptGuid。
        var saleAttempt = Assert.Single(attempts.Attempts, attempt => attempt.OperationKind == "Sale");
        var activeAttempt = Assert.Single(attempts.Attempts, attempt => attempt.OperationKind == "ActiveSession");
        Assert.Equal(terminal.SeenAttemptGuid, saleAttempt.AttemptGuid);
        Assert.NotEqual(saleAttempt.AttemptGuid, activeAttempt.AttemptGuid);
        Assert.Equal("active-session-1", activeAttempt.SessionId);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, activeAttempt.Status);
        Assert.NotNull(activeAttempt.AcknowledgedAt);

        // 最终证据持久化必须先于 acknowledge，ack 必须先于新扣款。
        Assert.True(events.IndexOf("persist-final") < events.IndexOf("acknowledge"));
        Assert.True(events.IndexOf("acknowledge") < events.IndexOf("new-start"));
    }

    [Fact]
    public async Task Takeover_persist_failure_returns_failed_without_ack_or_new_start()
    {
        var events = new List<string>();
        var attempts = new RecordingAttemptRepository(events, persistException: new InvalidOperationException("persist failed"));
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var backend = new RecordingBackendTerminalClient(events, FinalApprovedSession("active-session-1", "TXN-OLD"));
        var settings = CreateBackendLinklySettings();
        var terminal = new TakeoverInvokingCardTerminalClient(
            accessor,
            settings,
            ActivePendingSession("active-session-1", "TXN-OLD"),
            events);
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new StubOrderRepository(),
            new StubSyncQueueRepository(),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(settings),
            linklyPaymentAttemptContextAccessor: accessor,
            linklyBackendTerminalClient: backend);
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-T2B-PF", "Persist Fail Tea", "930T2BPF", 10m));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var result = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(result.Succeeded);
        Assert.False(terminal.TakeoverResult?.Succeeded);
        Assert.DoesNotContain(events, item => item == "acknowledge");
        Assert.DoesNotContain(events, item => item == "new-start");
    }

    [Fact]
    public async Task Takeover_ack_failure_returns_failed_after_persist_without_new_start()
    {
        var events = new List<string>();
        var attempts = new RecordingAttemptRepository(events);
        var accessor = new LinklyPaymentAttemptContextAccessor();
        var backend = new RecordingBackendTerminalClient(
            events,
            FinalApprovedSession("active-session-1", "TXN-OLD"),
            ackException: new InvalidOperationException("ack failed"));
        var settings = CreateBackendLinklySettings();
        var terminal = new TakeoverInvokingCardTerminalClient(
            accessor,
            settings,
            ActivePendingSession("active-session-1", "TXN-OLD"),
            events);
        var workflow = new CashPaymentWorkflowService(
            new CashCheckoutService(),
            new StubOrderRepository(),
            new StubSyncQueueRepository(),
            cardTerminalClient: terminal,
            cardPaymentAttemptRepository: attempts,
            cardTerminalSettingsProvider: new StaticCardTerminalSettingsProvider(settings),
            linklyPaymentAttemptContextAccessor: accessor,
            linklyBackendTerminalClient: backend);
        var cart = new PosCartService();
        cart.AddItem(CreateItem("SKU-T2B-AF", "Ack Fail Tea", "930T2BAF", 10m));
        var session = new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

        var result = await workflow.AddTenderAsync(
            PaymentMethodKind.Card,
            session,
            10m,
            [],
            "10.00",
            cancellationToken: CancellationToken.None,
            cartSnapshot: cart.CreateSnapshot());

        Assert.False(result.Succeeded);
        Assert.False(terminal.TakeoverResult?.Succeeded);
        Assert.Contains("persist-final", events);
        Assert.Contains("acknowledge", events);
        Assert.DoesNotContain(events, item => item == "new-start");
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

    private static LinklyCloudBackendSessionResponse ActivePendingSession(string sessionId, string txnRef)
    {
        return new LinklyCloudBackendSessionResponse(
            "Sandbox",
            "S001",
            "POS-01",
            sessionId,
            "Pending",
            txnRef,
            ResponseCode: null,
            ResponseText: null,
            RecoveryAction: null,
            DisplayText: "Processing",
            CancelKeyFlag: false,
            OKKeyFlag: false,
            AcceptYesKeyFlag: false,
            DeclineNoKeyFlag: false,
            AuthoriseKeyFlag: false,
            InputType: null,
            GraphicCode: null,
            DisplayLines: null,
            ReceiptText: null,
            RecoveryCount: 0,
            ReceiptPrintedAt: null,
            ClientAcknowledgedAt: null,
            LastHttpStatus: 409,
            Notifications: []);
    }

    private static LinklyCloudBackendSessionResponse FinalApprovedSession(string sessionId, string txnRef)
    {
        return new LinklyCloudBackendSessionResponse(
            "Sandbox",
            "S001",
            "POS-01",
            sessionId,
            "Completed",
            txnRef,
            ResponseCode: "00",
            ResponseText: "APPROVED",
            RecoveryAction: null,
            DisplayText: "APPROVED",
            CancelKeyFlag: false,
            OKKeyFlag: false,
            AcceptYesKeyFlag: false,
            DeclineNoKeyFlag: false,
            AuthoriseKeyFlag: false,
            InputType: null,
            GraphicCode: null,
            DisplayLines: null,
            ReceiptText: "APPROVED RECEIPT",
            RecoveryCount: 0,
            ReceiptPrintedAt: null,
            ClientAcknowledgedAt: null,
            LastHttpStatus: 200,
            Notifications: [],
            TransactionSuccess: true);
    }

    private sealed class TakeoverInvokingCardTerminalClient(
        ILinklyPaymentAttemptContextAccessor accessor,
        CardTerminalSettings settings,
        LinklyCloudBackendSessionResponse activeStatus,
        IList<string> events) : ICardTerminalClient
    {
        public LinklyActiveSessionTakeoverResult? TakeoverResult { get; private set; }

        public Guid? SeenAttemptGuid { get; private set; }

        public async Task<PaymentAuthorizationResult> AuthorizeAsync(
            decimal amount,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            var context = accessor.Current;
            Assert.NotNull(context);
            Assert.NotNull(context!.TakeOverActiveSessionAsync);
            SeenAttemptGuid = context.AttemptGuid;
            TakeoverResult = await context.TakeOverActiveSessionAsync!(settings, activeStatus, cancellationToken);
            if (!TakeoverResult.Succeeded)
            {
                return new PaymentAuthorizationResult(
                    false,
                    null,
                    TakeoverResult.Message,
                    StatusKey: "linkly.backend.resultUnknown",
                    ResultUnknown: true);
            }

            events.Add("new-start");
            return new PaymentAuthorizationResult(true, "ANZBACKEND:TXN-NEW:session=new-session:environment=Sandbox", AuthorizedAmount: amount);
        }

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            string? originalReference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingBackendTerminalClient(
        IList<string> events,
        LinklyCloudBackendSessionResponse finalStatus,
        Exception? ackException = null) : ILinklyBackendTerminalClient
    {
        public Task<LinklyCloudBackendSessionResponse> ResumeSessionUntilFinalAsync(
            CardTerminalSettings settings,
            LinklyCloudBackendSessionResponse activeStatus,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(finalStatus);

        public Task AcknowledgeSessionAsync(
            CardTerminalSettings settings,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            events.Add("acknowledge");
            return ackException is null
                ? Task.CompletedTask
                : Task.FromException(ackException);
        }

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
            Task.FromResult<LinklyCloudBackendSessionResponse?>(null);

        public Task<LinklyCloudBackendSessionResponse> RecoverSessionAsync(
            CardTerminalSettings settings,
            string sessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LinklyCloudBackendSessionResponse> GetSessionStatusAsync(
            CardTerminalSettings settings,
            string sessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAttemptRepository(
        IList<string> events,
        Exception? persistException = null) : ILocalCardPaymentAttemptRepository
    {
        public List<LocalCardPaymentAttempt> Attempts { get; } = [];

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
            if (persistException is not null)
            {
                throw persistException;
            }

            events.Add("persist-final");
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
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LocalCardPaymentAttempt?>(Attempts.LastOrDefault());

        public Task<IReadOnlyList<LocalCardPaymentAttempt>> GetOpenRefundAttemptsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalCardPaymentAttempt>>(Array.Empty<LocalCardPaymentAttempt>());

        public Task<LocalCardPaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult<LocalCardPaymentAttempt?>(Attempts.SingleOrDefault(attempt => attempt.AttemptGuid == attemptGuid));

        private void Update(Guid attemptGuid, Func<LocalCardPaymentAttempt, LocalCardPaymentAttempt> update)
        {
            var index = Attempts.FindIndex(attempt => attempt.AttemptGuid == attemptGuid);
            Assert.True(index >= 0);
            Attempts[index] = update(Attempts[index]);
        }
    }

    private sealed class StaticCardTerminalSettingsProvider(CardTerminalSettings settings) : ICardTerminalSettingsProvider
    {
        public Task<CardTerminalSettings> GetSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);
    }

    private sealed class StubOrderRepository : ILocalOrderRepository
    {
        public Task SavePendingOrderAsync(LocalOrder order, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(int take = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalOrderSummary>>(Array.Empty<LocalOrderSummary>());

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(
            LocalOrderHistoryQuery query,
            int take = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalOrderSummary>>(Array.Empty<LocalOrderSummary>());

        public Task<LocalOrder?> GetOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult<LocalOrder?>(null);
    }

    private sealed class StubSyncQueueRepository : ISyncQueueRepository
    {
        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<SyncQueueOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncQueueOverview(0, 0, 0, null));

        public Task<IReadOnlyList<SyncQueueListItem>> GetActiveItemsAsync(int take = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SyncQueueListItem>>(Array.Empty<SyncQueueListItem>());
    }
}
