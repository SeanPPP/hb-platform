using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class CardRecoveryCenterTests
{
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
            baseTime);
        var repository = new FakeSquareAttemptRepository(attempt);
        var service = new SquarePaymentRecoveryService(
            repository,
            new FakeSettingsProvider(CardProcessorKind.Square),
            null!,
            new CashCheckoutService(),
            null!);

        var items = await service.ListOpenAsync(Session);

        var item = Assert.Single(items);
        Assert.Equal(CardProcessorKind.Square, item.Processor);
        Assert.Equal(attempt.AttemptGuid, item.Key.AttemptGuid);
    }

    [Fact]
    public async Task Coordinator_ListOpenAsync_routes_to_the_active_provider_only()
    {
        var linklyAttempt = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000041"),
            LocalCardPaymentAttemptStatus.Pending,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"));
        var squareAttempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000042"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"));

        var linklyService = CreateLinklyService(new FakeLinklyAttemptRepository(linklyAttempt));
        var squareService = new SquarePaymentRecoveryService(
            new FakeSquareAttemptRepository(squareAttempt),
            new FakeSettingsProvider(CardProcessorKind.Square),
            null!,
            new CashCheckoutService(),
            null!);
        var coordinator = new CardPaymentRecoveryCoordinator(
            new FakeSettingsProvider(CardProcessorKind.Linkly),
            linklyService,
            squareService);

        var linklyItems = await coordinator.ListOpenAsync(Session);
        Assert.Single(linklyItems);
        Assert.Equal(CardProcessorKind.Linkly, linklyItems[0].Processor);

        var squareCoordinator = new CardPaymentRecoveryCoordinator(
            new FakeSettingsProvider(CardProcessorKind.Square),
            linklyService,
            squareService);
        var squareItems = await squareCoordinator.ListOpenAsync(Session);
        Assert.Single(squareItems);
        Assert.Equal(CardProcessorKind.Square, squareItems[0].Processor);
    }

    [Fact]
    public async Task Coordinator_RecoverAsync_rejects_a_key_from_another_provider()
    {
        var linklyService = CreateLinklyService(new FakeLinklyAttemptRepository());
        var squareService = new FakeSquareRecoveryService();
        var coordinator = new CardPaymentRecoveryCoordinator(
            new FakeSettingsProvider(CardProcessorKind.Linkly),
            linklyService,
            squareService);

        var result = await coordinator.RecoverAsync(
            new CardRecoveryAttemptKey(CardProcessorKind.Square, Guid.NewGuid()),
            new PosCartService(),
            Session);

        Assert.Equal(CardPaymentRecoveryOutcome.None, result.Outcome);
        Assert.Equal(0, squareService.RecoverAttemptCallCount);
    }

    [Fact]
    public async Task Coordinator_ResolveAsync_rejects_a_key_from_another_provider()
    {
        var linklyService = CreateLinklyService(new FakeLinklyAttemptRepository());
        var squareService = new FakeSquareRecoveryService();
        var coordinator = new CardPaymentRecoveryCoordinator(
            new FakeSettingsProvider(CardProcessorKind.Linkly),
            linklyService,
            squareService);

        var result = await coordinator.ResolveAsync(
            new CardRecoveryAttemptKey(CardProcessorKind.Square, Guid.NewGuid()),
            CardRecoverySupervisorDecision.ContinueWaiting,
            "reason",
            evidence: null,
            reference: null,
            new PosCartService(),
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.Equal(0, squareService.ResolveAttemptCallCount);
    }

    private static CardPaymentRecoveryService CreateLinklyService(
        FakeLinklyAttemptRepository repository) =>
        new(
            repository,
            new FakeSettingsProvider(CardProcessorKind.Linkly),
            null!,
            new CashCheckoutService(),
            null!,
            null!);

    private static LocalCardPaymentAttempt CreateLinklyAttempt(
        Guid attemptGuid,
        LocalCardPaymentAttemptStatus status,
        string operationKind,
        string cashierId,
        DateTimeOffset updatedAt)
    {
        return new LocalCardPaymentAttempt(
            attemptGuid,
            operationKind == "ActiveSession" ? "SESSION" : null,
            "TXN",
            "Linkly",
            "Sandbox",
            LinklyConnectionMode.CloudBackendAsync.ToString(),
            "P",
            12.34m,
            status,
            "{}",
            "S001",
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
        DateTimeOffset updatedAt)
    {
        return new LocalSquarePaymentAttempt(
            attemptGuid,
            null,
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
            "{}",
            "S001",
            "POS-01",
            cashierId,
            null,
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

        public Task UpdateSessionAsync(
            Guid attemptGuid,
            string sessionId,
            string? txnRef,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateOutcomeAsync(
            Guid attemptGuid,
            LocalCardPaymentAttemptStatus status,
            string? responseCode,
            string? responseText,
            string? paymentReference,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default) =>
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

        public Task CreateAsync(LocalSquarePaymentAttempt attempt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> TryRecordRefundResponseAsync(
            Guid attemptGuid,
            string submissionToken,
            string refundId,
            string refundStatus,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task MarkCheckoutCreatedAsync(
            Guid attemptGuid,
            string checkoutId,
            string? checkoutStatus,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateCheckoutStatusAsync(
            Guid attemptGuid,
            LocalSquarePaymentAttemptStatus status,
            string? checkoutStatus,
            string? cancelReason,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkPaymentVerifiedAsync(
            Guid attemptGuid,
            string paymentId,
            string paymentStatus,
            string? responseCode,
            string? responseText,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkFailedAsync(
            Guid attemptGuid,
            LocalSquarePaymentAttemptStatus status,
            string? checkoutStatus,
            string? paymentStatus,
            string? responseCode,
            string? responseText,
            DateTimeOffset resolvedAt,
            CancellationToken cancellationToken = default,
            string? cancelReason = null) =>
            Task.CompletedTask;

        public Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeSquareRecoveryService : ISquarePaymentRecoveryService
    {
        public int RecoverAttemptCallCount { get; private set; }

        public int ResolveAttemptCallCount { get; private set; }

        public Task<CardPaymentRecoveryResult> RecoverLatestAsync(
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CardPaymentRecoveryResult.None);

        public Task<CardRefundSupervisorResolutionResult> ResolveRefundAsync(
            CardRefundSupervisorResolution resolution,
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CardRefundSupervisorResolutionResult(false, "unavailable"));

        public Task<IReadOnlyList<CardRecoveryQueueItem>> ListOpenAsync(
            PosSessionState session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CardRecoveryQueueItem>>([]);

        public Task<CardPaymentRecoveryResult> RecoverAttemptAsync(
            Guid attemptGuid,
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            RecoverAttemptCallCount++;
            return Task.FromResult(CardPaymentRecoveryResult.None);
        }

        public Task<CardRecoveryResolutionResult> ResolveAttemptAsync(
            Guid attemptGuid,
            CardRecoverySupervisorDecision decision,
            string reason,
            string? evidence,
            string? reference,
            PosCartService cart,
            PosSessionState session,
            CancellationToken cancellationToken = default)
        {
            ResolveAttemptCallCount++;
            return Task.FromResult(new CardRecoveryResolutionResult(false, "unavailable"));
        }
    }
}
