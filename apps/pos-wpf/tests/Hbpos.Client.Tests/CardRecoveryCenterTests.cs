using System.Text.Json;
using System.IO;
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

    [Fact]
    public async Task Linkly_ResolveAttemptAsync_sale_resolves_when_settings_is_square()
    {
        var attempt = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000051"),
            LocalCardPaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"));
        var repository = new FakeLinklyAttemptRepository(attempt);
        var service = CreateLinklyService(repository, settingsProcessor: CardProcessorKind.Square);

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ContinueWaiting,
            "wait",
            evidence: null,
            reference: null,
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.NotNull(repository.LastPaymentResolution);
        Assert.Equal(attempt.AttemptGuid, repository.LastPaymentResolution!.AttemptGuid);
    }

    [Fact]
    public async Task Linkly_ResolveAttemptAsync_active_session_resolves_when_settings_is_square()
    {
        var attempt = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000052"),
            LocalCardPaymentAttemptStatus.Recovering,
            "ActiveSession",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"),
            sessionId: "SESSION-X");
        var repository = new FakeLinklyAttemptRepository(attempt);
        var service = CreateLinklyService(repository, settingsProcessor: CardProcessorKind.Square);

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ContinueWaiting,
            "wait",
            evidence: null,
            reference: null,
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.NotNull(repository.LastPaymentResolution);
        Assert.Equal(attempt.AttemptGuid, repository.LastPaymentResolution!.AttemptGuid);
    }

    [Fact]
    public async Task Linkly_ResolveAttemptAsync_refund_resolves_when_settings_is_square()
    {
        var attempt = CreateLinklyAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000053"),
            LocalCardPaymentAttemptStatus.Recovering,
            "Refund",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"));
        var repository = new FakeLinklyAttemptRepository(attempt);
        var service = CreateLinklyService(repository, settingsProcessor: CardProcessorKind.Square);

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ContinueWaiting,
            "wait",
            evidence: null,
            reference: null,
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.NotNull(repository.LastRefundResolution);
        Assert.Equal(attempt.AttemptGuid, repository.LastRefundResolution!.AttemptGuid);
    }

    [Fact]
    public async Task Square_ResolveAttemptAsync_refund_resolves_when_settings_is_linkly()
    {
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000054"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Refund",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"));
        var repository = new FakeSquareAttemptRepository(attempt);
        var service = CreateSquareService(repository, settingsProcessor: CardProcessorKind.Linkly);

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ContinueWaiting,
            "wait",
            evidence: null,
            reference: null,
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.NotNull(repository.LastRefundResolution);
        Assert.Equal(attempt.AttemptGuid, repository.LastRefundResolution!.AttemptGuid);
    }

    [Fact]
    public async Task Square_ResolveAttemptAsync_sale_confirm_processed_propagates_unknown_outcome()
    {
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000055"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"),
            draftJson: SerializeDraft());
        var repository = new FakeSquareAttemptRepository(attempt);
        var orders = new FakeOrderRepository { SaveException = new IOException("disk full") };
        var service = CreateSquareService(repository, orderRepository: orders);

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmProcessed,
            "paid",
            "bank matched",
            "ref-1",
            new PosCartService(),
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.NotNull(repository.LastPaymentResolution);
    }

    [Fact]
    public async Task Square_ResolveAttemptAsync_sale_confirm_not_processed_race_keeps_lock()
    {
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000056"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"),
            draftJson: SerializeDraft());
        var repository = new FakeSquareAttemptRepository(attempt);
        var cart = new PosCartService();
        repository.BeforeResolvePayment = () => cart.RestoreSnapshot(NonEmptySnapshot());
        var service = CreateSquareService(repository);

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmNotProcessed,
            "not paid",
            "bank none",
            reference: null,
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.False(result.RetryAllowed);
        Assert.True(result.LockRetained);
        Assert.False(cart.IsEmpty);
    }

    [Fact]
    public async Task Square_ResolveAttemptAsync_sale_confirm_processed_completes_order_preserving_current_cart()
    {
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000057"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"),
            draftJson: SerializeDraft());
        var repository = new FakeSquareAttemptRepository(attempt);
        var orders = new FakeOrderRepository();
        var service = CreateSquareService(repository, orderRepository: orders);
        var currentCart = CreateNonEmptyCart();

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmProcessed,
            "paid",
            "bank matched",
            "ref-1",
            currentCart,
            Session);

        Assert.True(result.Succeeded);
        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.RecoveryResult!.Outcome);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(1, repository.MarkOrderCompletedCount);
        Assert.False(currentCart.IsEmpty);
        Assert.Equal("SKU-NEW", currentCart.Lines[0].ProductCode);
    }

    [Fact]
    public async Task Square_repository_ResolvePaymentWithJournalAsync_default_throws()
    {
        ILocalSquarePaymentAttemptRepository repository = new DefaultThrowingSquareAttemptRepository();
        var resolution = new SquarePaymentResolution(
            Guid.NewGuid(),
            CardRecoverySupervisorDecision.ContinueWaiting,
            "wait",
            Evidence: null,
            PaymentReference: null,
            LocalSquarePaymentAttemptStatus.Recovering,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => repository.ResolvePaymentWithJournalAsync(resolution, null!, CancellationToken.None));
    }

    [Fact]
    public async Task Square_ResolveAttemptAsync_sale_confirm_not_processed_terminalizes_old_attempt()
    {
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000061"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"),
            draftJson: SerializeDraft());
        var repository = new FakeSquareAttemptRepository(attempt);
        var service = CreateSquareService(repository);

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmNotProcessed,
            "not paid",
            "bank none",
            reference: null,
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult!.Outcome);
        Assert.Equal(1, repository.TerminalizeNotPaidCount);
        var saved = await repository.GetAttemptAsync(attempt.AttemptGuid);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Abandoned, saved!.Status);
        var open = await service.ListOpenAsync(Session);
        Assert.Empty(open);
    }

    [Fact]
    public async Task Square_RecoverAttemptAsync_sale_confirm_not_processed_race_then_recover()
    {
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000062"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"),
            draftJson: SerializeDraft());
        var repository = new FakeSquareAttemptRepository(attempt);
        var cart = new PosCartService();
        repository.BeforeResolvePayment = () => cart.RestoreSnapshot(NonEmptySnapshot());
        var service = CreateSquareService(repository);

        var raceResult = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmNotProcessed,
            "not paid",
            "bank none",
            reference: null,
            cart,
            Session);
        Assert.False(raceResult.Succeeded);
        Assert.True(raceResult.LockRetained);

        cart.Clear();
        var recoverResult = await service.RecoverAttemptAsync(attempt.AttemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, recoverResult.Outcome);
        Assert.Equal(1, repository.TerminalizeNotPaidCount);
        var saved = await repository.GetAttemptAsync(attempt.AttemptGuid);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Abandoned, saved!.Status);
        var open = await service.ListOpenAsync(Session);
        Assert.Empty(open);
    }

    [Fact]
    public async Task Square_RecoverAttemptAsync_sale_confirm_not_processed_after_reconstruct_service()
    {
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000063"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"),
            draftJson: SerializeDraft());
        var repository = new FakeSquareAttemptRepository(attempt);
        var firstService = CreateSquareService(repository);
        var cart = new PosCartService();
        repository.BeforeResolvePayment = () => cart.RestoreSnapshot(NonEmptySnapshot());

        var raceResult = await firstService.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmNotProcessed,
            "not paid",
            "bank none",
            reference: null,
            cart,
            Session);
        Assert.False(raceResult.Succeeded);

        var secondService = CreateSquareService(repository);
        var recoverResult = await secondService.RecoverAttemptAsync(attempt.AttemptGuid, new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, recoverResult.Outcome);
        var saved = await repository.GetAttemptAsync(attempt.AttemptGuid);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Abandoned, saved!.Status);
    }

    [Fact]
    public async Task Square_RecoverAttemptAsync_sale_confirm_not_processed_without_checkout_id()
    {
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000064"),
            LocalSquarePaymentAttemptStatus.Pending,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"),
            draftJson: SerializeDraft()) with
        {
            ResponseCode = ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid
        };
        var repository = new FakeSquareAttemptRepository(attempt);
        var service = CreateSquareService(repository);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        Assert.Equal(1, repository.TerminalizeNotPaidCount);
    }

    [Fact]
    public async Task Square_ResolveAttemptAsync_sale_confirm_processed_preserves_reference_in_tender_and_transaction()
    {
        var attempt = CreateSquareAttempt(
            Guid.Parse("30000000-0000-0000-0000-000000000065"),
            LocalSquarePaymentAttemptStatus.Recovering,
            "Sale",
            "C001",
            DateTimeOffset.Parse("2026-06-05T09:00:00+10:00"),
            draftJson: SerializeDraft());
        var repository = new FakeSquareAttemptRepository(attempt);
        var orders = new FakeOrderRepository();
        var service = CreateSquareService(repository, orderRepository: orders);

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmProcessed,
            "paid",
            "bank matched",
            "ref-1",
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.RecoveryResult!.Outcome);
        Assert.NotNull(orders.SavedOrder);
        var payment = Assert.Single(orders.SavedOrder!.Payments);
        Assert.Equal("SQ:ref-1", payment.Reference);
        var transaction = Assert.Single(payment.CardTransactions!);
        Assert.Equal("ref-1", transaction.TxnRef);
    }

    private static CardPaymentRecoveryService CreateLinklyService(
        FakeLinklyAttemptRepository repository,
        ILinklyBackendTerminalClient? backend = null,
        CardProcessorKind settingsProcessor = CardProcessorKind.Linkly) =>
        new(
            repository,
            new FakeSettingsProvider(settingsProcessor),
            backend ?? new FakeLinklyBackendTerminalClient(),
            new CashCheckoutService(),
            null!,
            null!);

    private static SquarePaymentRecoveryService CreateSquareService(
        FakeSquareAttemptRepository repository,
        ILocalOrderRepository? orderRepository = null,
        CardProcessorKind settingsProcessor = CardProcessorKind.Square) =>
        new(
            repository,
            new FakeSettingsProvider(settingsProcessor),
            null!,
            new CashCheckoutService(),
            orderRepository ?? null!);

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
        cart.RestoreSnapshot(NonEmptySnapshot());
        return cart;
    }

    private static PosCartSnapshot NonEmptySnapshot()
    {
        return new PosCartSnapshot(
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
        ]);
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

        public ActiveSessionResolution? LastPaymentResolution { get; private set; }

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

        public Task<bool> ResolvePaymentWithJournalAsync(
            ActiveSessionResolution resolution,
            LocalFinancialSupervisorResolution journal,
            CancellationToken cancellationToken = default)
        {
            LastPaymentResolution = resolution;
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

        public CardRefundAttemptResolution? LastRefundResolution { get; private set; }

        public Action? BeforeResolvePayment { get; set; }

        public int MarkOrderCompletedCount { get; private set; }

        public int TerminalizeNotPaidCount { get; private set; }

        public Guid? LastTerminalizedAttemptGuid { get; private set; }

        public Task<IReadOnlyList<LocalSquarePaymentAttempt>> GetOpenAttemptsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalSquarePaymentAttempt>>(
                _attempts.Where(attempt => attempt.Status != LocalSquarePaymentAttemptStatus.Abandoned).ToArray());

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
            BeforeResolvePayment?.Invoke();
            LastPaymentResolution = resolution;
            var existing = _attempts.FirstOrDefault(attempt => attempt.AttemptGuid == resolution.AttemptGuid);
            if (existing is not null)
            {
                var updated = resolution.Decision switch
                {
                    CardRecoverySupervisorDecision.ConfirmProcessed => existing with
                    {
                        Status = LocalSquarePaymentAttemptStatus.PaymentVerified,
                        PaymentId = resolution.PaymentReference ?? ActiveSessionSupervisorResolutionCodes.ConfirmedPaid,
                        PaymentStatus = ActiveSessionSupervisorResolutionCodes.ConfirmedPaid,
                        ResponseCode = ActiveSessionSupervisorResolutionCodes.ConfirmedPaid
                    },
                    CardRecoverySupervisorDecision.ConfirmNotProcessed => existing with
                    {
                        Status = LocalSquarePaymentAttemptStatus.Pending,
                        CheckoutId = null,
                        PaymentId = null,
                        PaymentStatus = null,
                        ResponseCode = ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid
                    },
                    _ => existing with
                    {
                        Status = LocalSquarePaymentAttemptStatus.Recovering,
                        ResponseCode = ActiveSessionSupervisorResolutionCodes.ContinueWaiting
                    }
                };
                ReplaceAttempt(updated);
            }

            return Task.FromResult(true);
        }

        public Task<bool> TryTerminalizeNotPaidAsync(
            Guid attemptGuid,
            LocalSquarePaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset resolvedAt,
            CancellationToken cancellationToken = default)
        {
            TerminalizeNotPaidCount++;
            LastTerminalizedAttemptGuid = attemptGuid;
            var existing = _attempts.FirstOrDefault(attempt => attempt.AttemptGuid == attemptGuid);
            if (existing is not null)
            {
                ReplaceAttempt(existing with
                {
                    Status = LocalSquarePaymentAttemptStatus.Abandoned,
                    ResolvedAt = resolvedAt,
                    UpdatedAt = resolvedAt
                });
            }

            return Task.FromResult(true);
        }

        public Task<bool> ResolveRefundWithJournalAsync(
            CardRefundAttemptResolution resolution,
            LocalFinancialSupervisorResolution journal,
            CancellationToken cancellationToken = default)
        {
            LastRefundResolution = resolution;
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

        public Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
        {
            MarkOrderCompletedCount++;
            return Task.CompletedTask;
        }

        private void ReplaceAttempt(LocalSquarePaymentAttempt attempt)
        {
            var index = _attempts.FindIndex(candidate => candidate.AttemptGuid == attempt.AttemptGuid);
            if (index >= 0)
            {
                _attempts[index] = attempt;
            }
            else
            {
                _attempts.Add(attempt);
            }
        }
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

    private sealed class FakeOrderRepository : ILocalOrderRepository
    {
        public int SaveCount { get; private set; }

        public Exception? SaveException { get; init; }

        public LocalOrder? SavedOrder { get; private set; }

        public Task SavePendingOrderAsync(LocalOrder order, CancellationToken cancellationToken = default)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SaveCount++;
            SavedOrder = order;
            return Task.CompletedTask;
        }

        public async Task SavePendingOrderWithHeldSourceAsync(
            LocalOrder order,
            LocalHeldOrderCompletionContext heldOrder,
            CancellationToken cancellationToken = default)
        {
            await SavePendingOrderAsync(order, cancellationToken);
        }

        public Task UpdatePaymentReferenceAsync(Guid paymentGuid, string? reference, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(int take = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(LocalOrderHistoryQuery query, int take = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);

        public Task<LocalOrder?> GetOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult<LocalOrder?>(null);
    }

    private sealed class DefaultThrowingSquareAttemptRepository : ILocalSquarePaymentAttemptRepository
    {
        public Task CreateAsync(LocalSquarePaymentAttempt attempt, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> TryRecordRefundResponseAsync(Guid attemptGuid, string submissionToken, string refundId, string refundStatus, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task MarkCheckoutCreatedAsync(Guid attemptGuid, string checkoutId, string? checkoutStatus, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task UpdateCheckoutStatusAsync(Guid attemptGuid, LocalSquarePaymentAttemptStatus status, string? checkoutStatus, string? cancelReason, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task MarkPaymentVerifiedAsync(Guid attemptGuid, string paymentId, string paymentStatus, string? responseCode, string? responseText, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task MarkFailedAsync(Guid attemptGuid, LocalSquarePaymentAttemptStatus status, string? checkoutStatus, string? paymentStatus, string? responseCode, string? responseText, DateTimeOffset resolvedAt, CancellationToken cancellationToken = default, string? cancelReason = null) =>
            throw new NotImplementedException();

        public Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<LocalSquarePaymentAttempt?> GetLatestOpenAttemptAsync(string storeCode, string deviceCode, string? cashierId, string environment, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<LocalSquarePaymentAttempt>> GetOpenRefundAttemptsAsync(string storeCode, string deviceCode, string environment, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<LocalSquarePaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
