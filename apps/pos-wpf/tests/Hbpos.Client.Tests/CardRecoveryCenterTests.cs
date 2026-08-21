using System.Text.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Linkly;

namespace Hbpos.Client.Tests;

public sealed class CardRecoveryCenterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly PosSessionState Session = new(
        "HB POS",
        "S001",
        "Main Branch",
        "POS-01",
        "C001",
        "Alice",
        true,
        0);

    [Fact]
    public async Task Linkly_ListOpenAsync_maps_open_attempts_to_provider_scoped_queue_items()
    {
        var baseTime = DateTimeOffset.Parse("2026-06-05T09:00:00+10:00");
        var first = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            LocalCardPaymentAttemptStatus.Pending,
            "Sale",
            "C001",
            baseTime);
        var second = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000002"),
            LocalCardPaymentAttemptStatus.Recovering,
            "Refund",
            "C999",
            baseTime.AddMinutes(1));
        var repository = new FakeLinklyAttemptRepository(second, first);
        var service = CreateLinklyService(repository);

        var items = await service.ListOpenAsync(Session);

        Assert.Equal(
            [second.AttemptGuid, first.AttemptGuid],
            items.Select(item => item.AttemptGuid).ToArray());
        Assert.All(items, item => Assert.Equal(CardProcessorKind.Linkly, item.Processor));
        Assert.Equal("Refund", items[0].OperationKind);
        Assert.Equal("C999", items[0].CashierId);
        Assert.Equal(CardProcessorKind.Linkly, items[0].Key.Processor);
        Assert.Equal(second.AttemptGuid, items[0].Key.AttemptGuid);
    }

    [Fact]
    public async Task Linkly_ListOpenAsync_carries_readonly_detail_fields()
    {
        var baseTime = DateTimeOffset.Parse("2026-06-05T09:00:00+10:00");
        var attempt = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000003"),
            LocalCardPaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            baseTime);
        var repository = new FakeLinklyAttemptRepository(attempt);
        var service = CreateLinklyService(repository);

        var items = await service.ListOpenAsync(Session);

        var item = Assert.Single(items);
        Assert.Equal("{}", item.OrderDraftJson);
        Assert.Null(item.CheckoutId);
        Assert.Null(item.PaymentId);
        Assert.Equal("TXN", item.TxnRef);
    }

    [Fact]
    public async Task Linkly_RecoverAttemptAsync_recovers_the_selected_attempt_not_the_latest()
    {
        var baseTime = DateTimeOffset.Parse("2026-06-05T09:00:00+10:00");
        var latest = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000011"),
            LocalCardPaymentAttemptStatus.Pending,
            "Sale",
            "C001",
            baseTime);
        var selected = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000012"),
            LocalCardPaymentAttemptStatus.RequiresReview,
            "Sale",
            "C002",
            baseTime.AddMinutes(1));
        var repository = new FakeLinklyAttemptRepository(latest, selected);
        var service = CreateLinklyService(repository);

        var result = await service.RecoverAttemptAsync(selected.AttemptGuid, new PosCartService(), Session);

        Assert.Equal(selected.AttemptGuid, repository.LastRequestedAttemptGuid);
        Assert.NotNull(result.PaymentSupervisorDetails);
        Assert.Equal(selected.AttemptGuid, result.PaymentSupervisorDetails!.AttemptGuid);
    }

    [Fact]
    public async Task Linkly_RecoverAttemptAsync_ActiveSession_uses_selected_session_not_latest()
    {
        var baseTime = DateTimeOffset.Parse("2026-06-05T09:00:00+10:00");
        var older = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000013"),
            LocalCardPaymentAttemptStatus.Recovering,
            "ActiveSession",
            "C001",
            baseTime,
            sessionId: "SESSION-OLD");
        var selected = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000014"),
            LocalCardPaymentAttemptStatus.Recovering,
            "ActiveSession",
            "C001",
            baseTime.AddMinutes(1),
            sessionId: "SESSION-SELECTED");
        var repository = new FakeLinklyAttemptRepository(older, selected);
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateLinklyService(repository, backend);

        var result = await service.RecoverAttemptAsync(selected.AttemptGuid, new PosCartService(), Session);

        Assert.Equal("SESSION-SELECTED", backend.LastQueriedSessionId);
        Assert.NotNull(result.PaymentSupervisorDetails);
        Assert.Equal(selected.AttemptGuid, result.PaymentSupervisorDetails!.AttemptGuid);
    }

    [Fact]
    public async Task Linkly_RecoverAttemptAsync_excludes_other_storecode()
    {
        var baseTime = DateTimeOffset.Parse("2026-06-05T09:00:00+10:00");
        var otherStore = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000015"),
            LocalCardPaymentAttemptStatus.RequiresReview,
            "Sale",
            "C001",
            baseTime,
            storeCode: "S999");
        var repository = new FakeLinklyAttemptRepository(otherStore);
        var service = CreateLinklyService(repository);

        var result = await service.RecoverAttemptAsync(otherStore.AttemptGuid, new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.None, result.Outcome);
    }

    [Fact]
    public async Task Linkly_ResolveAttemptAsync_resolves_the_selected_refund_attempt_only()
    {
        var baseTime = DateTimeOffset.Parse("2026-06-05T09:00:00+10:00");
        var untouched = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000021"),
            LocalCardPaymentAttemptStatus.Recovering,
            "Refund",
            "C001",
            baseTime);
        var selected = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000022"),
            LocalCardPaymentAttemptStatus.Recovering,
            "Refund",
            "C002",
            baseTime.AddMinutes(1));
        var repository = new FakeLinklyAttemptRepository(untouched, selected);
        var service = CreateLinklyService(repository);

        var result = await service.ResolveAttemptAsync(
            selected.AttemptGuid,
            CardRecoverySupervisorDecision.ContinueWaiting,
            "Still waiting",
            evidence: null,
            reference: null,
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.NotNull(repository.LastRefundResolution);
        Assert.Equal(selected.AttemptGuid, repository.LastRefundResolution!.AttemptGuid);
    }

    [Fact]
    public async Task Square_ListOpenAsync_maps_open_attempts_with_square_provider_identity()
    {
        var baseTime = DateTimeOffset.Parse("2026-06-05T09:00:00+10:00");
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000031"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            baseTime,
            checkoutId: "CHECKOUT-1",
            paymentId: "PAYMENT-1");
        var repository = new FakeSquareAttemptRepository(attempt);
        var service = CreateSquareService(repository);

        var items = await service.ListOpenAsync(Session);

        var item = Assert.Single(items);
        Assert.Equal(CardProcessorKind.Square, item.Processor);
        Assert.Equal(attempt.AttemptGuid, item.Key.AttemptGuid);
        Assert.Equal("CHECKOUT-1", item.CheckoutId);
        Assert.Equal("PAYMENT-1", item.PaymentId);
    }

    [Fact]
    public async Task Square_ResolveAttemptAsync_sale_continue_waiting_keeps_lock_and_writes_journal()
    {
        var baseTime = DateTimeOffset.Parse("2026-06-05T09:00:00+10:00");
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000032"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            baseTime);
        var repository = new FakeSquareAttemptRepository(attempt);
        var service = CreateSquareService(repository);

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ContinueWaiting,
            "Keep waiting",
            evidence: null,
            reference: null,
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.NotNull(repository.LastPaymentResolution);
        Assert.Equal(attempt.AttemptGuid, repository.LastPaymentResolution!.AttemptGuid);
    }

    [Fact]
    public async Task Square_ResolveAttemptAsync_sale_confirm_not_processed_requires_empty_cart()
    {
        var baseTime = DateTimeOffset.Parse("2026-06-05T09:00:00+10:00");
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000033"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            baseTime,
            draftJson: SerializeDraft());
        var repository = new FakeSquareAttemptRepository(attempt);
        var service = CreateSquareService(repository);
        var cart = CreateNonEmptyCart();

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmNotProcessed,
            "Not paid",
            evidence: "bank check",
            reference: null,
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.Null(repository.LastPaymentResolution);
    }

    [Fact]
    public async Task Coordinator_ListOpenAsync_merges_both_providers_and_sorts_globally()
    {
        var baseTime = DateTimeOffset.Parse("2026-06-05T09:00:00+10:00");
        var linklyAttempt = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000041"),
            LocalCardPaymentAttemptStatus.Pending,
            "Sale",
            "C001",
            baseTime);
        var squareAttempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000042"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            baseTime.AddMinutes(1));

        var linklyService = CreateLinklyService(new FakeLinklyAttemptRepository(linklyAttempt));
        var squareService = CreateSquareService(new FakeSquareAttemptRepository(squareAttempt));
        var coordinator = new CardPaymentRecoveryCoordinator(
            new FakeSettingsProvider(CardProcessorKind.Linkly),
            linklyService,
            squareService);

        var items = await coordinator.ListOpenAsync(Session);

        Assert.Equal(2, items.Count);
        Assert.Equal(squareAttempt.AttemptGuid, items[0].AttemptGuid);
        Assert.Equal(CardProcessorKind.Square, items[0].Processor);
        Assert.Equal(linklyAttempt.AttemptGuid, items[1].AttemptGuid);
        Assert.Equal(CardProcessorKind.Linkly, items[1].Processor);
    }

    [Fact]
    public async Task Coordinator_RecoverAsync_routes_by_key_provider_regardless_of_settings()
    {
        var linklyService = CreateLinklyService(new FakeLinklyAttemptRepository());
        var squareService = new FakeSquareRecoveryService();
        var coordinator = new CardPaymentRecoveryCoordinator(
            new FakeSettingsProvider(CardProcessorKind.Linkly),
            linklyService,
            squareService);

        var squareKey = new CardRecoveryAttemptKey(CardProcessorKind.Square, Guid.NewGuid());
        await coordinator.RecoverAsync(squareKey, new PosCartService(), Session);

        Assert.Equal(1, squareService.RecoverAttemptCallCount);
        Assert.Equal(squareKey.AttemptGuid, squareService.LastRecoveredAttemptGuid);
    }

    [Fact]
    public async Task Coordinator_ResolveAsync_routes_by_key_provider_regardless_of_settings()
    {
        var linklyService = CreateLinklyService(new FakeLinklyAttemptRepository());
        var squareService = new FakeSquareRecoveryService();
        var coordinator = new CardPaymentRecoveryCoordinator(
            new FakeSettingsProvider(CardProcessorKind.Linkly),
            linklyService,
            squareService);

        var squareKey = new CardRecoveryAttemptKey(CardProcessorKind.Square, Guid.NewGuid());
        await coordinator.ResolveAsync(
            squareKey,
            CardRecoverySupervisorDecision.ContinueWaiting,
            "reason",
            evidence: null,
            reference: null,
            new PosCartService(),
            Session);

        Assert.Equal(1, squareService.ResolveAttemptCallCount);
        Assert.Equal(squareKey.AttemptGuid, squareService.LastResolvedAttemptGuid);
    }

    [Fact]
    public async Task Coordinator_isolates_same_guid_across_different_providers()
    {
        var sharedGuid = Guid.Parse("30000000-0000-0000-0000-000000000043");
        var linklyService = CreateLinklyService(new FakeLinklyAttemptRepository());
        var squareService = new FakeSquareRecoveryService();
        var coordinator = new CardPaymentRecoveryCoordinator(
            new FakeSettingsProvider(CardProcessorKind.Linkly),
            linklyService,
            squareService);

        await coordinator.RecoverAsync(
            new CardRecoveryAttemptKey(CardProcessorKind.Square, sharedGuid),
            new PosCartService(),
            Session);
        await coordinator.RecoverAsync(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, sharedGuid),
            new PosCartService(),
            Session);

        Assert.Equal(1, squareService.RecoverAttemptCallCount);
        Assert.Equal(sharedGuid, squareService.LastRecoveredAttemptGuid);
    }

    [Fact]
    public async Task Linkly_service_RecoverAsync_default_throws_when_not_wired()
    {
        ICardPaymentRecoveryService service = CreateLinklyService(new FakeLinklyAttemptRepository());

        await Assert.ThrowsAsync<NotSupportedException>(() => service.RecoverAsync(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, Guid.NewGuid()),
            new PosCartService(),
            Session));
    }

    private static CardPaymentRecoveryService CreateLinklyService(
        FakeLinklyAttemptRepository repository,
        ILinklyBackendTerminalClient? backend = null) =>
        new(
            repository,
            new FakeSettingsProvider(CardProcessorKind.Linkly),
            backend ?? new FakeLinklyBackendTerminalClient(),
            new CashCheckoutService(),
            null!,
            null!);

    private static SquarePaymentRecoveryService CreateSquareService(
        FakeSquareAttemptRepository repository) =>
        new(
            repository,
            new FakeSettingsProvider(CardProcessorKind.Square),
            null!,
            new CashCheckoutService(),
            null!);

    private static LocalCardPaymentAttempt CreateLinklyAttempt(
        Guid attemptGuid,
        LocalCardPaymentAttemptStatus status,
        string operationKind,
        string cashierId,
        DateTimeOffset updatedAt,
        string? sessionId = null,
        string storeCode = "S001")
    {
        return new LocalCardPaymentAttempt(
            attemptGuid,
            sessionId ?? (operationKind == "ActiveSession" ? "SESSION" : null),
            "TXN",
            "Linkly",
            "Sandbox",
            LinklyConnectionMode.CloudBackendAsync.ToString(),
            "P",
            12.34m,
            status,
            "{}",
            storeCode,
            "POS-01",
            cashierId,
            null,
            null,
            null,
            updatedAt.AddMinutes(-1),
            updatedAt,
            null,
            null,
            operationKind);
    }

    private static LocalSquarePaymentAttempt CreateSquareAttempt(
        Guid attemptGuid,
        LocalSquarePaymentAttemptStatus status,
        string operationKind,
        string cashierId,
        DateTimeOffset updatedAt,
        string? checkoutId = null,
        string? paymentId = null,
        string? draftJson = null,
        string storeCode = "S001")
    {
        return new LocalSquarePaymentAttempt(
            attemptGuid,
            checkoutId,
            "idem",
            "DEVICE",
            "LOCATION",
            "Sandbox",
            12.34m,
            1234,
            "AUD",
            status,
            null,
            null,
            draftJson ?? "{}",
            storeCode,
            "POS-01",
            cashierId,
            paymentId,
            null,
            null,
            null,
            updatedAt.AddMinutes(-1),
            updatedAt,
            null,
            null,
            null,
            operationKind);
    }

    private static string SerializeDraft()
    {
        var snapshot = new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "SKU-10",
                null,
                "Test Item",
                "930010",
                "ITEM-10",
                null,
                1m,
                10m,
                0m,
                null,
                PriceSourceKind.StoreRetailPrice,
                "Store price")
        ]);
        var draft = new CardPaymentOrderDraft(
            Guid.NewGuid(),
            Session,
            snapshot,
            [],
            10m,
            10m,
            "P",
            null,
            DateTimeOffset.Parse("2026-06-05T10:00:00+10:00"));
        return JsonSerializer.Serialize(draft, JsonOptions);
    }

    private static PosCartService CreateNonEmptyCart()
    {
        var cart = new PosCartService();
        cart.RestoreSnapshot(new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "SKU-NEW",
                null,
                "New Item",
                "930NEW",
                "ITEM-NEW",
                null,
                1m,
                5m,
                0m,
                null,
                PriceSourceKind.StoreRetailPrice,
                "Store price")
        ]));
        return cart;
    }

    private sealed class FakeSettingsProvider(CardProcessorKind processor) : ICardTerminalSettingsProvider
    {
        public Task<CardTerminalSettings> GetSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CardTerminalSettings(
                processor,
                CardTerminalEnvironment.Sandbox,
                "127.0.0.1",
                2011,
                null,
                null,
                null,
                "https://connect.squareupsandbox.com",
                TimeSpan.FromSeconds(30),
                LinklyConnectionMode.CloudBackendAsync));
    }

    private sealed class FakeLinklyBackendTerminalClient : ILinklyBackendTerminalClient
    {
        public string? LastQueriedSessionId { get; private set; }

        public Task<LinklyCloudBackendSessionResponse> GetSessionStatusAsync(
            CardTerminalSettings settings,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            LastQueriedSessionId = sessionId;
            return Task.FromResult<LinklyCloudBackendSessionResponse>(null!);
        }

        public Task<LinklyConnectionTestResult> TestConnectionAsync(CardTerminalEnvironment environment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LinklyConnectionTestResult> TestTransactionStatusAsync(CardTerminalEnvironment environment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentAuthorizationResult> PurchaseAsync(decimal amount, PosSessionState session, CardTerminalSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, CardTerminalSettings settings, string? originalReference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LinklyCloudBackendSessionResponse?> GetResumableSessionAsync(CardTerminalSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult<LinklyCloudBackendSessionResponse?>(null);

        public Task<LinklyCloudBackendSessionResponse> RecoverSessionAsync(CardTerminalSettings settings, string sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LinklyCloudBackendSessionResponse> ResumeSessionUntilFinalAsync(CardTerminalSettings settings, LinklyCloudBackendSessionResponse activeStatus, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AcknowledgeSessionAsync(CardTerminalSettings settings, string sessionId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeLinklyAttemptRepository : ILocalCardPaymentAttemptRepository
    {
        private readonly List<LocalCardPaymentAttempt> _attempts;

        public FakeLinklyAttemptRepository(params LocalCardPaymentAttempt[] attempts)
        {
            _attempts = [.. attempts];
        }

        public Guid? LastRequestedAttemptGuid { get; private set; }

        public CardRefundAttemptResolution? LastRefundResolution { get; private set; }

        public Task<LocalCardPaymentAttempt?> GetAttemptAsync(
            Guid attemptGuid,
            CancellationToken cancellationToken = default)
        {
            LastRequestedAttemptGuid = attemptGuid;
            return Task.FromResult(_attempts.FirstOrDefault(attempt => attempt.AttemptGuid == attemptGuid));
        }

        public Task<IReadOnlyList<LocalCardPaymentAttempt>> GetOpenAttemptsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalCardPaymentAttempt>>(_attempts);

        public Task<IReadOnlyList<LocalCardPaymentAttempt>> GetOpenRefundAttemptsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalCardPaymentAttempt>>(
                _attempts.Where(attempt => attempt.OperationKind == "Refund").ToArray());

        public Task<LocalCardPaymentAttempt?> GetLatestOpenAttemptAsync(
            string storeCode,
            string deviceCode,
            string? cashierId,
            string environment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_attempts.FirstOrDefault());

        public Task<bool> ResolveRefundWithJournalAsync(
            CardRefundAttemptResolution resolution,
            LocalFinancialSupervisorResolution journal,
            CancellationToken cancellationToken = default)
        {
            LastRefundResolution = resolution;
            return Task.FromResult(true);
        }

        public Task CreateAsync(LocalCardPaymentAttempt attempt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateSessionAsync(Guid attemptGuid, string sessionId, string? txnRef, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateOutcomeAsync(Guid attemptGuid, LocalCardPaymentAttemptStatus status, string? responseCode, string? responseText, string? paymentReference, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkAcknowledgedAsync(Guid attemptGuid, DateTimeOffset acknowledgedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeSquareAttemptRepository : ILocalSquarePaymentAttemptRepository
    {
        private readonly List<LocalSquarePaymentAttempt> _attempts;

        public FakeSquareAttemptRepository(params LocalSquarePaymentAttempt[] attempts)
        {
            _attempts = [.. attempts];
        }

        public SquarePaymentResolution? LastPaymentResolution { get; private set; }

        public Task<IReadOnlyList<LocalSquarePaymentAttempt>> GetOpenAttemptsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalSquarePaymentAttempt>>(_attempts);

        public Task<LocalSquarePaymentAttempt?> GetAttemptAsync(
            Guid attemptGuid,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_attempts.FirstOrDefault(attempt => attempt.AttemptGuid == attemptGuid));

        public Task<IReadOnlyList<LocalSquarePaymentAttempt>> GetOpenRefundAttemptsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalSquarePaymentAttempt>>(
                _attempts.Where(attempt => attempt.OperationKind == "Refund").ToArray());

        public Task<LocalSquarePaymentAttempt?> GetLatestOpenAttemptAsync(
            string storeCode,
            string deviceCode,
            string? cashierId,
            string environment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_attempts.FirstOrDefault());

        public Task<bool> ResolvePaymentWithJournalAsync(
            SquarePaymentResolution resolution,
            LocalFinancialSupervisorResolution journal,
            CancellationToken cancellationToken = default)
        {
            LastPaymentResolution = resolution;
            return Task.FromResult(true);
        }

        public Task CreateAsync(LocalSquarePaymentAttempt attempt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> TryRecordRefundResponseAsync(Guid attemptGuid, string submissionToken, string refundId, string refundStatus, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task MarkCheckoutCreatedAsync(Guid attemptGuid, string checkoutId, string? checkoutStatus, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateCheckoutStatusAsync(Guid attemptGuid, LocalSquarePaymentAttemptStatus status, string? checkoutStatus, string? cancelReason, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkPaymentVerifiedAsync(Guid attemptGuid, string paymentId, string paymentStatus, string? responseCode, string? responseText, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkFailedAsync(Guid attemptGuid, LocalSquarePaymentAttemptStatus status, string? checkoutStatus, string? paymentStatus, string? responseCode, string? responseText, DateTimeOffset resolvedAt, CancellationToken cancellationToken = default, string? cancelReason = null) =>
            Task.CompletedTask;

        public Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeSquareRecoveryService : ISquarePaymentRecoveryService
    {
        public int RecoverAttemptCallCount { get; private set; }

        public int ResolveAttemptCallCount { get; private set; }

        public Guid? LastRecoveredAttemptGuid { get; private set; }

        public Guid? LastResolvedAttemptGuid { get; private set; }

        public Task<CardPaymentRecoveryResult> RecoverLatestAsync(PosCartService cart, PosSessionState session, CancellationToken cancellationToken = default) =>
            Task.FromResult(CardPaymentRecoveryResult.None);

        public Task<CardRefundSupervisorResolutionResult> ResolveRefundAsync(CardRefundSupervisorResolution resolution, PosCartService cart, PosSessionState session, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CardRefundSupervisorResolutionResult(false, "unavailable"));

        public Task<IReadOnlyList<CardRecoveryQueueItem>> ListOpenAsync(PosSessionState session, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CardRecoveryQueueItem>>([]);

        public Task<CardPaymentRecoveryResult> RecoverAttemptAsync(Guid attemptGuid, PosCartService cart, PosSessionState session, CancellationToken cancellationToken = default)
        {
            RecoverAttemptCallCount++;
            LastRecoveredAttemptGuid = attemptGuid;
            return Task.FromResult(CardPaymentRecoveryResult.None);
        }

        public Task<CardRecoveryResolutionResult> ResolveAttemptAsync(Guid attemptGuid, CardRecoverySupervisorDecision decision, string reason, string? evidence, string? reference, PosCartService cart, PosSessionState session, CancellationToken cancellationToken = default)
        {
            ResolveAttemptCallCount++;
            LastResolvedAttemptGuid = attemptGuid;
            return Task.FromResult(new CardRecoveryResolutionResult(false, "unavailable"));
        }
    }
}
