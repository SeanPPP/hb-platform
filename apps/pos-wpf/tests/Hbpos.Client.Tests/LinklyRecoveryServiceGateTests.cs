using System.Text.Json;
using BlazorApp.Shared.DTOs;
using Hbpos.Contracts.Catalog;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Linkly;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class LinklyRecoveryServiceGateTests
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
    public async Task RecoverAttempt_rejects_terminal_attempt_without_marking_recovering()
    {
        var now = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
        var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.Declined, now);
        var repository = new RecordingAttemptRepository(attempt);
        var service = CreateService(repository);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.None, result.Outcome);
        Assert.Equal(0, repository.MarkRecoveringCount);
    }

    [Fact]
    public async Task ListOpen_maps_finalize_pending_status_to_queue_phase()
    {
        var now = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
        var attempt = CreateAttempt(
            LocalCardPaymentAttemptStatus.Recovering,
            now,
            recoveryPhase: CardRecoveryPhases.FinalizePending,
            recoveryTargetStatus: LocalCardPaymentAttemptStatus.OrderCompleted.ToString());
        var repository = new RecordingAttemptRepository(attempt);
        var service = CreateService(repository);

        var items = await service.ListOpenAsync(Session);

        var item = Assert.Single(items);
        Assert.Equal(CardRecoveryPhases.FinalizePending, item.Status);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering.ToString(), attempt.Status.ToString());
    }

    [Fact]
    public async Task RecoverAttempt_finalize_pending_matching_order_only_finalizes_without_backend_or_cart_publication()
    {
        var now = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
        var attemptGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var orderGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var draft = CreateDraft(orderGuid);
        var attempt = CreateAttempt(
                LocalCardPaymentAttemptStatus.Approved,
                now,
                recoveryPhase: CardRecoveryPhases.FinalizePending,
                recoveryTargetStatus: LocalCardPaymentAttemptStatus.OrderCompleted.ToString()) with
        {
            AttemptGuid = attemptGuid,
            ResponseCode = "00",
            ResponseText = "APPROVED",
            PaymentReference = "ANZ:TXN-001",
            OrderDraftJson = JsonSerializer.Serialize(draft, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var repository = new RecordingAttemptRepository(attempt);
        var backend = new FakeBackendTerminalClient();
        var orders = new FakeOrderRepository(CreateExistingOrder(
            orderGuid,
            $"CARD_ATTEMPT:{attemptGuid:N}"));
        var service = CreateService(repository, backend, orders);
        var cart = new PosCartService();

        var result = await service.RecoverAttemptAsync(attemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(orderGuid, result.Order?.OrderGuid);
        Assert.Equal(1, orders.GetOrderCount);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.StatusCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(1, repository.FinalizeRecoveryCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, repository.CurrentAttempt.Status);
        Assert.Equal(CardRecoveryPhases.None, repository.CurrentAttempt.RecoveryPhase);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task RecoverAttempt_finalize_pending_accepts_already_applied_terminal_winner_after_cas_false()
    {
        var now = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
        var attemptGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var orderGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var draft = CreateDraft(orderGuid);
        var attempt = CreateAttempt(
                LocalCardPaymentAttemptStatus.Approved,
                now,
                recoveryPhase: CardRecoveryPhases.FinalizePending,
                recoveryTargetStatus: LocalCardPaymentAttemptStatus.OrderCompleted.ToString()) with
        {
            AttemptGuid = attemptGuid,
            ResponseCode = "00",
            ResponseText = "APPROVED",
            PaymentReference = "ANZ:TXN-001",
            OrderDraftJson = JsonSerializer.Serialize(draft, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var repository = new RecordingAttemptRepository(attempt)
        {
            SimulateFinalizeWinner = true
        };
        var backend = new FakeBackendTerminalClient();
        var orders = new FakeOrderRepository(CreateExistingOrder(
            orderGuid,
            $"CARD_ATTEMPT:{attemptGuid:N}"));
        var service = CreateService(repository, backend, orders);
        var cart = new PosCartService();

        var result = await service.RecoverAttemptAsync(attemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, repository.CurrentAttempt.Status);
        Assert.Equal(CardRecoveryPhases.None, repository.CurrentAttempt.RecoveryPhase);
        Assert.Equal(0, backend.StatusCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task RecoverAttempt_finalize_pending_rejects_non_card_payment_with_attempt_key()
    {
        var now = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
        var attemptGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var orderGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var draft = CreateDraft(orderGuid);
        var attempt = CreateAttempt(
                LocalCardPaymentAttemptStatus.Approved,
                now,
                recoveryPhase: CardRecoveryPhases.FinalizePending,
                recoveryTargetStatus: LocalCardPaymentAttemptStatus.OrderCompleted.ToString()) with
        {
            AttemptGuid = attemptGuid,
            ResponseCode = "00",
            ResponseText = "APPROVED",
            PaymentReference = "ANZ:TXN-001",
            OrderDraftJson = JsonSerializer.Serialize(draft, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var repository = new RecordingAttemptRepository(attempt);
        var backend = new FakeBackendTerminalClient();
        var orders = new FakeOrderRepository(CreateExistingOrder(
            orderGuid,
            $"CARD_ATTEMPT:{attemptGuid:N}",
            PaymentMethodKind.Cash));
        var service = CreateService(repository, backend, orders);
        var cart = new PosCartService();

        var result = await service.RecoverAttemptAsync(attemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(0, repository.FinalizeRecoveryCount);
        Assert.Equal(0, backend.StatusCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.True(cart.IsEmpty);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, repository.CurrentAttempt.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, repository.CurrentAttempt.RecoveryPhase);
    }

    [Fact]
    public async Task RecoverAttempt_historical_abandoned_not_paid_only_retries_acknowledgement()
    {
        var now = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
        var attempt = CreateAttempt(LocalCardPaymentAttemptStatus.Abandoned, now) with
        {
            ResponseCode = ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
            ResponseText = "confirmed not paid",
            RecoveryPhase = CardRecoveryPhases.None,
            RecoveryTargetStatus = null,
            AcknowledgedAt = null
        };
        var repository = new RecordingAttemptRepository(attempt);
        var backend = new FakeBackendTerminalClient();
        var service = CreateService(repository, backend);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.None, result.Outcome);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal(0, repository.FinalizeRecoveryCount);
        Assert.Equal(0, repository.FinalizeSupervisorNotPaidCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned, repository.CurrentAttempt.Status);
        Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, repository.CurrentAttempt.ResponseCode);
        Assert.NotNull(repository.CurrentAttempt.AcknowledgedAt);
        Assert.Equal(CardRecoveryPhases.None, repository.CurrentAttempt.RecoveryPhase);
    }

    [Fact]
    public async Task RecoverAttempt_finalize_pending_semantically_invalid_draft_returns_unknown_without_side_effects()
    {
        var now = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
        var draft = CreateDraft(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var invalidDraft = draft with
        {
            CartSnapshot = draft.CartSnapshot with
            {
                Lines = [draft.CartSnapshot.Lines[0] with { Quantity = 0m }]
            }
        };
        var attempt = CreateAttempt(
                LocalCardPaymentAttemptStatus.Approved,
                now,
                recoveryPhase: CardRecoveryPhases.FinalizePending,
                recoveryTargetStatus: LocalCardPaymentAttemptStatus.OrderCompleted.ToString()) with
        {
            ResponseCode = "00",
            ResponseText = "APPROVED",
            OrderDraftJson = JsonSerializer.Serialize(
                invalidDraft,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var repository = new RecordingAttemptRepository(attempt);
        var backend = new FakeBackendTerminalClient();
        var orders = new FakeOrderRepository();
        var service = CreateService(repository, backend, orders);
        var cart = new PosCartService();

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(0, orders.GetOrderCount);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.StatusCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.True(cart.IsEmpty);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, repository.CurrentAttempt.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, repository.CurrentAttempt.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted.ToString(), repository.CurrentAttempt.RecoveryTargetStatus);
    }

    [Fact]
    public async Task RecoverAttempt_linkly_partial_refund_keeps_recovery_owner_until_order_save_cas()
    {
        var now = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
        var attemptGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var draft = CreateDraft(Guid.Parse("11111111-2222-3333-4444-555555555555")) with
        {
            ActualAmount = -10m,
            CardAmount = 5m,
            TxnType = "R",
            OriginalReference = "ANZ:ORIGINAL-REFUND"
        };
        var attempt = CreateAttempt(
                LocalCardPaymentAttemptStatus.Approved,
                now,
                recoveryPhase: CardRecoveryPhases.FinalizePending,
                recoveryTargetStatus: LocalCardPaymentAttemptStatus.OrderCompleted.ToString()) with
        {
            AttemptGuid = attemptGuid,
            OperationKind = "Refund",
            ResponseCode = "SUPERVISOR_CONFIRMED_REFUNDED",
            ResponseText = "confirmed refunded",
            OrderDraftJson = JsonSerializer.Serialize(
                draft,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        var repository = new RecordingAttemptRepository(attempt);
        var orders = new FakeOrderRepository();
        var service = CreateService(repository, new FakeBackendTerminalClient(), orders);
        var cart = new PosCartService();

        var result = await service.RecoverAttemptAsync(attemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        Assert.Equal(attemptGuid, cart.RecoveryOwnerAttemptGuid);
        Assert.Equal(0, repository.FinalizeRecoveryCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, repository.CurrentAttempt.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, repository.CurrentAttempt.RecoveryPhase);
        Assert.Equal(0, orders.SaveCount);
    }

    private static CardPaymentRecoveryService CreateService(
        RecordingAttemptRepository repository,
        FakeBackendTerminalClient? backend = null,
        FakeOrderRepository? orders = null)
    {
        return new CardPaymentRecoveryService(
            repository,
            new FakeSettingsProvider(),
            backend ?? new FakeBackendTerminalClient(),
            new CashCheckoutService(),
            orders ?? new FakeOrderRepository(),
            new FakeSyncQueueRepository());
    }

    private static CardPaymentOrderDraft CreateDraft(Guid orderGuid)
    {
        var cart = new PosCartService();
        cart.AddItem(new SellableItemDto(
            "S001",
            "SKU-10",
            null,
            "Test Item",
            "930010",
            "ITEM-10",
            null,
            10m,
            PriceSourceKind.StoreRetailPrice,
            "Store price",
            1m,
            DateTimeOffset.Parse("2026-06-05T10:00:00+10:00")));
        return new CardPaymentOrderDraft(
            orderGuid,
            Session,
            cart.CreateSnapshot(),
            [],
            10m,
            10m,
            "P",
            null,
            DateTimeOffset.Parse("2026-06-05T10:00:00+10:00"));
    }

    private static LocalOrder CreateExistingOrder(
        Guid orderGuid,
        string paymentIdempotencyKey,
        PaymentMethodKind paymentMethod = PaymentMethodKind.Card)
    {
        return new LocalOrder(
            orderGuid,
            "S001",
            "POS-01",
            "C001",
            "Alice",
            DateTimeOffset.Parse("2026-06-05T10:00:00+10:00"),
            10m,
            0m,
            10m,
            [
                new LocalOrderLine(
                    Guid.NewGuid(),
                    "SKU-10",
                    null,
                    "Test Item",
                    "930010",
                    "ITEM-10",
                    1m,
                    10m,
                    0m,
                    10m,
                    PriceSourceKind.StoreRetailPrice)
            ],
            [
                new LocalPayment(
                    Guid.NewGuid(),
                    paymentMethod,
                    10m,
                    "ANZ:TXN-001",
                    IdempotencyKey: paymentIdempotencyKey)
            ]);
    }

    private static LocalCardPaymentAttempt CreateAttempt(
        LocalCardPaymentAttemptStatus status,
        DateTimeOffset updatedAt,
        string recoveryPhase = CardRecoveryPhases.None,
        string? recoveryTargetStatus = null)
    {
        return new LocalCardPaymentAttempt(
            Guid.NewGuid(),
            "SESSION-001",
            "TXN-001",
            "Linkly",
            "Sandbox",
            "CloudBackendAsync",
            "P",
            10m,
            status,
            "{}",
            "S001",
            "POS-01",
            "C001",
            null,
            null,
            null,
            updatedAt.AddMinutes(-1),
            updatedAt,
            null,
            null,
            "Sale",
            null,
            null,
            null,
            recoveryPhase,
            recoveryTargetStatus);
    }

    private sealed class RecordingAttemptRepository(
        params LocalCardPaymentAttempt[] attempts) : ILocalCardPaymentAttemptRepository
    {
        private readonly List<LocalCardPaymentAttempt> _attempts = [.. attempts];

        public int MarkRecoveringCount { get; private set; }

        public int FinalizeRecoveryCount { get; private set; }

        public int FinalizeSupervisorNotPaidCount { get; private set; }

        public bool SimulateFinalizeWinner { get; init; }

        public LocalCardPaymentAttempt CurrentAttempt => _attempts.Single();

        public Task<LocalCardPaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(_attempts.FirstOrDefault(attempt => attempt.AttemptGuid == attemptGuid));

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

        public Task CreateAsync(LocalCardPaymentAttempt attempt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateSessionAsync(Guid attemptGuid, string sessionId, string? txnRef, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateOutcomeAsync(Guid attemptGuid, LocalCardPaymentAttemptStatus status, string? responseCode, string? responseText, string? paymentReference, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkAcknowledgedAsync(Guid attemptGuid, DateTimeOffset acknowledgedAt, CancellationToken cancellationToken = default)
        {
            var index = _attempts.FindIndex(attempt => attempt.AttemptGuid == attemptGuid);
            if (index >= 0)
            {
                _attempts[index] = _attempts[index] with
                {
                    AcknowledgedAt = acknowledgedAt,
                    UpdatedAt = acknowledgedAt
                };
            }

            return Task.CompletedTask;
        }

        public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
        {
            MarkRecoveringCount++;
            return Task.CompletedTask;
        }

        public Task<bool> TryFinalizeRecoveryOutcomeAsync(
            Guid attemptGuid,
            LocalCardPaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            LocalCardPaymentAttemptStatus recoveryTargetStatus,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            var index = _attempts.FindIndex(attempt =>
                attempt.AttemptGuid == attemptGuid &&
                attempt.Status == expectedStatus &&
                attempt.UpdatedAt == expectedUpdatedAt &&
                string.Equals(attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal) &&
                string.Equals(attempt.RecoveryTargetStatus, recoveryTargetStatus.ToString(), StringComparison.Ordinal));
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _attempts[index] = _attempts[index] with
            {
                Status = recoveryTargetStatus,
                RecoveryPhase = CardRecoveryPhases.None,
                RecoveryTargetStatus = null,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            };
            if (SimulateFinalizeWinner)
            {
                return Task.FromResult(false);
            }

            FinalizeRecoveryCount++;
            return Task.FromResult(true);
        }

        public Task<bool> TryFinalizeSupervisorNotPaidAndAcknowledgeAsync(
            Guid attemptGuid,
            LocalCardPaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            var index = _attempts.FindIndex(attempt =>
                attempt.AttemptGuid == attemptGuid &&
                attempt.Status == expectedStatus &&
                attempt.UpdatedAt == expectedUpdatedAt &&
                string.Equals(
                    attempt.ResponseCode,
                    ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
                    StringComparison.Ordinal) &&
                string.Equals(attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal) &&
                string.Equals(
                    attempt.RecoveryTargetStatus,
                    LocalCardPaymentAttemptStatus.Abandoned.ToString(),
                    StringComparison.Ordinal) &&
                attempt.AcknowledgedAt is null);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            FinalizeSupervisorNotPaidCount++;
            _attempts[index] = _attempts[index] with
            {
                Status = LocalCardPaymentAttemptStatus.Abandoned,
                RecoveryPhase = CardRecoveryPhases.None,
                RecoveryTargetStatus = null,
                CompletedAt = completedAt,
                AcknowledgedAt = completedAt,
                UpdatedAt = completedAt
            };
            return Task.FromResult(true);
        }

        public Task<bool> TryMarkAcknowledgedAsync(
            Guid attemptGuid,
            LocalCardPaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset acknowledgedAt,
            CancellationToken cancellationToken = default)
        {
            var index = _attempts.FindIndex(attempt =>
                attempt.AttemptGuid == attemptGuid &&
                attempt.Status == expectedStatus &&
                attempt.UpdatedAt == expectedUpdatedAt &&
                attempt.AcknowledgedAt is null &&
                !string.Equals(attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal));
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _attempts[index] = _attempts[index] with
            {
                AcknowledgedAt = acknowledgedAt,
                UpdatedAt = acknowledgedAt
            };
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSettingsProvider : ICardTerminalSettingsProvider
    {
        public Task<CardTerminalSettings> GetSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CardTerminalSettings(
                CardProcessorKind.Linkly,
                CardTerminalEnvironment.Sandbox,
                "127.0.0.1",
                2011,
                null,
                null,
                null,
                "https://rest.pos.cloud.pceftpos.com/v1/",
                TimeSpan.FromSeconds(30),
                LinklyConnectionMode.CloudBackendAsync));
    }

    private sealed class FakeBackendTerminalClient : ILinklyBackendTerminalClient
    {
        public int StatusCallCount { get; private set; }

        public int AcknowledgeCallCount { get; private set; }

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

        public Task<LinklyCloudBackendSessionResponse> GetSessionStatusAsync(CardTerminalSettings settings, string sessionId, CancellationToken cancellationToken = default)
        {
            StatusCallCount++;
            throw new NotSupportedException();
        }

        public Task AcknowledgeSessionAsync(CardTerminalSettings settings, string sessionId, CancellationToken cancellationToken = default)
        {
            AcknowledgeCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOrderRepository(params LocalOrder[] orders) : ILocalOrderRepository
    {
        private readonly IReadOnlyList<LocalOrder> _orders = orders;

        public int GetOrderCount { get; private set; }

        public int SaveCount { get; private set; }

        public Task SavePendingOrderAsync(LocalOrder order, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(int take = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(LocalOrderHistoryQuery query, int take = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);

        public Task<LocalOrder?> GetOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            GetOrderCount++;
            return Task.FromResult<LocalOrder?>(_orders.FirstOrDefault(order => order.OrderGuid == orderGuid));
        }
    }

    private sealed class FakeSyncQueueRepository : ISyncQueueRepository
    {
        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<SyncQueueOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SyncQueueListItem>> GetActiveItemsAsync(int take = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SyncQueueListItem>>([]);
    }
}
