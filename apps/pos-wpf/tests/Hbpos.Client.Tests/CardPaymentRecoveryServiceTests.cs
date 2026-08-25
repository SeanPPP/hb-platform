using System.Text.Json;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Linkly;
using Hbpos.Contracts.Orders;
using static Hbpos.Client.Tests.SharedHeldOrderClientTestSupport;

namespace Hbpos.Client.Tests;

public sealed class CardPaymentRecoveryServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PosSessionState Session = new("HB POS", "S001", "Main Branch", "POS-01", "C001", "Alice", true, 0);

    [Fact]
    public async Task RecoverLatestAsync_approved_matching_session_completes_order_and_acknowledges_once()
    {
        var attempt = CreateAttempt(sessionId: "SESSION-001", txnRef: "TXN-001");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-001", txnRef: "TXN-001", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.NotNull(result.Order);
        Assert.Equal(CreateOrderGuid(), result.Order!.OrderGuid);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-001", backend.AcknowledgedSessionId);
        Assert.NotNull(attempts.AcknowledgedAt);
    }

    [Fact]
    public async Task RecoverAttemptAsync_approved_order_save_failure_replays_finalize_pending_without_status_lookup()
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-ORDER-SAVE-FAIL",
            txnRef: "TXN-ORDER-SAVE-FAIL",
            status: LocalCardPaymentAttemptStatus.Approved) with
        {
            ResponseCode = "00",
            ResponseText = "APPROVED",
            PaymentReference = "BANK-ORDER-SAVE-FAIL",
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalCardPaymentAttemptStatus.OrderCompleted.ToString()
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverAttemptAsync(
            attempt.AttemptGuid,
            new PosCartService(),
            Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Equal(0, backend.StatusCallCount);
        Assert.Equal(0, backend.ResumeCallCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_binds_matching_active_claim_from_frozen_draft_snapshot()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var holdGuid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            claimId,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.OfflineOrigin,
            "prepare-card",
            CanonicalForDraftCart(),
            "2026-07-28T00:00:00.000Z")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimId, "prepare-card", "activate-card", serverRevision: null, "2026-07-28T00:00:01.000Z"));

        var draft = CreateDraft();
        var attempt = CreateAttempt(
            sessionId: "SESSION-001",
            txnRef: "TXN-001",
            draft: draft with
            {
                CartSnapshot = draft.CartSnapshot with { SharedHeldOrderClaimId = claimId }
            });
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-001", txnRef: "TXN-001", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend, sharedHeldOrderRepository: scope.Repository);

        // 当前 UI 购物车为空：恢复必须使用 draft 中冻结的 durable claim binding 解析来源。
        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        var heldCompletion = Assert.Single(orders.HeldSources);
        Assert.Equal(result.Order!.OrderGuid, heldCompletion.Order.OrderGuid);
        Assert.Equal(holdGuid, heldCompletion.Context.HoldGuid);
        Assert.Equal(claimId, heldCompletion.Context.ClaimId);
        Assert.Equal(SharedHeldOrderClaimSource.OfflineOrigin, heldCompletion.Context.Source);
        Assert.Equal("prepare-card", heldCompletion.Context.PrepareIdempotencyKey);
        Assert.Equal("activate-card", heldCompletion.Context.ActivateIdempotencyKey);
        Assert.Equal(1, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_with_non_matching_claim_does_not_bind_held_source()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var claimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            claimId,
            Guid.NewGuid(),
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.OfflineOrigin,
            "prepare-card-nomatch",
            CanonicalForDraftCart(quantity: 2m),
            "2026-07-28T00:00:00.000Z")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimId, "prepare-card-nomatch", "activate-card-nomatch", serverRevision: null, "2026-07-28T00:00:01.000Z"));

        var attempt = CreateAttempt(sessionId: "SESSION-NOMATCH", txnRef: "TXN-NOMATCH");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-NOMATCH", txnRef: "TXN-NOMATCH", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend, sharedHeldOrderRepository: scope.Repository);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(1, orders.SaveCount);
        Assert.Empty(orders.HeldSources);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_with_existing_order_skips_bound_claim_resolution()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var holdGuid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            claimId,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.OfflineOrigin,
            "prepare-card-existing",
            CanonicalForDraftCart(),
            "2026-07-28T00:00:00.000Z")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimId,
            "prepare-card-existing",
            "activate-card-existing",
            serverRevision: null,
            "2026-07-28T00:00:01.000Z"));
        // 订单已保存、attempt 未收尾：claim 已绑定并完成（与 LocalOrder 同事务）。
        Assert.True(await scope.Repository.TryBindOrderAsync(
            claimId,
            "activate-card-existing",
            CreateOrderGuid().ToString("D"),
            "2026-07-28T00:00:02.000Z"));
        Assert.True(await scope.Repository.TryCompleteClaimAsync(
            claimId,
            "activate-card-existing",
            "release-card-existing",
            "2026-07-28T00:00:03.000Z"));

        var attempt = CreateAttempt(sessionId: "SESSION-EXISTING", txnRef: "TXN-EXISTING");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository(CreateExistingOrder(
            "CARD_ATTEMPT:aaaaaaaabbbbccccddddeeeeeeeeeeee"));
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus(
                "Completed",
                sessionId: "SESSION-EXISTING",
                txnRef: "TXN-EXISTING",
                responseCode: "00",
                responseText: "APPROVED",
                transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend, sharedHeldOrderRepository: scope.Repository);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        // 既有订单幂等收尾：不再解析已 Completed/bound 的 held claim，直接完成 attempt，
        // 不重复保存订单，也绝不把来源静默降级为普通订单。
        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(CreateOrderGuid(), result.Order!.OrderGuid);
        Assert.Equal(0, orders.SaveCount);
        Assert.Empty(orders.HeldSources);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(1, backend.AcknowledgeCallCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_with_mismatched_existing_order_keeps_finalize_pending()
    {
        var attempt = CreateAttempt(sessionId: "SESSION-EXISTING-MISMATCH", txnRef: "TXN-EXISTING-MISMATCH");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository(CreateExistingOrder(
            "CARD_ATTEMPT:11111111222233334444555555555555"));
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus(
                "Completed",
                sessionId: "SESSION-EXISTING-MISMATCH",
                txnRef: "TXN-EXISTING-MISMATCH",
                responseCode: "00",
                responseText: "APPROVED",
                transactionSuccess: true)
        };
        var cart = new PosCartService();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_approved_binds_matching_active_claim_from_frozen_draft_snapshot()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var holdGuid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            claimId,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.OfflineOrigin,
            "prepare-card-local",
            CanonicalForDraftCart(),
            "2026-07-28T00:00:00.000Z")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimId, "prepare-card-local", "activate-card-local", serverRevision: null, "2026-07-28T00:00:01.000Z"));

        var draft = CreateDraft();
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "LOCAL-TXN-001",
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft with
            {
                CartSnapshot = draft.CartSnapshot with { SharedHeldOrderClaimId = claimId }
            });
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:LOCAL-TXN-001",
            "ANZ Linkly",
            10m,
            [CreateLocalCardTransaction("LOCAL-TXN-001", "00", "APPROVED")],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "LOCAL-TXN-001",
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal,
            sharedHeldOrderRepository: scope.Repository);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        var heldCompletion = Assert.Single(orders.HeldSources);
        Assert.Equal(result.Order!.OrderGuid, heldCompletion.Order.OrderGuid);
        Assert.Equal(holdGuid, heldCompletion.Context.HoldGuid);
        Assert.Equal(claimId, heldCompletion.Context.ClaimId);
        Assert.Equal(1, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_partial_amount_restores_tender_without_saving_or_acknowledging()
    {
        var draft = CreateDraft(cardAmount: 5m);
        var attempt = CreateAttempt(
            sessionId: "SESSION-PARTIAL",
            txnRef: "TXN-PARTIAL",
            draft: draft,
            amount: 5m);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-PARTIAL", txnRef: "TXN-PARTIAL", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var cart = new PosCartService();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        var restoredTender = Assert.Single(result.RestoredTenders!);
        Assert.Equal(PaymentMethodKind.Card, restoredTender.Method);
        Assert.Equal(5m, restoredTender.Amount);
        Assert.Equal("CARD_ATTEMPT:aaaaaaaabbbbccccddddeeeeeeeeeeee", restoredTender.IdempotencyKey);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Null(attempts.AcknowledgedAt);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted.ToString(), attempts.RecoveryTargetStatus);
        Assert.Single(cart.Lines);

        // UI 投影失败时只能回滚本次 publication；金融证据和 FinalizePending 必须保持开放。
        Assert.True(cart.RollbackRecoveryPublication(attempt.AttemptGuid).Succeeded);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        Assert.True(cart.IsEmpty);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted.ToString(), attempts.RecoveryTargetStatus);

        // 重放只沿用已持久化的批准证据，不能再次查询 Linkly 金融状态。
        var replay = await service.RecoverAttemptAsync(attempt.AttemptGuid, cart, Session);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, replay.Outcome);
        Assert.Equal(1, backend.StatusCallCount);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task RecoverAttemptAsync_approved_partial_repairs_legacy_approved_target_without_terminalizing()
    {
        var draft = CreateDraft(cardAmount: 5m);
        var attempt = CreateAttempt(
            sessionId: "SESSION-PARTIAL-LEGACY",
            txnRef: "TXN-PARTIAL-LEGACY",
            status: LocalCardPaymentAttemptStatus.Approved,
            draft: draft,
            amount: 5m) with
        {
            ResponseCode = "00",
            ResponseText = "APPROVED",
            PaymentReference = "BANK-PARTIAL-LEGACY",
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalCardPaymentAttemptStatus.Approved.ToString()
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var cart = new PosCartService();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.StatusCallCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_invalid_draft_requires_review_without_leaving_restored_cart()
    {
        var draft = CreateDraft(cardAmount: 15m);
        var attempt = CreateAttempt(
            sessionId: "SESSION-INVALID",
            txnRef: "TXN-INVALID",
            draft: draft,
            amount: 15m);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-INVALID", txnRef: "TXN-INVALID", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var cart = new PosCartService();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Empty(cart.Lines);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_order_save_failure_does_not_overwrite_new_cart()
    {
        var attempt = CreateAttempt(sessionId: "SESSION-SAVE-FAIL", txnRef: "TXN-SAVE-FAIL");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var cart = new PosCartService();
        var orders = new FakeLocalOrderRepository
        {
            BeforeSave = () => AddCurrentCartItem(cart),
            SaveException = new IOException("disk full")
        };
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus(
                "Completed",
                sessionId: "SESSION-SAVE-FAIL",
                txnRef: "TXN-SAVE-FAIL",
                responseCode: "00",
                responseText: "APPROVED",
                transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_stale_approved_result_cannot_save_order_after_supervisor_wins()
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-SUPERVISOR-RACE",
            txnRef: "TXN-SUPERVISOR-RACE");
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            UpdateOutcomeException = new InvalidOperationException("supervisor resolution won")
        };
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus(
                "Completed",
                sessionId: "SESSION-SUPERVISOR-RACE",
                txnRef: "TXN-SUPERVISOR-RACE",
                responseCode: "00",
                responseText: "APPROVED",
                transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_attempt_finalize_failure_returns_post_commit_warning()
    {
        var attempt = CreateAttempt(sessionId: "SESSION-FINALIZE-FAIL", txnRef: "TXN-FINALIZE-FAIL");
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            MarkOrderCompletedException = new IOException("database busy")
        };
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus(
                "Completed",
                sessionId: "SESSION-FINALIZE-FAIL",
                txnRef: "TXN-FINALIZE-FAIL",
                responseCode: "00",
                responseText: "APPROVED",
                transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.True(result.HasPostCommitWarning);
        Assert.NotNull(result.Order);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
    }

    [Fact]
    public async Task RecoverLatestAsync_uses_terminal_scope_when_attempt_cashier_differs_from_current_session()
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-EMERGENCY",
            txnRef: "TXN-EMERGENCY",
            cashierId: "EMERGENCY");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-EMERGENCY", txnRef: "TXN-EMERGENCY", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Null(attempts.LastCashierId);
        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-EMERGENCY", backend.AcknowledgedSessionId);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_acknowledge_failure_still_returns_completed_order_without_retrying_save()
    {
        var attempt = CreateAttempt(sessionId: "SESSION-001", txnRef: "TXN-001");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-001", txnRef: "TXN-001", responseCode: "00", responseText: "APPROVED", transactionSuccess: true),
            AcknowledgeException = new InvalidOperationException("ack failed")
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.NotNull(result.Order);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Null(attempts.AcknowledgedAt);
    }

    [Fact]
    public async Task RecoverLatestAsync_order_completed_without_acknowledgement_retries_ack_only()
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-ACK",
            txnRef: "TXN-ACK",
            status: LocalCardPaymentAttemptStatus.OrderCompleted);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.None, result.Outcome);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-ACK", backend.AcknowledgedSessionId);
        Assert.NotNull(attempts.AcknowledgedAt);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
    }

    [Fact]
    public async Task RecoverLatestAsync_order_completed_ack_retry_failure_keeps_unacknowledged_attempt()
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-ACK",
            txnRef: "TXN-ACK",
            status: LocalCardPaymentAttemptStatus.OrderCompleted);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            AcknowledgeException = new InvalidOperationException("ack failed")
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.None, result.Outcome);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-ACK", backend.AcknowledgedSessionId);
        Assert.Null(attempts.AcknowledgedAt);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_get_last_approved_completes_order_without_backend_acknowledgement()
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "LOCAL-TXN-001",
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:LOCAL-TXN-001",
            "ANZ Linkly",
            10m,
            [CreateLocalCardTransaction("LOCAL-TXN-001", "00", "APPROVED")],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "LOCAL-TXN-001",
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.NotNull(result.Order);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal("ANZ:LOCAL-TXN-001", attempts.PaymentReference);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(1, localTerminal.RecoverCallCount);
        Assert.Equal("LOCAL-TXN-001", localTerminal.LastTxnRef);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal("ANZ:LOCAL-TXN-001", result.Order!.Payments.Single().Reference);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_approved_with_mismatched_existing_order_keeps_finalize_pending()
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "P000000000000002",
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository(CreateExistingOrder(
            "CARD_ATTEMPT:11111111222233334444555555555555"));
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:P000000000000002",
            "ANZ Linkly",
            10m,
            [CreateLocalCardTransaction("P000000000000002", "00", "APPROVED")],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "P000000000000002",
            "00",
            "APPROVED"));
        var cart = new PosCartService();
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(1, localTerminal.RecoverCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_approved_partial_amount_restores_tender_without_saving_order()
    {
        var draft = CreateDraft(cardAmount: 5m);
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "P000000000000003",
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft,
            amount: 5m);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:P000000000000003",
            "ANZ Linkly",
            5m,
            [CreateLocalCardTransaction("P000000000000003", "00", "APPROVED", 5m)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "P000000000000003",
            "00",
            "APPROVED"));
        var cart = new PosCartService();
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        var restoredTender = Assert.Single(result.RestoredTenders!);
        Assert.Equal(PaymentMethodKind.Card, restoredTender.Method);
        Assert.Equal(5m, restoredTender.Amount);
        Assert.Equal("CARD_ATTEMPT:aaaaaaaabbbbccccddddeeeeeeeeeeee", restoredTender.IdempotencyKey);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal("ANZ:P000000000000003", attempts.PaymentReference);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(1, localTerminal.RecoverCallCount);
        Assert.Equal(0, orders.SaveCount);
        Assert.Single(cart.Lines);
    }

    [Theory]
    [InlineData("null-txn-type")]
    [InlineData("decimal-min-card-amount")]
    public async Task RecoverLatestAsync_local_ip_approved_persists_approved_before_invalid_draft_materialization(
        string invalidKind)
    {
        var draft = invalidKind switch
        {
            "null-txn-type" => CreateDraft(cardAmount: 5m) with { TxnType = null! },
            "decimal-min-card-amount" => CreateDraft(cardAmount: decimal.MinValue),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidKind), invalidKind, null)
        };
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "P000000000000004",
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft,
            amount: 5m);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:P000000000000004",
            "ANZ Linkly",
            5m,
            [CreateLocalCardTransaction("P000000000000004", "00", "APPROVED", 5m)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "P000000000000004",
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);
        var cart = new PosCartService();

        CardPaymentRecoveryResult? result = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            result = await service.RecoverLatestAsync(cart, Session);
        });

        Assert.Null(exception);
        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result!.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal("ANZ:P000000000000004", attempts.PaymentReference);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_approved_semantically_invalid_snapshot_returns_unknown_and_leaves_cart_unchanged()
    {
        var draft = CreateSemanticallyInvalidDraft() with { CardAmount = 5m };
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "P000000000000005",
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft,
            amount: 5m);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:P000000000000005",
            "ANZ Linkly",
            5m,
            [CreateLocalCardTransaction("P000000000000005", "00", "APPROVED", 5m)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "P000000000000005",
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);
        var cart = new PosCartService();

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_approved_cart_subscriber_failure_still_restores_tender()
    {
        var draft = CreateDraft(cardAmount: 5m);
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "P000000000000006",
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft,
            amount: 5m);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:P000000000000006",
            "ANZ Linkly",
            5m,
            [CreateLocalCardTransaction("P000000000000006", "00", "APPROVED", 5m)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "P000000000000006",
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);
        var cart = new PosCartService();
        cart.CartChanged += (_, _) => throw new InvalidOperationException("subscriber failed");

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        var restoredTender = Assert.Single(result.RestoredTenders!);
        Assert.Equal(PaymentMethodKind.Card, restoredTender.Method);
        Assert.Equal(5m, restoredTender.Amount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Single(cart.Lines);
        Assert.Equal(0, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_approved_rebuild_log_subscriber_failure_still_returns_unknown()
    {
        var draft = CreateDraft(cardAmount: 5m) with { TxnType = null! };
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "P000000000000007",
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft,
            amount: 5m);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:P000000000000007",
            "ANZ Linkly",
            5m,
            [CreateLocalCardTransaction("P000000000000007", "00", "APPROVED", 5m)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "P000000000000007",
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);
        var cart = new PosCartService();

        Action<string> throwOnLine = line =>
        {
            if (line.Contains("recover approved draft rebuild failed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("log subscriber failed");
            }
        };
        ConsoleLog.LineWritten += throwOnLine;
        CardPaymentRecoveryResult? result = null;
        Exception? exception;
        try
        {
            exception = await Record.ExceptionAsync(async () =>
            {
                result = await service.RecoverLatestAsync(cart, Session);
            });
        }
        finally
        {
            ConsoleLog.LineWritten -= throwOnLine;
        }

        Assert.Null(exception);
        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result!.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_approved_out_of_memory_propagates()
    {
        var draft = CreateDraft(cardAmount: 5m);
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "LOCAL-TXN-OOM",
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft,
            amount: 5m);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:LOCAL-TXN-OOM",
            "ANZ Linkly",
            5m,
            [CreateLocalCardTransaction("LOCAL-TXN-OOM", "00", "APPROVED", 5m)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "LOCAL-TXN-OOM",
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);
        var cart = new PosCartService();
        cart.CartChanged += (_, _) => throw new OutOfMemoryException();

        await Assert.ThrowsAsync<OutOfMemoryException>(
            async () => await service.RecoverLatestAsync(cart, Session));
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_get_last_declined_restores_draft_without_backend_acknowledgement()
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "P000000000000009",
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            false,
            "ANZ:P000000000000009",
            "DECLINED",
            10m,
            [CreateLocalCardTransaction("P000000000000009", "05", "DECLINED")],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "P000000000000009",
            "05",
            "DECLINED"));
        var cart = new PosCartService();
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            result.DraftHandoffKey);
        Assert.Equal("05", attempts.ResponseCode);
        Assert.Equal("DECLINED", attempts.ResponseText);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(1, localTerminal.RecoverCallCount);
        Assert.Equal("P000000000000009", localTerminal.LastTxnRef);
        Assert.Equal(0, orders.SaveCount);
        Assert.Single(cart.Lines);

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Null(attempts.RecoveryTargetStatus);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_get_last_code_only_decline_restores_draft_as_declined()
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "LOCAL-TXN-CODE",
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            false,
            "ANZ:LOCAL-TXN-CODE",
            null,
            10m,
            [CreateLocalCardTransaction("LOCAL-TXN-CODE", "05", "请联系发卡行")],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "LOCAL-TXN-CODE",
            "05",
            "请联系发卡行"));
        var cart = new PosCartService();
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            result.DraftHandoffKey);
        Assert.Equal("05", attempts.ResponseCode);
        Assert.Single(cart.Lines);

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Null(attempts.RecoveryTargetStatus);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_get_last_txn_ref_mismatch_returns_unknown_without_saving()
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "LOCAL-TXN-001",
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:OTHER-TXN",
            "ANZ Linkly",
            10m,
            [CreateLocalCardTransaction("OTHER-TXN", "00", "APPROVED")],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "OTHER-TXN",
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(1, localTerminal.RecoverCallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("P1234567890123456")]
    [InlineData("P1234\n5678")]
    [InlineData("P交易")]
    public async Task RecoverLatestAsync_local_ip_invalid_historical_txn_ref_never_calls_get_last(
        string txnRef)
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: txnRef,
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:P000000000000001",
            AuthorizedAmount: 10m,
            TxnType: "P",
            TxnRef: "P000000000000001"));
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.SessionStarted, attempts.Status);
        Assert.Equal(0, localTerminal.RecoverCallCount);
        Assert.NotNull(result.PaymentSupervisorDetails);
    }

    [Fact]
    public async Task RecoverAttemptAsync_local_ip_invalid_historical_txn_ref_never_calls_get_last()
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "P1234567890123456",
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:P000000000000001",
            AuthorizedAmount: 10m,
            TxnType: "P",
            TxnRef: "P000000000000001"));
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.SessionStarted, attempts.Status);
        Assert.Equal(0, localTerminal.RecoverCallCount);
        Assert.NotNull(result.PaymentSupervisorDetails);
    }

    [Theory]
    [InlineData("en-US", "The saved Linkly reference is an older value that does not meet the 16-character protocol.")]
    [InlineData("zh-CN", "已保存的 Linkly 引用是不符合 16 字符协议的旧值。")]
    public async Task RecoverLatestAsync_local_ip_invalid_historical_txn_ref_uses_bilingual_supervisor_warning(
        string cultureName,
        string expectedPrefix)
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "P1234567890123456",
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var localization = new LocalizationService();
        localization.SetCulture(cultureName);
        var service = new CardPaymentRecoveryService(
            attempts,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            new FakeLinklyBackendTerminalClient(),
            new CashCheckoutService(),
            new FakeLocalOrderRepository(),
            new FakeSyncQueueRepository(),
            localization,
            new FakeLinklyTerminalClient(new PaymentAuthorizationResult(false, ResultUnknown: true)));

        try
        {
            var result = await service.RecoverLatestAsync(new PosCartService(), Session);

            Assert.StartsWith(expectedPrefix, result.Message, StringComparison.Ordinal);
            Assert.Contains("RRN", result.Message, StringComparison.Ordinal);
            Assert.Contains("STAN", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            localization.SetCulture(LocalizationService.DefaultCultureName);
        }
    }

    [Fact]
    public async Task RecoverLatestAsync_invalid_historical_refund_txn_ref_stays_locked_for_supervisor_review()
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "R1234567890123456",
            status: LocalCardPaymentAttemptStatus.Pending,
            connectionMode: LinklyConnectionMode.LocalIp) with
        {
            OperationKind = "Refund",
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(true));
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.NotEqual(LocalCardPaymentAttemptStatus.Abandoned, attempts.Status);
        Assert.Equal(0, localTerminal.RecoverCallCount);
        Assert.Equal(0, attempts.ResolveRefundCount);
        Assert.NotNull(result.RefundDetails);
        Assert.StartsWith(
            "The saved Linkly reference is an older value that does not meet the 16-character protocol.",
            result.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoverAttemptAsync_invalid_historical_refund_txn_ref_stays_locked_for_supervisor_review()
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "R1234567890123456",
            status: LocalCardPaymentAttemptStatus.Pending,
            connectionMode: LinklyConnectionMode.LocalIp) with
        {
            OperationKind = "Refund",
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(true));
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.NotEqual(LocalCardPaymentAttemptStatus.Abandoned, attempts.Status);
        Assert.Equal(0, localTerminal.RecoverCallCount);
        Assert.Equal(0, attempts.ResolveRefundCount);
        Assert.NotNull(result.RefundDetails);
        Assert.StartsWith(
            "The saved Linkly reference is an older value that does not meet the 16-character protocol.",
            result.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recover_refund_with_legacy_txn_ref_honors_persisted_supervisor_confirmed_refunded(
        bool targetedRecovery)
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-001");
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "R1234567890123456",
            status: LocalCardPaymentAttemptStatus.Approved,
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R",
            ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedRefunded,
            ResponseText = "Matched terminal receipt",
            PaymentReference = "LINKLY-REFUND-OLD",
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalCardPaymentAttemptStatus.OrderCompleted.ToString()
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(true));
        var service = CreateService(
            attempts,
            orders,
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = targetedRecovery
            ? await service.RecoverAttemptAsync(attempt.AttemptGuid, new PosCartService(), Session)
            : await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(0, localTerminal.RecoverCallCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_normalizes_anz_wrapper_and_protocol_padding_before_exact_match()
    {
        const string txnRef = "P000000000000101";
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: $" ANZ:{txnRef}   ",
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            $"ANZ:{txnRef}   ",
            "ANZ Linkly",
            10m,
            [CreateLocalCardTransaction($"{txnRef}   ", "00", "APPROVED")],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            $"{txnRef}   ",
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(txnRef, localTerminal.LastTxnRef);
        Assert.Equal(1, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_approved_accepts_consistent_identity_across_all_returned_fields()
    {
        const string txnRef = "P000000000000106";
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: txnRef,
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            $" ANZ:{txnRef} ",
            "ANZ Linkly",
            10m,
            [
                CreateLocalCardTransaction($"ANZ:{txnRef}", "00", "APPROVED"),
                CreateLocalCardTransaction($" {txnRef} ", "00", "APPROVED")
            ],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            $" {txnRef} ",
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(1, orders.SaveCount);
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("first-card-transaction-txn-ref")]
    [InlineData("second-card-transaction-txn-ref")]
    [InlineData("authorized-amount")]
    [InlineData("second-card-transaction-amount")]
    public async Task RecoverLatestAsync_local_ip_approved_rejects_conflict_in_any_returned_identity_field(
        string conflictField)
    {
        const string txnRef = "P000000000000107";
        const string conflictingTxnRef = "P000000000000999";
        var reference = $"ANZ:{txnRef}";
        var authorizedAmount = 10m;
        var firstTransaction = CreateLocalCardTransaction(txnRef, "00", "APPROVED");
        var secondTransaction = CreateLocalCardTransaction(txnRef, "00", "APPROVED");
        switch (conflictField)
        {
            case "reference":
                reference = $"ANZ:{conflictingTxnRef}";
                break;
            case "first-card-transaction-txn-ref":
                firstTransaction = CreateLocalCardTransaction(conflictingTxnRef, "00", "APPROVED");
                break;
            case "second-card-transaction-txn-ref":
                secondTransaction = CreateLocalCardTransaction(conflictingTxnRef, "00", "APPROVED");
                break;
            case "authorized-amount":
                authorizedAmount = 9m;
                break;
            case "second-card-transaction-amount":
                secondTransaction = CreateLocalCardTransaction(txnRef, "00", "APPROVED", 9m);
                break;
        }

        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: txnRef,
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            reference,
            "ANZ Linkly",
            authorizedAmount,
            [firstTransaction, secondTransaction],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            txnRef,
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(0, orders.SaveCount);
    }

    [Theory]
    [InlineData(null, 0, 0, true)]
    [InlineData(0, 0, 0, true)]
    [InlineData(null, 10, 0, true)]
    [InlineData(10, 10, 10, true)]
    [InlineData(null, 9, 0, false)]
    [InlineData(0, 0, 9, false)]
    [InlineData(9, 0, 0, false)]
    public async Task RecoverLatestAsync_local_ip_final_decline_allows_missing_or_zero_amount_but_rejects_nonzero_conflict(
        int? authorizedAmount,
        int firstTransactionAmount,
        int secondTransactionAmount,
        bool expectedMatch)
    {
        const string txnRef = "P000000000000108";
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: txnRef,
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var cart = new PosCartService();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            false,
            $"ANZ:{txnRef}",
            "DECLINED",
            authorizedAmount,
            [
                CreateLocalCardTransaction(txnRef, "05", "DECLINED", firstTransactionAmount),
                CreateLocalCardTransaction(txnRef, "05", "DECLINED", secondTransactionAmount)
            ],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            txnRef,
            "05",
            "DECLINED"));
        var service = CreateService(
            attempts,
            orders,
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(
            expectedMatch ? CardPaymentRecoveryOutcome.DraftRestored : CardPaymentRecoveryOutcome.Unknown,
            result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(expectedMatch, !cart.IsEmpty);
        Assert.Equal(0, orders.SaveCount);

        if (expectedMatch)
        {
            Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
            Assert.Equal(LocalCardPaymentAttemptStatus.Declined.ToString(), attempts.RecoveryTargetStatus);
            Assert.Equal(
                new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
                result.DraftHandoffKey);
            Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));
            Assert.Equal(LocalCardPaymentAttemptStatus.Declined, attempts.Status);
            Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
            Assert.Null(attempts.RecoveryTargetStatus);
            Assert.Null(cart.RecoveryOwnerAttemptGuid);
        }
        else
        {
            Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
            Assert.Null(attempts.RecoveryTargetStatus);
            Assert.Null(result.DraftHandoffKey);
        }
    }

    [Theory]
    [InlineData("p000000000000102", "P", 10)]
    [InlineData("P000000000000102", "R", 10)]
    [InlineData("P000000000000102", "P", null)]
    [InlineData("P000000000000102", "P", 11)]
    [InlineData("P00000000000010", "P", 10)]
    public async Task RecoverLatestAsync_local_ip_approved_identity_conflict_stays_unknown(
        string returnedTxnRef,
        string returnedTxnType,
        int? returnedAmount)
    {
        const string persistedTxnRef = "P000000000000102";
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: persistedTxnRef,
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            $"ANZ:{returnedTxnRef}",
            "ANZ Linkly",
            returnedAmount,
            [CreateLocalCardTransaction(returnedTxnRef, "00", "APPROVED")],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            returnedTxnType,
            null,
            returnedTxnRef,
            "00",
            "APPROVED"));
        var service = CreateService(
            attempts,
            orders,
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(0, orders.SaveCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(10)]
    public async Task RecoverLatestAsync_local_ip_final_decline_accepts_missing_zero_or_exact_amount(
        int? returnedAmount)
    {
        const string txnRef = "P000000000000103";
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: txnRef,
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var cart = new PosCartService();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            false,
            $"ANZ:{txnRef}",
            "DECLINED",
            returnedAmount,
            [CreateLocalCardTransaction(txnRef, "05", "DECLINED")],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            txnRef,
            "05",
            "DECLINED"));
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            result.DraftHandoffKey);
        Assert.Single(cart.Lines);

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Null(attempts.RecoveryTargetStatus);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_final_decline_with_nonzero_amount_mismatch_stays_unknown()
    {
        const string txnRef = "P000000000000104";
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: txnRef,
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var cart = new PosCartService();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            false,
            $"ANZ:{txnRef}",
            "DECLINED",
            9m,
            [CreateLocalCardTransaction(txnRef, "05", "DECLINED", 9m)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            txnRef,
            "05",
            "DECLINED"));
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_provider_unknown_does_not_require_identity_fields()
    {
        const string txnRef = "P000000000000105";
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: txnRef,
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            false,
            Message: "Terminal outcome unavailable.",
            ResultUnknown: true));
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(1, localTerminal.RecoverCallCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_uses_attempt_connection_mode_instead_of_current_settings_mode()
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-BACKEND",
            txnRef: "TXN-BACKEND",
            connectionMode: LinklyConnectionMode.CloudBackendAsync);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-BACKEND", txnRef: "TXN-BACKEND", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(false, "ANZ:TXN-BACKEND"));
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal(0, localTerminal.RecoverCallCount);
    }

    [Fact]
    public async Task RecoverActiveSessionAsync_allows_backend_recovery_when_local_ip_priority_includes_cloud_backend()
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus("Pending", sessionId: "ACTIVE-BACKEND", txnRef: "TXN-ACTIVE", responseCode: null, responseText: null),
            Status = CreateStatus("Failed", sessionId: "ACTIVE-BACKEND", txnRef: "TXN-ACTIVE", responseCode: "05", responseText: "FAILED")
        };
        var service = CreateService(
            attempts,
            orders,
            backend,
            new FakeCardTerminalSettingsProvider(
                LinklyConnectionMode.LocalIp,
                [
                    LinklyConnectionMode.LocalIp,
                    LinklyConnectionMode.CloudBackendAsync
                ]));

        var result = await service.RecoverActiveSessionAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.ActiveSessionNotPaid, result.Outcome);
        Assert.Equal(LinklyBankReceiptKind.RecoveredFailed, result.BankReceipt?.Kind);
        Assert.Equal("RECEIPT", result.BankReceipt?.ReceiptText);
        Assert.Equal(1, backend.ResumeCallCount);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("ACTIVE-BACKEND", backend.AcknowledgedSessionId);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("NotSubmitted")]
    [InlineData("Cancelled")]
    [InlineData("Canceled")]
    public async Task RecoverActiveSessionAsync_without_local_attempt_recovers_and_acknowledges_failed_session(string finalStatus)
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus("Pending", sessionId: "ACTIVE-SESSION", txnRef: "TXN-ACTIVE", responseCode: null, responseText: null),
            Status = CreateStatus(finalStatus, sessionId: "ACTIVE-SESSION", txnRef: "TXN-ACTIVE", responseCode: "05", responseText: finalStatus)
        };
        var cart = CreateCurrentCart();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverActiveSessionAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.ActiveSessionNotPaid, result.Outcome);
        Assert.Equal(LinklyBankReceiptKind.RecoveredFailed, result.BankReceipt?.Kind);
        Assert.Equal("RECEIPT", result.BankReceipt?.ReceiptText);
        Assert.Equal(1, backend.ResumeCallCount);
        Assert.Equal("ACTIVE-SESSION", backend.ResumedSessionId);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("ACTIVE-SESSION", backend.AcknowledgedSessionId);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal(0, orders.SaveCount);
        Assert.Contains("not paid", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current order", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoverActiveSessionAsync_without_local_attempt_approved_session_acknowledges_and_returns_bank_receipt()
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus("Completed", sessionId: "ACTIVE-APPROVED", txnRef: "TXN-APPROVED", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var cart = CreateCurrentCart();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverActiveSessionAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.ActiveSessionApproved, result.Outcome);
        Assert.Equal(LinklyBankReceiptKind.RecoveredApproved, result.BankReceipt?.Kind);
        Assert.Equal("RECEIPT", result.BankReceipt?.ReceiptText);
        Assert.Equal("ACTIVE-APPROVED", result.BankReceipt?.SessionId);
        Assert.Equal("Sandbox", result.BankReceipt?.Environment);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("ACTIVE-APPROVED", backend.AcknowledgedSessionId);
        Assert.Equal(0, orders.SaveCount);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal("TXN-APPROVED", result.DialogDetails?.TxnRef);
    }

    [Fact]
    public async Task RecoverActiveSessionAsync_completed_summary_without_result_refreshes_status_by_session()
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus("Completed", sessionId: "ACTIVE-REFRESH", txnRef: "TXN-REFRESH", responseCode: null, responseText: null),
            Status = CreateStatus("Completed", sessionId: "ACTIVE-REFRESH", txnRef: "TXN-REFRESH", responseCode: "05", responseText: "DECLINED", transactionSuccess: false)
        };
        var cart = CreateCurrentCart();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverActiveSessionAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.ActiveSessionNotPaid, result.Outcome);
        Assert.Equal(1, backend.StatusCallCount);
        Assert.Equal("ACTIVE-REFRESH", backend.StatusSessionId);
        Assert.Equal(0, backend.ResumeCallCount);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal(LinklyBankReceiptKind.RecoveredFailed, result.BankReceipt?.Kind);
        Assert.Equal("05", result.BankReceipt?.ResponseCode);
        Assert.Single(cart.Lines);
        Assert.Equal(0, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverActiveSessionAsync_approved_session_uses_receipt_notification_when_receipt_text_is_missing()
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus(
                "Completed",
                sessionId: "ACTIVE-NOTIFICATION",
                txnRef: "TXN-NOTIFICATION",
                responseCode: "00",
                responseText: "APPROVED",
                transactionSuccess: true,
                receiptText: null,
                notifications:
                [
                    new LinklyCloudBackendNotificationDto(
                        "receipt",
                        """{ "Response": { "ReceiptText": "NOTIFICATION RECEIPT" } }""",
                        DateTimeOffset.Parse("2026-06-05T10:01:00+10:00"))
                ])
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverActiveSessionAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.ActiveSessionApproved, result.Outcome);
        Assert.Equal("NOTIFICATION RECEIPT", result.BankReceipt?.ReceiptText);
    }

    [Theory]
    [InlineData("Completed", true)]
    [InlineData("Failed", false)]
    public async Task RecoverActiveSessionAsync_final_session_acknowledge_failure_stays_unknown(
        string finalStatus,
        bool transactionSuccess)
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus(
                finalStatus,
                sessionId: "ACTIVE-ACK-FAIL",
                txnRef: "TXN-ACK-FAIL",
                responseCode: transactionSuccess ? "00" : "05",
                responseText: transactionSuccess ? "APPROVED" : "DECLINED",
                transactionSuccess: transactionSuccess),
            AcknowledgeException = new HttpRequestException("ack failed")
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverActiveSessionAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Null(result.BankReceipt);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Contains("could not clear", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManuallyClearActiveSessionAsync_acknowledges_session_without_changing_cart_or_order()
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var cart = CreateCurrentCart();
        var service = CreateService(attempts, orders, backend);

        var result = await service.ManuallyClearActiveSessionAsync("ACTIVE-MANUAL", Session);

        Assert.Equal(CardPaymentRecoveryOutcome.ActiveSessionManuallyCleared, result.Outcome);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("ACTIVE-MANUAL", backend.AcknowledgedSessionId);
        Assert.Equal(0, orders.SaveCount);
        Assert.Single(cart.Lines);
        Assert.Contains("manually checked", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManuallyClearActiveSessionAsync_acknowledge_failure_stays_unknown()
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            AcknowledgeException = new HttpRequestException("ack failed")
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.ManuallyClearActiveSessionAsync("ACTIVE-MANUAL", Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Contains("could not clear", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoverLatestAsync_requires_review_returns_unknown_without_status_query_or_acknowledge()
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-REVIEW",
            txnRef: "TXN-REVIEW",
            status: LocalCardPaymentAttemptStatus.RequiresReview);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var cart = new PosCartService();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Contains("does not match the order amount", result.Message);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.RequiresReview, attempts.Status);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_txn_ref_mismatch_returns_unknown_without_saving_or_acknowledging()
    {
        var attempt = CreateAttempt(sessionId: "SESSION-001", txnRef: "TXN-LOCAL");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-001", txnRef: "TXN-REMOTE", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Null(attempts.AcknowledgedAt);
    }

    [Theory]
    [InlineData("Failed", "OPERATOR TIMEOUT", LocalCardPaymentAttemptStatus.TimedOut)]
    [InlineData("NotSubmitted", "Linkly Cloud returned HTTP 400.", LocalCardPaymentAttemptStatus.Failed)]
    public async Task RecoverLatestAsync_final_resumable_failure_restores_draft_and_acknowledges(
        string status,
        string responseText,
        LocalCardPaymentAttemptStatus expectedStatus)
    {
        var draft = CreateDraft(
            cardAmount: 5m,
            currentTenders:
            [
                new PaymentTender(PaymentMethodKind.Cash, 3m, "CASH:RECOVERY"),
                new PaymentTender(PaymentMethodKind.Voucher, 2m, "VOUCHER:RECOVERY")
            ]);
        var attempt = CreateAttempt(sessionId: null, txnRef: "TXN-FAILED", draft: draft);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus(status, sessionId: "SESSION-FAILED", txnRef: "TXN-FAILED", responseCode: "05", responseText: responseText)
        };
        var cart = new PosCartService();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        Assert.Single(cart.Lines);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.SessionStarted, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(expectedStatus.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            result.DraftHandoffKey);
        Assert.Equal(2, result.RestoredTenders!.Count);
        Assert.Equal(PaymentMethodKind.Cash, result.RestoredTenders[0].Method);
        Assert.Equal(PaymentMethodKind.Voucher, result.RestoredTenders[1].Method);
        Assert.Equal(5m, result.TenderedAmount);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-FAILED", backend.AcknowledgedSessionId);
        Assert.NotNull(attempts.AcknowledgedAt);

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));
        Assert.Equal(expectedStatus, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Null(attempts.RecoveryTargetStatus);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task RecoverLatestAsync_pending_resumable_resumes_to_final_binds_session_and_completes_order()
    {
        var attempt = CreateAttempt(sessionId: null, txnRef: "TXN-PENDING");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus("Pending", sessionId: "SESSION-PENDING", txnRef: "TXN-PENDING", responseCode: null, responseText: "PRESENT CARD"),
            Status = CreateStatus("Completed", sessionId: "SESSION-PENDING", txnRef: "TXN-PENDING", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(1, backend.ResumeCallCount);
        Assert.Equal("SESSION-PENDING", backend.ResumedSessionId);
        Assert.Equal("SESSION-PENDING", attempts.SessionId);
        Assert.Equal("TXN-PENDING", attempts.TxnRef);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-PENDING", backend.AcknowledgedSessionId);
    }

    [Fact]
    public async Task RecoverLatestAsync_pending_resumable_resumes_to_final_and_restores_draft_after_decline()
    {
        var draft = CreateDraft(
            cardAmount: 5m,
            currentTenders:
            [
                new PaymentTender(PaymentMethodKind.Cash, 3m, "CASH:RECOVERY"),
                new PaymentTender(PaymentMethodKind.Voucher, 2m, "VOUCHER:RECOVERY")
            ]);
        var attempt = CreateAttempt(sessionId: null, txnRef: "TXN-PENDING", draft: draft);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus("Pending", sessionId: "SESSION-PENDING", txnRef: "TXN-PENDING", responseCode: null, responseText: "PRESENT CARD"),
            Status = CreateStatus("Failed", sessionId: "SESSION-PENDING", txnRef: "TXN-PENDING", responseCode: "05", responseText: "DECLINED")
        };
        var cart = new PosCartService();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        Assert.Equal(1, backend.ResumeCallCount);
        Assert.Equal("SESSION-PENDING", backend.ResumedSessionId);
        Assert.Equal("SESSION-PENDING", attempts.SessionId);
        Assert.Equal("TXN-PENDING", attempts.TxnRef);
        Assert.Single(cart.Lines);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.SessionStarted, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            result.DraftHandoffKey);
        Assert.Equal(2, result.RestoredTenders!.Count);
        Assert.Equal(PaymentMethodKind.Cash, result.RestoredTenders[0].Method);
        Assert.Equal(PaymentMethodKind.Voucher, result.RestoredTenders[1].Method);
        Assert.Equal(5m, result.TenderedAmount);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-PENDING", backend.AcknowledgedSessionId);

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Null(attempts.RecoveryTargetStatus);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task RecoverLatestAsync_resumable_without_local_session_or_txn_ref_fails_closed_without_saving()
    {
        var attempt = CreateAttempt(sessionId: null, txnRef: null);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus("Pending", sessionId: "SESSION-UNKNOWN", txnRef: "TXN-UNKNOWN", responseCode: null, responseText: "PRESENT CARD"),
            Status = CreateStatus("Completed", sessionId: "SESSION-UNKNOWN", txnRef: "TXN-UNKNOWN", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Contains("cannot be confirmed", result.Message);
        Assert.Equal(1, backend.ResumeCallCount);
        Assert.Null(attempts.SessionId);
        Assert.Null(attempts.TxnRef);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
    }

    [Fact]
    public async Task RecoverLatestAsync_status_query_failure_returns_unknown_without_saving_or_acknowledging()
    {
        var attempt = CreateAttempt(sessionId: "SESSION-001", txnRef: "TXN-001");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            StatusException = new InvalidOperationException("network down")
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Null(attempts.AcknowledgedAt);
    }

    [Fact]
    public async Task RecoverLatestAsync_completed_with_approved_code_but_missing_transaction_success_requires_supervisor_review()
    {
        var attempt = CreateAttempt(sessionId: "SESSION-MISSING-SUCCESS", txnRef: "TXN-MISSING-SUCCESS");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-MISSING-SUCCESS", txnRef: "TXN-MISSING-SUCCESS", responseCode: "00", responseText: "APPROVED")
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Contains("supervisor", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoverLatestAsync_completed_with_transaction_success_false_restores_draft()
    {
        var draft = CreateDraft(
            cardAmount: 5m,
            currentTenders:
            [
                new PaymentTender(PaymentMethodKind.Cash, 3m, "CASH:RECOVERY"),
                new PaymentTender(PaymentMethodKind.Voucher, 2m, "VOUCHER:RECOVERY")
            ]);
        var attempt = CreateAttempt(
            sessionId: "SESSION-DECLINED",
            txnRef: "TXN-DECLINED",
            draft: draft);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-DECLINED", txnRef: "TXN-DECLINED", responseCode: "50", responseText: "SYSTEM ERROR", transactionSuccess: false)
        };
        var cart = new PosCartService();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.Outcome);
        Assert.Single(cart.Lines);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Failed.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            result.DraftHandoffKey);
        Assert.Equal(2, result.RestoredTenders!.Count);
        Assert.Equal(PaymentMethodKind.Cash, result.RestoredTenders[0].Method);
        Assert.Equal(PaymentMethodKind.Voucher, result.RestoredTenders[1].Method);
        Assert.Equal(5m, result.TenderedAmount);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-DECLINED", backend.AcknowledgedSessionId);

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));
        Assert.Equal(LocalCardPaymentAttemptStatus.Failed, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Null(attempts.RecoveryTargetStatus);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task RecoverLatestAsync_declined_acknowledge_failure_rolls_back_cart_and_keeps_finalize_pending()
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-DECLINED-ACK-FAIL",
            txnRef: "TXN-DECLINED-ACK-FAIL");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus(
                "Completed",
                sessionId: "SESSION-DECLINED-ACK-FAIL",
                txnRef: "TXN-DECLINED-ACK-FAIL",
                responseCode: "50",
                responseText: "SYSTEM ERROR",
                transactionSuccess: false),
            AcknowledgeException = new HttpRequestException("ack failed")
        };
        var cart = new PosCartService();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.True(cart.IsEmpty);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        var saved = Assert.IsType<LocalCardPaymentAttempt>(
            await attempts.GetAttemptAsync(attempt.AttemptGuid));
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, saved.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, saved.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Failed.ToString(), saved.RecoveryTargetStatus);
    }

    [Fact]
    public async Task RecoverLatestAsync_completed_from_official_get_payload_keeps_refund_reference_in_payment_reference()
    {
        var attempt = CreateAttempt(sessionId: "SESSION-RFN", txnRef: "TXN-RFN");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus(
                "Completed",
                sessionId: "SESSION-RFN",
                txnRef: "TXN-RFN",
                responseCode: "08",
                responseText: "APPROVE WITH SIG",
                transactionSuccess: true,
                notifications:
                [
                    new LinklyCloudBackendNotificationDto(
                        "transaction",
                        """{ "Response": { "Success": true, "TxnRef": "TXN-RFN", "ResponseCode": "08", "ResponseText": "APPROVE WITH SIG", "AmtPurchase": 1008, "PurchaseAnalysisData": { "RFN": "RFN-OFFICIAL" } } }""",
                        DateTimeOffset.Parse("2026-06-05T10:01:00+10:00"))
                ])
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(
            "ANZBACKEND:TXN-RFN:RFN-OFFICIAL:session=SESSION-RFN:environment=Sandbox",
            attempts.PaymentReference);
    }

    [Fact]
    public async Task RecoverLatestAsync_pending_resumable_resume_result_unknown_preserves_detail_message()
    {
        var attempt = CreateAttempt(sessionId: null, txnRef: "TXN-PENDING");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus("Pending", sessionId: "SESSION-PENDING", txnRef: "TXN-PENDING", responseCode: null, responseText: "PRESENT CARD"),
            ResumeException = new LinklyBackendResultUnknownException("Resume timed out for session SESSION-PENDING / txn TXN-PENDING.")
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Contains("SESSION-PENDING", result.Message);
        Assert.Contains("TXN-PENDING", result.Message);
        Assert.Equal("SESSION-PENDING", result.DialogDetails?.SessionId);
        Assert.Equal("TXN-PENDING", result.DialogDetails?.TxnRef);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(1, backend.ResumeCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
    }

    [Fact]
    public async Task RecoverActiveSessionAsync_resume_result_unknown_preserves_detail_message()
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus("Pending", sessionId: "ACTIVE-SESSION", txnRef: "TXN-ACTIVE", responseCode: null, responseText: "PRESENT CARD"),
            ResumeException = new LinklyBackendResultUnknownException("Recovery timed out for session ACTIVE-SESSION / txn TXN-ACTIVE.")
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverActiveSessionAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Contains("ACTIVE-SESSION", result.Message);
        Assert.Contains("TXN-ACTIVE", result.Message);
        Assert.Equal("ACTIVE-SESSION", result.DialogDetails?.SessionId);
        Assert.Equal("TXN-ACTIVE", result.DialogDetails?.TxnRef);
        Assert.Equal(1, backend.ResumeCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
    }

    [Fact]
    public async Task RecoverActiveSessionAsync_resume_local_cancel_returns_unknown_with_local_stop_message()
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus("Pending", sessionId: "ACTIVE-SESSION", txnRef: "TXN-ACTIVE", responseCode: null, responseText: "PRESENT CARD"),
            ResumeException = new LinklyBackendLocalCancelException()
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverActiveSessionAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Contains("stopped waiting", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cannot be confirmed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ACTIVE-SESSION", result.DialogDetails?.SessionId);
        Assert.Equal("TXN-ACTIVE", result.DialogDetails?.TxnRef);
        Assert.Equal(1, backend.ResumeCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_approved_with_non_empty_current_cart_completes_old_order_without_overwriting_cart()
    {
        var attempt = CreateAttempt(sessionId: "SESSION-001", txnRef: "TXN-001");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-001", txnRef: "TXN-001", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var cart = CreateCurrentCart();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.NotNull(attempts.AcknowledgedAt);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("NotSubmitted")]
    public async Task RecoverLatestAsync_failure_with_non_empty_current_cart_defers_without_restoring_or_acknowledging(string status)
    {
        var attempt = CreateAttempt(sessionId: null, txnRef: "TXN-FAILED");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus(status, sessionId: "SESSION-FAILED", txnRef: "TXN-FAILED", responseCode: "05", responseText: "OPERATOR TIMEOUT")
        };
        var cart = CreateCurrentCart();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.SessionStarted, attempts.Status);
        Assert.Null(attempts.AcknowledgedAt);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_verified_with_non_empty_current_cart_completes_old_order_without_overwriting_cart()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.PaymentVerified,
            checkoutId: "CHECKOUT-001",
            paymentId: "PAYMENT-001",
            paymentStatus: "COMPLETED");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var service = CreateSquareService(attempts, orders, new FakeSquareTerminalPaymentClient());
        var cart = CreateCurrentCart();

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(1, attempts.MarkOrderCompletedCount);
        Assert.Equal(0, attempts.MarkFailedCount);
    }

    [Fact]
    public async Task RecoverAttemptAsync_square_verified_without_checkout_id_replays_finalize_pending_locally()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.PaymentVerified,
            checkoutId: null,
            paymentId: "PAYMENT-SAVE-FAIL",
            paymentStatus: "COMPLETED") with
        {
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.OrderCompleted
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var terminal = new FakeSquareTerminalPaymentClient();
        var service = CreateSquareService(attempts, orders, terminal);

        var result = await service.RecoverAttemptAsync(
            attempt.AttemptGuid,
            new PosCartService(),
            Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(0, terminal.GetCheckoutCallCount);
        Assert.Equal(0, terminal.GetPaymentCallCount);
    }

    [Fact]
    public async Task RecoverAttemptAsync_finalize_pending_square_existing_order_requires_exact_attempt_tender_key()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.PaymentVerified,
            checkoutId: "CHECKOUT-EXISTING-MISMATCH",
            paymentId: "PAYMENT-EXISTING-MISMATCH",
            paymentStatus: "COMPLETED") with
        {
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.OrderCompleted
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository(CreateExistingOrder(
            "SQUARE_ATTEMPT:11111111222233334444555555555555"));
        var terminal = new FakeSquareTerminalPaymentClient();
        var cart = new PosCartService();
        var service = CreateSquareService(attempts, orders, terminal);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, terminal.GetCheckoutCallCount);
        Assert.Equal(0, terminal.GetPaymentCallCount);
        Assert.True(cart.IsEmpty);
        Assert.Equal(LocalSquarePaymentAttemptStatus.PaymentVerified, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, attempts.RecoveryTargetStatus);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_missing_checkout_id_stays_locked_without_restoring_cart()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Pending,
            checkoutId: null) with
        {
            // legacy rows may not contain a usable draft; missing CheckoutId must still lock safely.
            OrderDraftJson = "{invalid"
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var terminal = new FakeSquareTerminalPaymentClient();
        var service = CreateSquareService(attempts, orders, terminal);
        var cart = CreateCurrentCart();

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Contains("Do not take payment again", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(0, attempts.MarkFailedCount);
        Assert.Equal(0, terminal.GetCheckoutCallCount);
        Assert.Equal(0, terminal.GetPaymentCallCount);
        Assert.Equal(0, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_order_save_failure_does_not_overwrite_new_cart()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.PaymentVerified,
            checkoutId: "CHECKOUT-SAVE-FAIL",
            paymentId: "PAYMENT-SAVE-FAIL",
            paymentStatus: "COMPLETED");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var cart = new PosCartService();
        var orders = new FakeLocalOrderRepository
        {
            BeforeSave = () => AddCurrentCartItem(cart),
            SaveException = new IOException("disk full")
        };
        var service = CreateSquareService(
            attempts,
            orders,
            new FakeSquareTerminalPaymentClient());

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(0, attempts.MarkOrderCompletedCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_attempt_finalize_failure_returns_post_commit_warning()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.PaymentVerified,
            checkoutId: "CHECKOUT-FINALIZE-FAIL",
            paymentId: "PAYMENT-FINALIZE-FAIL",
            paymentStatus: "COMPLETED");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt)
        {
            MarkOrderCompletedException = new IOException("database busy")
        };
        var orders = new FakeLocalOrderRepository();
        var service = CreateSquareService(
            attempts,
            orders,
            new FakeSquareTerminalPaymentClient());

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.True(result.HasPostCommitWarning);
        Assert.NotNull(result.Order);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(1, attempts.MarkOrderCompletedCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_payment_details_are_saved_to_completed_order()
    {
        var attempt = CreateSquareAttempt(LocalSquarePaymentAttemptStatus.CheckoutCreated, checkoutId: "CHECKOUT-001");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var terminal = new FakeSquareTerminalPaymentClient
        {
            Checkout = new SquareCheckoutStatusResult("CHECKOUT-001", "COMPLETED", 1000, "AUD", ["PAYMENT-001"], null),
            Payment = new SquarePaymentStatusResult(
                "PAYMENT-001",
                "COMPLETED",
                1000,
                "AUD",
                CardBrand: "VISA",
                MaskedCardNumber: "****1111",
                AuthCode: "68aLBM")
        };
        var service = CreateSquareService(attempts, orders, terminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(1, orders.SaveCount);
        var payment = Assert.Single(result.Order!.Payments);
        var transaction = Assert.Single(payment.CardTransactions!);
        Assert.Equal("VISA", transaction.CardType);
        Assert.Equal("****1111", transaction.MaskedCardNumber);
        Assert.Equal("68aLBM", transaction.AuthCode);
        Assert.Equal("COMPLETED", transaction.ResponseText);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_canceled_with_non_empty_current_cart_defers_without_restoring_or_marking_terminal()
    {
        var attempt = CreateSquareAttempt(LocalSquarePaymentAttemptStatus.CheckoutCreated, checkoutId: "CHECKOUT-001");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var terminal = new FakeSquareTerminalPaymentClient
        {
            Checkout = new SquareCheckoutStatusResult("CHECKOUT-001", "CANCELED", 1000, "AUD", [], "OPERATOR TIMEOUT")
        };
        var service = CreateSquareService(attempts, orders, terminal);
        var cart = CreateCurrentCart();

        var result = await service.RecoverLatestAsync(cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Contains("current cart already contains items", result.Message);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(0, attempts.UpdateCheckoutStatusCount);
        Assert.Equal(0, attempts.MarkFailedCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_payment_amount_mismatch_requires_supervisor_review()
    {
        var attempt = CreateSquareAttempt(LocalSquarePaymentAttemptStatus.CheckoutCreated, checkoutId: "CHECKOUT-001");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var terminal = new FakeSquareTerminalPaymentClient
        {
            Checkout = new SquareCheckoutStatusResult("CHECKOUT-001", "COMPLETED", 1000, "AUD", ["PAYMENT-001"], null),
            Payment = new SquarePaymentStatusResult("PAYMENT-001", "COMPLETED", 999, "AUD")
        };
        var service = CreateSquareService(attempts, orders, terminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Contains("order amount", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(1, attempts.MarkFailedCount);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, attempts.Status);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_payment_currency_mismatch_returns_unknown_without_saving()
    {
        var attempt = CreateSquareAttempt(LocalSquarePaymentAttemptStatus.CheckoutCreated, checkoutId: "CHECKOUT-001");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var terminal = new FakeSquareTerminalPaymentClient
        {
            Checkout = new SquareCheckoutStatusResult("CHECKOUT-001", "COMPLETED", 1000, "AUD", ["PAYMENT-001"], null),
            Payment = new SquarePaymentStatusResult("PAYMENT-001", "COMPLETED", 1000, "USD")
        };
        var service = CreateSquareService(attempts, orders, terminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Contains("cannot be confirmed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(1, attempts.MarkFailedCount);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Unknown, attempts.Status);
    }

    [Theory]
    [InlineData(CardRefundSupervisorDecision.ConfirmRefunded)]
    [InlineData(CardRefundSupervisorDecision.ConfirmNotRefunded)]
    [InlineData(CardRefundSupervisorDecision.ContinueWaiting)]
    public async Task ResolveRefundAsync_rejects_missing_required_supervisor_evidence(
        CardRefundSupervisorDecision decision)
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-REFUND",
            txnRef: "TXN-REFUND",
            status: LocalCardPaymentAttemptStatus.Recovering) with
        {
            OperationKind = "Refund",
            OperationGuid = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                decision,
                Reason: string.Empty,
                Evidence: null,
                RefundReference: null),
            new PosCartService(),
            Session);

        Assert.False(result.Succeeded);
        Assert.Equal(0, attempts.ResolveRefundCount);
    }

    [Fact]
    public async Task RecoverActiveSessionAsync_unknown_creates_stable_supervisor_target()
    {
        var attempts = new FakeCardPaymentAttemptRepository(null);
        var backend = new FakeLinklyBackendTerminalClient
        {
            ResumableStatus = CreateStatus(
                "Completed",
                "SESSION-ORPHAN-001",
                "TXN-ORPHAN-001",
                responseCode: null,
                responseText: null,
                transactionSuccess: null)
        };
        var service = CreateService(attempts, new FakeLocalOrderRepository(), backend);

        var result = await service.RecoverActiveSessionAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(1, attempts.CreateActiveSessionCount);
        Assert.Equal("ActiveSession", attempts.OperationKind);
        var details = Assert.IsType<CardPaymentSupervisorDetails>(result.PaymentSupervisorDetails);
        Assert.Equal("SESSION-ORPHAN-001", details.SessionId);
        Assert.NotEqual(Guid.Empty, details.AttemptGuid);
    }

    [Fact]
    public async Task ResolvePaymentAsync_confirm_not_paid_restores_draft_and_keeps_owner_until_handoff()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-001",
            "TXN-SUPERVISOR-001",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateService(attempts, new FakeLocalOrderRepository(), backend);
        var cart = new PosCartService();

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                string.Empty,
                "MANAGER-01",
                "USER-MANAGER-01",
                "Manager One",
                Evidence: "Bank case confirms no charge"),
            cart,
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult?.Outcome);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            result.RecoveryResult?.DraftHandoffKey);
        Assert.Single(cart.Lines);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.NotNull(attempts.AcknowledgedAt);
        var journal = Assert.IsType<LocalFinancialSupervisorResolution>(attempts.LastPaymentJournal);
        Assert.Equal(string.Empty, journal.Reason);
        Assert.Equal("Bank case confirms no charge", journal.Evidence);
        Assert.Equal("MANAGER-01", journal.OperatorCashierId);
        Assert.Equal("USER-MANAGER-01", journal.OperatorUserGuid);
        using var audit = JsonDocument.Parse(journal.AuditPayloadJson);
        Assert.Equal(
            "CARD_PAYMENT_SUPERVISOR_RESOLUTION",
            audit.RootElement.GetProperty("operationType").GetString());
        Assert.Equal("Succeeded", audit.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(
            CardPaymentSupervisorDecision.ConfirmNotPaid.ToString(),
            audit.RootElement.GetProperty("reasonCode").GetString());
        Assert.Equal("MANAGER-01", audit.RootElement.GetProperty("cashierId").GetString());
        var properties = audit.RootElement.GetProperty("properties");
        Assert.Equal(attempt.AttemptGuid.ToString("D"), properties.GetProperty("attemptGuid").GetString());
        Assert.Equal(JsonValueKind.Null, properties.GetProperty("operationGuid").ValueKind);
        Assert.Equal("SESSION-SUPERVISOR-001", properties.GetProperty("sessionId").GetString());
        Assert.Equal("Bank case confirms no charge", properties.GetProperty("evidence").GetString());
        Assert.Equal(JsonValueKind.Null, properties.GetProperty("financialReference").ValueKind);

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));
        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Null(attempts.RecoveryTargetStatus);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task ResolvePaymentAsync_confirm_not_paid_without_draft_releases_terminal_lock()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-NO-DRAFT",
            "TXN-SUPERVISOR-NO-DRAFT",
            LocalCardPaymentAttemptStatus.Recovering) with
        {
            OrderDraftJson = string.Empty
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateService(attempts, new FakeLocalOrderRepository(), backend);
        var cart = new PosCartService();

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                string.Empty,
                "MANAGER-01",
                Evidence: "Bank confirms no charge"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.False(result.LockRetained);
        Assert.Equal(CardPaymentRecoveryOutcome.ActiveSessionNotPaid, result.RecoveryResult?.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned, attempts.Status);
        Assert.True(cart.IsEmpty);
        Assert.Equal(1, backend.AcknowledgeCallCount);
    }

    [Fact]
    public async Task RecoverAttemptAsync_finalize_pending_linkly_existing_order_requires_exact_attempt_tender_key()
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-EXISTING-MISMATCH",
            txnRef: "TXN-EXISTING-MISMATCH",
            status: LocalCardPaymentAttemptStatus.Approved) with
        {
            ResponseCode = "00",
            ResponseText = "APPROVED",
            PaymentReference = "ANZ:TXN-EXISTING-MISMATCH",
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalCardPaymentAttemptStatus.OrderCompleted.ToString()
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository(CreateExistingOrder(
            "CARD_ATTEMPT:11111111222233334444555555555555"));
        var backend = new FakeLinklyBackendTerminalClient();
        var cart = new PosCartService();
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.StatusCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.True(cart.IsEmpty);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted.ToString(), attempts.RecoveryTargetStatus);
    }

    [Fact]
    public async Task ResolvePaymentAsync_terminal_attempt_is_rejected_without_reopening_or_journal()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-TERMINAL",
            "TXN-SUPERVISOR-TERMINAL",
            LocalCardPaymentAttemptStatus.Declined);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                string.Empty,
                "MANAGER-01",
                Evidence: "Bank confirms no charge"),
            new PosCartService(),
            Session);

        Assert.False(result.Succeeded);
        Assert.False(result.ResolutionPersisted);
        Assert.False(result.LockRetained);
        Assert.Null(attempts.LastPaymentJournal);
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined, attempts.Status);
    }

    [Fact]
    public async Task ResolvePaymentAsync_terminal_decline_with_reference_reports_released_lock()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-TERMINAL-REFERENCE",
            "TXN-SUPERVISOR-TERMINAL-REFERENCE",
            LocalCardPaymentAttemptStatus.Declined) with
        {
            ResponseCode = "05",
            ResponseText = "DECLINED",
            PaymentReference = "BANK-DECLINED-REFERENCE"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                string.Empty,
                "MANAGER-01",
                Evidence: "Bank confirms no charge"),
            new PosCartService(),
            Session);

        Assert.False(result.Succeeded);
        Assert.False(result.LockRetained);
        Assert.False(result.ResolutionPersisted);
        Assert.Null(attempts.LastPaymentJournal);
        Assert.Equal("BANK-DECLINED-REFERENCE", attempts.PaymentReference);
    }

    [Fact]
    public async Task ResolvePaymentAsync_completed_supervisor_paid_attempt_reports_persisted_released_resolution()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-PAID-COMPLETED",
            "TXN-SUPERVISOR-PAID-COMPLETED",
            LocalCardPaymentAttemptStatus.OrderCompleted,
            acknowledgedAt: DateTimeOffset.Parse("2026-06-05T10:02:00+10:00")) with
        {
            ResponseCode = ActiveSessionSupervisorResolutionCodes.ConfirmedPaid,
            ResponseText = "Supervisor confirmed paid.",
            PaymentReference = "BANK-PAID-COMPLETED",
            RecoveryPhase = CardRecoveryPhases.None,
            RecoveryTargetStatus = null
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                string.Empty,
                "MANAGER-01",
                Evidence: "Stale dialog submission"),
            new PosCartService(),
            Session);

        Assert.False(result.Succeeded);
        Assert.False(result.LockRetained);
        Assert.True(result.ResolutionPersisted);
        Assert.Null(attempts.LastPaymentJournal);
        Assert.Equal("BANK-PAID-COMPLETED", attempts.PaymentReference);
    }

    [Fact]
    public async Task ResolvePaymentAsync_existing_payment_reference_rejects_supervisor_closure_without_side_effects()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-EVIDENCE-REFERENCE",
            "TXN-SUPERVISOR-EVIDENCE-REFERENCE",
            LocalCardPaymentAttemptStatus.Recovering) with
        {
            ResponseCode = "05",
            PaymentReference = "BANK-EXISTING-001"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var backend = new FakeLinklyBackendTerminalClient();
        var orders = new FakeLocalOrderRepository();
        var service = CreateService(attempts, orders, backend);

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                "Supervisor review",
                "MANAGER-01",
                Evidence: "Bank evidence says to recover the existing payment"),
            new PosCartService(),
            Session);

        Assert.False(result.Succeeded);
        Assert.Equal("Bank payment evidence already exists. Run recovery instead.", result.Message);
        Assert.False(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.Null(attempts.LastPaymentJournal);
        Assert.Equal("05", attempts.ResponseCode);
        Assert.Equal("BANK-EXISTING-001", attempts.PaymentReference);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(0, orders.SaveCount);
    }

    [Theory]
    [InlineData("00")]
    [InlineData("08")]
    [InlineData("11")]
    public async Task ResolvePaymentAsync_existing_approved_response_code_rejects_supervisor_closure_without_side_effects(
        string responseCode)
    {
        var attempt = CreateAttempt(
            $"SESSION-SUPERVISOR-EVIDENCE-{responseCode}",
            $"TXN-SUPERVISOR-EVIDENCE-{responseCode}",
            LocalCardPaymentAttemptStatus.Recovering) with
        {
            ResponseCode = responseCode
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var backend = new FakeLinklyBackendTerminalClient();
        var orders = new FakeLocalOrderRepository();
        var service = CreateService(attempts, orders, backend);

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                "Supervisor review",
                "MANAGER-01",
                Evidence: "Bank evidence says to recover the approved payment"),
            new PosCartService(),
            Session);

        Assert.False(result.Succeeded);
        Assert.Equal("Bank payment evidence already exists. Run recovery instead.", result.Message);
        Assert.False(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.Null(attempts.LastPaymentJournal);
        Assert.Equal(responseCode, attempts.ResponseCode);
        Assert.Null(attempts.PaymentReference);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(0, orders.SaveCount);
    }

    [Theory]
    [InlineData("en-US", "Do not enter a payment reference when confirming that no payment was processed.")]
    [InlineData("zh-CN", "确认未付款时，请勿填写付款参考号。")]
    public async Task ResolvePaymentAsync_confirm_not_paid_rejects_payment_reference_with_localized_message(
        string cultureName,
        string expectedMessage)
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-NOT-PAID-REFERENCE",
            "TXN-SUPERVISOR-NOT-PAID-REFERENCE",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var localization = new LocalizationService();
        localization.SetCulture(cultureName);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient(),
            localization: localization);

        try
        {
            var result = await service.ResolvePaymentAsync(
                new CardPaymentSupervisorResolution(
                    attempt.AttemptGuid,
                    CardProcessorKind.Linkly,
                    CardPaymentSupervisorDecision.ConfirmNotPaid,
                    "Supervisor review",
                    "MANAGER-01",
                    Evidence: "Bank evidence confirms no charge",
                    PaymentReference: "SUPERVISOR-PAYMENT-REFERENCE"),
                new PosCartService(),
                Session);

            Assert.False(result.Succeeded);
            Assert.Equal(expectedMessage, result.Message);
            Assert.True(result.LockRetained);
            Assert.False(result.ResolutionPersisted);
            Assert.Null(attempts.LastPaymentJournal);
            Assert.Null(attempts.PaymentReference);
            Assert.Null(attempts.ResponseCode);
        }
        finally
        {
            localization.SetCulture(LocalizationService.DefaultCultureName);
        }
    }

    [Fact]
    public async Task RecoverAttemptAsync_linkly_finalize_pending_refund_zero_row_draft_stays_unknown_and_pending()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-ZERO-ROW") with
        {
            CartSnapshot = new PosCartSnapshot([])
        };
        var attempt = CreateAttempt(
            "SESSION-REFUND-ZERO-ROW",
            "TXN-REFUND-ZERO-ROW",
            LocalCardPaymentAttemptStatus.Pending,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R",
            ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalCardPaymentAttemptStatus.Abandoned.ToString()
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var cart = CreateCurrentCart();
        var originalRevision = cart.Revision;
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Equal(originalRevision, cart.Revision);
        Assert.Equal("CURRENT-SKU", Assert.Single(cart.Lines).ProductCode);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned.ToString(), attempts.RecoveryTargetStatus);
    }

    [Fact]
    public async Task ResolveRefundAsync_linkly_confirm_not_refunded_keeps_owner_until_ui_projection()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-NOT-REFUNDED");
        var attempt = CreateAttempt(
            sessionId: "SESSION-REFUND-NOT-REFUNDED",
            txnRef: "TXN-REFUND-NOT-REFUNDED",
            status: LocalCardPaymentAttemptStatus.Recovering,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var backend = new FakeLinklyBackendTerminalClient();
        var cart = new PosCartService();
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            backend);

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                Reason: "Checked settlement",
                Evidence: "No matching refund"),
            cart,
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.RetryAllowed);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult?.Outcome);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            result.RecoveryResult?.DraftHandoffKey);
        Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);
        Assert.Equal(0, backend.StatusCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);

        // 关键投影失败时，精确 owner 回滚不能把退款决议推进成终态。
        Assert.True(cart.RollbackRecoveryPublication(attempt.AttemptGuid).Succeeded);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        Assert.True(cart.IsEmpty);
        Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned.ToString(), attempts.RecoveryTargetStatus);
    }

    [Fact]
    public async Task ResolveRefundAsync_linkly_confirm_not_refunded_finalizes_only_after_draft_handoff()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-HANDOFF");
        var attempt = CreateAttempt(
            sessionId: "SESSION-REFUND-HANDOFF",
            txnRef: "TXN-REFUND-HANDOFF",
            status: LocalCardPaymentAttemptStatus.Recovering,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var cart = new PosCartService();
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                Reason: "Checked settlement",
                Evidence: "No matching refund"),
            cart,
            Session);

        Assert.True(result.Succeeded);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult?.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempts.Status);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));

        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Null(attempts.RecoveryTargetStatus);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        Assert.False(cart.IsEmpty);
    }

    [Fact]
    public async Task CompleteDraftHandoffAsync_linkly_finalize_failure_keeps_exact_owner_and_attempt_open()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-HANDOFF-FAIL");
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "TXN-REFUND-HANDOFF-FAIL",
            status: LocalCardPaymentAttemptStatus.Pending,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R",
            ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalCardPaymentAttemptStatus.Abandoned.ToString()
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            FailRecoveryFinalization = true
        };
        var cart = new PosCartService();
        Assert.True(cart.TryPublishRecoverySnapshot(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            cart.Revision,
            draft.CartSnapshot).Succeeded);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        Assert.False(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));

        Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);
        Assert.False(cart.IsEmpty);
    }

    [Fact]
    public async Task CompleteDraftHandoffAsync_linkly_missing_attempt_keeps_exact_owner_locked()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-HANDOFF-MISSING");
        var attemptGuid = Guid.NewGuid();
        var attemptKey = new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attemptGuid);
        var cart = new PosCartService();
        Assert.True(cart.TryPublishRecoverySnapshot(
            attemptKey,
            cart.Revision,
            draft.CartSnapshot).Succeeded);
        var service = CreateService(
            new FakeCardPaymentAttemptRepository(null),
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        Assert.False(await service.CompleteDraftHandoffAsync(attemptGuid, cart));

        Assert.Equal(attemptKey, cart.RecoveryOwnerAttemptKey);
        Assert.False(cart.IsEmpty);
    }

    [Fact]
    public async Task CompleteDraftHandoffAsync_linkly_changed_decision_keeps_exact_owner_locked()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-HANDOFF-CHANGED");
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "TXN-REFUND-HANDOFF-CHANGED",
            status: LocalCardPaymentAttemptStatus.Pending,
            draft: draft) with
        {
            OperationKind = "Refund",
            ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalCardPaymentAttemptStatus.Abandoned.ToString()
        };
        var changedWinner = attempt with
        {
            ResponseCode = CardRefundSupervisorResolutionCodes.ContinueWaiting,
            UpdatedAt = attempt.UpdatedAt.AddTicks(1)
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            RecoveryFinalizationWinner = changedWinner
        };
        var attemptKey = new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid);
        var cart = new PosCartService();
        Assert.True(cart.TryPublishRecoverySnapshot(
            attemptKey,
            cart.Revision,
            draft.CartSnapshot).Succeeded);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        Assert.False(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));

        Assert.Equal(CardRefundSupervisorResolutionCodes.ContinueWaiting, attempts.ResponseCode);
        Assert.Equal(attemptKey, cart.RecoveryOwnerAttemptKey);
    }

    [Fact]
    public async Task CompleteDraftHandoffAsync_linkly_exact_cas_winner_releases_exact_owner()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-HANDOFF-WINNER");
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "TXN-REFUND-HANDOFF-WINNER",
            status: LocalCardPaymentAttemptStatus.Pending,
            draft: draft) with
        {
            OperationKind = "Refund",
            ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalCardPaymentAttemptStatus.Abandoned.ToString()
        };
        var terminalWinner = attempt with
        {
            Status = LocalCardPaymentAttemptStatus.Abandoned,
            RecoveryPhase = CardRecoveryPhases.None,
            RecoveryTargetStatus = null,
            UpdatedAt = attempt.UpdatedAt.AddTicks(1)
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            RecoveryFinalizationWinner = terminalWinner
        };
        var attemptKey = new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid);
        var cart = new PosCartService();
        Assert.True(cart.TryPublishRecoverySnapshot(
            attemptKey,
            cart.Revision,
            draft.CartSnapshot).Succeeded);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));

        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned, attempts.Status);
        Assert.Null(cart.RecoveryOwnerAttemptKey);
        Assert.False(cart.IsEmpty);
    }

    [Fact]
    public async Task ResolvePaymentAsync_linkly_confirm_not_paid_sale_draft_handoff_keeps_pending_until_cas_and_restart_skips_ack()
    {
        var draft = CreateDraft(
            cardAmount: 5m,
            currentTenders:
            [
                new PaymentTender(PaymentMethodKind.Cash, 3m, "CASH:SUPERVISOR"),
                new PaymentTender(PaymentMethodKind.Voucher, 2m, "VOUCHER:SUPERVISOR")
            ]);
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-NOT-PAID",
            "TXN-SUPERVISOR-NOT-PAID",
            LocalCardPaymentAttemptStatus.Recovering,
            draft: draft);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var backend = new FakeLinklyBackendTerminalClient();
        var cart = new PosCartService();
        var service = CreateService(attempts, new FakeLocalOrderRepository(), backend);

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                "Supervisor checked the bank",
                "MANAGER-01",
                Evidence: "Bank evidence confirms no charge"),
            cart,
            Session);

        Assert.True(result.Succeeded);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult?.Outcome);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            result.RecoveryResult?.DraftHandoffKey);
        Assert.Equal(2, result.RecoveryResult?.RestoredTenders?.Count);
        Assert.Equal(5m, result.RecoveryResult?.TenderedAmount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned.ToString(), attempts.RecoveryTargetStatus);
        Assert.NotNull(attempts.AcknowledgedAt);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);
        Assert.Equal(1, backend.AcknowledgeCallCount);

        // 中文注释：进程退出后由新的 service/cart 继续接收同一草稿，已确认的 acknowledge 不得重放。
        var restartedCart = new PosCartService();
        var restartedService = CreateService(attempts, new FakeLocalOrderRepository(), backend);
        var restarted = await restartedService.RecoverAttemptAsync(
            attempt.AttemptGuid,
            restartedCart,
            Session);

        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, restarted.Outcome);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid),
            restarted.DraftHandoffKey);
        Assert.Equal(5m, restarted.TenderedAmount);
        Assert.Equal(2, restarted.RestoredTenders?.Count);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(attempt.AttemptGuid, restartedCart.RecoveryOwnerAttemptGuid);

        Assert.True(await restartedService.CompleteDraftHandoffAsync(attempt.AttemptGuid, restartedCart));
        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Null(attempts.RecoveryTargetStatus);
        Assert.Null(restartedCart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task CompleteDraftHandoffAsync_linkly_sale_cas_failure_keeps_failure_pending_and_owner()
    {
        var draft = CreateDraft(
            cardAmount: 5m,
            currentTenders:
            [
                new PaymentTender(PaymentMethodKind.Cash, 3m, "CASH:CAS"),
                new PaymentTender(PaymentMethodKind.Voucher, 2m, "VOUCHER:CAS")
            ]);
        var attempt = CreateAttempt(
            "SESSION-SALE-CAS-FAIL",
            "TXN-SALE-CAS-FAIL",
            LocalCardPaymentAttemptStatus.Recovering,
            draft: draft) with
        {
            ResponseCode = "05",
            ResponseText = "DECLINED",
            AcknowledgedAt = DateTimeOffset.UtcNow,
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalCardPaymentAttemptStatus.Failed.ToString()
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            FailRecoveryFinalization = true
        };
        var attemptKey = new CardRecoveryAttemptKey(CardProcessorKind.Linkly, attempt.AttemptGuid);
        var cart = new PosCartService();
        Assert.True(cart.TryPublishRecoverySnapshot(
            attemptKey,
            cart.Revision,
            draft.CartSnapshot).Succeeded);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        Assert.False(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));

        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Failed.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(attemptKey, cart.RecoveryOwnerAttemptKey);
        Assert.False(cart.IsEmpty);
    }

    [Fact]
    public async Task ResolvePaymentAsync_confirm_not_paid_ack_failure_rolls_back_only_recovery_cart()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-ACK-FAIL",
            "TXN-SUPERVISOR-ACK-FAIL",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var backend = new FakeLinklyBackendTerminalClient
        {
            AcknowledgeException = new InvalidOperationException("ack failed")
        };
        var service = CreateService(attempts, new FakeLocalOrderRepository(), backend);
        var cart = new PosCartService();

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                string.Empty,
                "MANAGER-01",
                Evidence: "Bank case confirms no charge"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.RecoveryResult?.Outcome);
        Assert.True(cart.IsEmpty);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        Assert.Null(attempts.AcknowledgedAt);
    }

    [Fact]
    public async Task ResolvePaymentAsync_non_empty_cart_persists_not_paid_decision_without_overwriting_cart()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-CART-BLOCKED",
            "TXN-SUPERVISOR-CART-BLOCKED",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());
        var cart = CreateCurrentCart();

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                string.Empty,
                "MANAGER-01",
                Evidence: "Bank confirms no charge"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.NotNull(attempts.LastPaymentJournal);
        Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, attempts.ResponseCode);
        Assert.Equal("CURRENT-SKU", Assert.Single(cart.Lines).ProductCode);
    }

    [Fact]
    public async Task ResolvePaymentAsync_confirm_paid_accepts_reference_with_empty_note()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-PAID",
            "TXN-SUPERVISOR-PAID",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var service = CreateService(attempts, orders, new FakeLinklyBackendTerminalClient());

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmPaid,
                string.Empty,
                "MANAGER-01",
                PaymentReference: "BANK-PAYMENT-001"),
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.False(result.LockRetained);
        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.RecoveryResult?.Outcome);
        Assert.Equal(1, orders.SaveCount);
        var journal = Assert.IsType<LocalFinancialSupervisorResolution>(attempts.LastPaymentJournal);
        Assert.Equal(string.Empty, journal.Reason);
        Assert.Equal("BANK-PAYMENT-001", journal.FinancialReference);
        using var audit = JsonDocument.Parse(journal.AuditPayloadJson);
        Assert.Equal("Succeeded", audit.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(
            CardPaymentSupervisorDecision.ConfirmPaid.ToString(),
            audit.RootElement.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task ResolvePaymentAsync_continue_waiting_keeps_payment_locked_without_acknowledge()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-002",
            "TXN-SUPERVISOR-002",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateService(attempts, new FakeLocalOrderRepository(), backend);

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ContinueWaiting,
                string.Empty,
                "MANAGER-01",
                PaymentReference: "STALE-BANK-REFERENCE"),
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.RecoveryResult?.Outcome);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Null(attempts.PaymentReference);
        Assert.Equal(string.Empty, attempts.LastPaymentJournal?.Reason);
        var journal = Assert.IsType<LocalFinancialSupervisorResolution>(attempts.LastPaymentJournal);
        using var audit = JsonDocument.Parse(journal.AuditPayloadJson);
        Assert.Equal("Succeeded", audit.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            audit.RootElement.GetProperty("properties").GetProperty("financialReference").ValueKind);
        Assert.Equal(
            CardPaymentSupervisorDecision.ContinueWaiting.ToString(),
            audit.RootElement.GetProperty("reasonCode").GetString());
    }

    [Theory]
    [InlineData(CardPaymentSupervisorDecision.ConfirmPaid)]
    [InlineData(CardPaymentSupervisorDecision.ConfirmNotPaid)]
    public async Task ResolvePaymentAsync_requires_financial_evidence_when_note_is_empty(
        CardPaymentSupervisorDecision decision)
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-EVIDENCE",
            "TXN-SUPERVISOR-EVIDENCE",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                decision,
                string.Empty,
                "MANAGER-01"),
            new PosCartService(),
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.Null(attempts.LastPaymentJournal);
    }

    [Fact]
    public async Task ResolvePaymentAsync_rejects_note_over_500_characters()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-LONG-NOTE",
            "TXN-SUPERVISOR-LONG-NOTE",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ContinueWaiting,
                new string('x', 501),
                "MANAGER-01"),
            new PosCartService(),
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.Null(attempts.LastPaymentJournal);
    }

    [Theory]
    [InlineData(CardPaymentSupervisorDecision.ConfirmPaid, ActiveSessionSupervisorDecision.ConfirmNotPaid)]
    [InlineData(CardPaymentSupervisorDecision.ConfirmNotPaid, ActiveSessionSupervisorDecision.ConfirmPaid)]
    [InlineData(CardPaymentSupervisorDecision.ConfirmPaid, ActiveSessionSupervisorDecision.ConfirmPaid)]
    [InlineData(CardPaymentSupervisorDecision.ConfirmNotPaid, ActiveSessionSupervisorDecision.ConfirmNotPaid)]
    public async Task ResolvePaymentAsync_cas_loser_returns_persisted_payment_winner_without_side_effects(
        CardPaymentSupervisorDecision loserDecision,
        ActiveSessionSupervisorDecision winnerDecision)
    {
        var attempt = CreateAttempt(
            "SESSION-PAYMENT-CAS-LOSER",
            "TXN-PAYMENT-CAS-LOSER",
            LocalCardPaymentAttemptStatus.Recovering);
        var winner = CreatePaymentCasWinner(attempt, winnerDecision);
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            PaymentCasWinner = winner
        };
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateService(attempts, orders, backend);
        var cart = winnerDecision == ActiveSessionSupervisorDecision.ConfirmPaid
            ? CreateCurrentCart()
            : new PosCartService();
        var originalRevision = cart.Revision;
        var cartChangedCount = 0;
        cart.CartChanged += (_, _) => cartChangedCount++;

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                loserDecision,
                "Loser supervisor decision",
                "LOSER-MANAGER",
                Evidence: loserDecision == CardPaymentSupervisorDecision.ConfirmNotPaid
                    ? "Loser bank evidence"
                    : null,
                PaymentReference: loserDecision == CardPaymentSupervisorDecision.ConfirmPaid
                    ? "LOSER-PAYMENT-REFERENCE"
                    : null),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.False(result.ResolutionApplied);
        Assert.True(result.LockRetained);
        Assert.Equal(
            "The supervisor decision was saved, but recovery is still pending. Run recovery again before taking another payment or refund.",
            result.Message);
        Assert.Null(result.RecoveryResult);
        Assert.Equal(winner.Journal.ResolutionGuid, attempts.LastPaymentJournal?.ResolutionGuid);
        Assert.Equal(winnerDecision.ToString(), attempts.LastPaymentJournal?.Decision);
        Assert.Equal(
            winnerDecision == ActiveSessionSupervisorDecision.ConfirmPaid
                ? ActiveSessionSupervisorResolutionCodes.ConfirmedPaid
                : ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
            attempts.ResponseCode);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Null(attempts.AcknowledgedAt);
        Assert.Equal(0, backend.StatusCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(originalRevision, cart.Revision);
        Assert.Equal(0, cartChangedCount);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        if (winnerDecision == ActiveSessionSupervisorDecision.ConfirmPaid)
        {
            Assert.Equal("CURRENT-SKU", Assert.Single(cart.Lines).ProductCode);
        }
        else
        {
            Assert.True(cart.IsEmpty);
        }
    }

    [Fact]
    public async Task ResolvePaymentAsync_continue_waiting_cas_loser_keeps_existing_waiting_semantics_without_side_effects()
    {
        var attempt = CreateAttempt(
            "SESSION-PAYMENT-CAS-WAITING",
            "TXN-PAYMENT-CAS-WAITING",
            LocalCardPaymentAttemptStatus.Recovering);
        var winner = CreatePaymentCasWinner(attempt, ActiveSessionSupervisorDecision.ContinueWaiting);
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            PaymentCasWinner = winner
        };
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateService(attempts, orders, backend);
        var cart = new PosCartService();
        var cartChangedCount = 0;
        cart.CartChanged += (_, _) => cartChangedCount++;

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmPaid,
                "Loser supervisor decision",
                "LOSER-MANAGER",
                PaymentReference: "LOSER-PAYMENT-REFERENCE"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.False(result.ResolutionApplied);
        Assert.True(result.LockRetained);
        Assert.Equal(ActiveSessionSupervisorResolutionCodes.ContinueWaiting, attempts.ResponseCode);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Equal(0, backend.StatusCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, cartChangedCount);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task ResolvePaymentAsync_unknown_cas_loser_keeps_lock_without_side_effects()
    {
        var attempt = CreateAttempt(
            "SESSION-PAYMENT-CAS-UNKNOWN",
            "TXN-PAYMENT-CAS-UNKNOWN",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            RejectPaymentResolution = true
        };
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateService(attempts, orders, backend);
        var cart = new PosCartService();
        var cartChangedCount = 0;
        cart.CartChanged += (_, _) => cartChangedCount++;

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmPaid,
                "Supervisor decision",
                "MANAGER-01",
                PaymentReference: "BANK-PAYMENT-UNKNOWN"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.False(result.ResolutionPersisted);
        Assert.False(result.ResolutionApplied);
        Assert.True(result.LockRetained);
        Assert.Null(result.RecoveryResult);
        Assert.Null(attempts.LastPaymentJournal);
        Assert.Equal(0, backend.StatusCallCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, cartChangedCount);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task RecoverAttemptAsync_after_payment_cas_loser_applies_persisted_winner_explicitly()
    {
        var attempt = CreateAttempt(
            "SESSION-PAYMENT-CAS-RECOVER",
            "TXN-PAYMENT-CAS-RECOVER",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            PaymentCasWinner = CreatePaymentCasWinner(attempt, ActiveSessionSupervisorDecision.ConfirmPaid)
        };
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateService(attempts, orders, backend);
        var cart = new PosCartService();

        var loserResult = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                "Loser supervisor decision",
                "LOSER-MANAGER",
                Evidence: "Loser bank evidence"),
            cart,
            Session);

        Assert.False(loserResult.Succeeded);
        Assert.True(loserResult.LockRetained);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);

        var recovered = await service.RecoverAttemptAsync(attempt.AttemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, recovered.Outcome);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
    }

    [Theory]
    [InlineData(LinklyConnectionMode.LocalIp)]
    [InlineData(LinklyConnectionMode.CloudDirectSync)]
    [InlineData(LinklyConnectionMode.CloudBackendAsync)]
    public async Task ResolveRefundAsync_confirm_not_refunded_persists_mode_compatible_new_txn_ref_and_allows_retry(
        LinklyConnectionMode connectionMode)
    {
        const string originalTxnRef = "R000000000000001";
        var draft = CreateRefundDraft($"ANZ:{originalTxnRef}");
        var attempt = CreateAttempt(
            sessionId: connectionMode == LinklyConnectionMode.CloudBackendAsync ? "SESSION-REFUND" : null,
            txnRef: originalTxnRef,
            status: LocalCardPaymentAttemptStatus.Recovering,
            connectionMode: connectionMode,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(connectionMode));
        var cart = new PosCartService();

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                Reason: "Checked settlement report",
                Evidence: "No refund entry for this reference"),
            cart,
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.RetryAllowed);
        Assert.True(result.ResolutionPersisted);
        Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.Abandoned.ToString(), attempts.RecoveryTargetStatus);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);
        Assert.Null(attempts.SessionId);
        Assert.NotNull(attempts.LastRefundResolution);
        var retryTxnRef = Assert.IsType<string>(attempts.LastRefundResolution.RetryTxnRef);
        if (connectionMode == LinklyConnectionMode.LocalIp)
        {
            Assert.Equal(16, retryTxnRef.Length);
            Assert.StartsWith("R", retryTxnRef, StringComparison.Ordinal);
            Assert.All(retryTxnRef, character => Assert.Contains(character, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
        }
        else
        {
            Assert.Equal(32, retryTxnRef.Length);
            Assert.True(Guid.TryParseExact(retryTxnRef, "N", out _));
        }

        Assert.NotEqual(originalTxnRef, retryTxnRef);
        Assert.Equal(retryTxnRef, attempts.TxnRef);

        Assert.True(cart.RollbackRecoveryPublication(attempt.AttemptGuid).Succeeded);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
        Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
    }

    [Fact]
    public async Task ResolveRefundAsync_confirm_refunded_loser_preserves_confirm_not_refunded_cas_winner_without_side_effects()
    {
        const string originalTxnRef = "R000000000000201";
        const string winnerRetryTxnRef = "R000000000000202";
        var draft = CreateRefundDraft("ANZ:ORIGINAL-CAS-NOT-REFUNDED");
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: originalTxnRef,
            status: LocalCardPaymentAttemptStatus.Recovering,
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            RefundCasWinner = new CardRefundAttemptResolution(
                attempt.AttemptGuid,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                "Winner checked settlement",
                "Winner found no refund",
                RefundReference: null,
                RetryTxnRef: winnerRetryTxnRef,
                ResolvedAt: attempt.UpdatedAt.AddSeconds(1))
        };
        var orders = new FakeLocalOrderRepository();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(true));
        var cart = new PosCartService();
        var service = CreateService(
            attempts,
            orders,
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmRefunded,
                Reason: "Loser believed refund completed",
                RefundReference: "LOSER-REFUND-REFERENCE"),
            cart,
            Session);

        Assert.True(result.ResolutionPersisted);
        Assert.Equal(CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded, attempts.ResponseCode);
        Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(winnerRetryTxnRef, attempts.TxnRef);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, localTerminal.RecoverCallCount);
        Assert.Equal(0, orders.SaveCount);
        Assert.Null(result.RecoveryResult?.Order);
    }

    [Fact]
    public async Task ResolveRefundAsync_confirm_not_refunded_loser_preserves_confirm_refunded_cas_winner_without_side_effects()
    {
        const string originalTxnRef = "R000000000000203";
        const string winnerRefundReference = "WINNER-REFUND-REFERENCE";
        var draft = CreateRefundDraft("ANZ:ORIGINAL-CAS-REFUNDED");
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: originalTxnRef,
            status: LocalCardPaymentAttemptStatus.Recovering,
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            RefundCasWinner = new CardRefundAttemptResolution(
                attempt.AttemptGuid,
                CardRefundSupervisorDecision.ConfirmRefunded,
                "Winner matched settlement",
                Evidence: null,
                RefundReference: winnerRefundReference,
                RetryTxnRef: null,
                ResolvedAt: attempt.UpdatedAt.AddSeconds(1))
        };
        var orders = new FakeLocalOrderRepository();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(true));
        var cart = new PosCartService();
        var service = CreateService(
            attempts,
            orders,
            new FakeLinklyBackendTerminalClient(),
            new FakeCardTerminalSettingsProvider(LinklyConnectionMode.LocalIp),
            localTerminal);

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                Reason: "Loser checked settlement",
                Evidence: "Loser found no refund"),
            cart,
            Session);

        Assert.True(result.ResolutionPersisted);
        Assert.Equal(CardRefundSupervisorResolutionCodes.ConfirmedRefunded, attempts.ResponseCode);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(originalTxnRef, attempts.TxnRef);
        Assert.Equal(winnerRefundReference, attempts.PaymentReference);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, localTerminal.RecoverCallCount);
        Assert.Equal(0, orders.SaveCount);
        Assert.Null(result.RecoveryResult?.Order);
    }

    [Fact]
    public async Task ResolveRefundAsync_confirm_refunded_completes_linkly_return_without_second_terminal_call()
    {
        const string originalReference = "ANZ:ORIGINAL-REFUND";
        const string refundReference = "LINKLY-REFUND-001";
        var draft = CreateRefundDraft(originalReference);
        var attempt = CreateAttempt(
            sessionId: "SESSION-REFUND",
            txnRef: "TXN-REFUND",
            status: LocalCardPaymentAttemptStatus.Recovering,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var service = CreateService(attempts, orders, backend);
        var cart = new PosCartService();

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmRefunded,
                Reason: "Matched bank settlement",
                RefundReference: refundReference),
            cart,
            Session);

        Assert.True(result.Succeeded);
        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.RecoveryResult?.Outcome);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(0, backend.StatusCallCount);
        var payment = Assert.Single(result.RecoveryResult!.Order!.Payments);
        Assert.Equal(PaymentMethodKind.Card, payment.Method);
        Assert.Equal(-10m, payment.Amount);
        Assert.True(CardRefundReference.TryGetRefundReference(payment.Reference, out var savedRefundReference));
        Assert.Equal(refundReference, savedRefundReference);
        Assert.True(CardRefundReference.TryGetOriginalReference(payment.Reference, out var savedOriginalReference));
        Assert.Equal(originalReference, savedOriginalReference);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task ResolveRefundAsync_linkly_order_save_failure_keeps_current_cart_and_persisted_resolution()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-SAVE-FAIL");
        var attempt = CreateAttempt(
            "SESSION-REFUND-SAVE-FAIL",
            "TXN-REFUND-SAVE-FAIL",
            LocalCardPaymentAttemptStatus.Recovering,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository
        {
            SaveException = new InvalidOperationException("database unavailable")
        };
        var service = CreateService(attempts, orders, new FakeLinklyBackendTerminalClient());
        var cart = CreateCurrentCart();

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmRefunded,
                Reason: "Matched settlement",
                RefundReference: "LINKLY-REFUND-SAVE-FAIL"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.RecoveryResult?.Outcome);
        Assert.Equal("CURRENT-SKU", Assert.Single(cart.Lines).ProductCode);
    }

    [Fact]
    public async Task ResolvePaymentAsync_linkly_saved_order_with_finalize_failure_succeeds_but_retains_lock()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-FINALIZE-FAIL",
            "TXN-SUPERVISOR-FINALIZE-FAIL",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            FailRecoveryFinalization = true
        };
        var orders = new FakeLocalOrderRepository();
        var service = CreateService(attempts, orders, new FakeLinklyBackendTerminalClient());

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmPaid,
                string.Empty,
                "MANAGER-01",
                PaymentReference: "BANK-LINKLY-FINALIZE-FAIL"),
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.True(result.RecoveryResult?.HasPostCommitWarning);
        Assert.Equal(1, orders.SaveCount);
    }

    [Fact]
    public async Task ResolveRefundAsync_confirm_refunded_partial_only_succeeds_after_return_draft_is_published()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-PARTIAL") with { CardAmount = 5m };
        var attempt = CreateAttempt(
            sessionId: "SESSION-REFUND-PARTIAL",
            txnRef: "TXN-REFUND-PARTIAL",
            status: LocalCardPaymentAttemptStatus.Recovering,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var service = CreateService(attempts, orders, new FakeLinklyBackendTerminalClient());
        var cart = new PosCartService();

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmRefunded,
                Reason: "Matched partial bank refund",
                RefundReference: "LINKLY-REFUND-PARTIAL"),
            cart,
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.False(result.RetryAllowed);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult?.Outcome);
        Assert.Single(cart.Lines);
        Assert.Equal(0, orders.SaveCount);
    }

    [Fact]
    public async Task ResolveRefundAsync_confirm_refunded_partial_keeps_linkly_finalization_pending_until_order_save()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-PARTIAL-PENDING") with { CardAmount = 5m };
        var attempt = CreateAttempt(
            sessionId: "SESSION-REFUND-PARTIAL-PENDING",
            txnRef: "TXN-REFUND-PARTIAL-PENDING",
            status: LocalCardPaymentAttemptStatus.Recovering,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmRefunded,
                Reason: "Matched partial bank refund",
                RefundReference: "LINKLY-REFUND-PARTIAL-PENDING"),
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult?.Outcome);
        Assert.Equal(LocalCardPaymentAttemptStatus.Approved, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalCardPaymentAttemptStatus.OrderCompleted.ToString(), attempts.RecoveryTargetStatus);
    }

    [Fact]
    public async Task ResolveRefundAsync_confirm_refunded_partial_with_non_empty_cart_persists_decision_without_fake_success()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-PARTIAL") with { CardAmount = 5m };
        var attempt = CreateAttempt(
            sessionId: "SESSION-REFUND-PARTIAL-BLOCKED",
            txnRef: "TXN-REFUND-PARTIAL-BLOCKED",
            status: LocalCardPaymentAttemptStatus.Recovering,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var service = CreateService(attempts, orders, new FakeLinklyBackendTerminalClient());
        var cart = CreateCurrentCart();

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmRefunded,
                Reason: "Matched partial bank refund",
                RefundReference: "LINKLY-REFUND-PARTIAL"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.False(result.RetryAllowed);
        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.RecoveryResult?.Outcome);
        Assert.Equal("CURRENT-SKU", Assert.Single(cart.Lines).ProductCode);
        Assert.Equal(0, orders.SaveCount);
    }

    [Fact]
    public async Task ResolveRefundAsync_continue_waiting_keeps_attempt_locked()
    {
        var attempt = CreateAttempt(
            sessionId: "SESSION-REFUND",
            txnRef: "TXN-REFUND",
            status: LocalCardPaymentAttemptStatus.Recovering) with
        {
            OperationKind = "Refund",
            OperationGuid = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ContinueWaiting,
                Reason: "Bank settlement is not final"),
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRefundSupervisorResolutionCodes.ContinueWaiting, attempts.ResponseCode);
    }

    [Fact]
    public async Task ResolvePaymentAsync_linkly_post_commit_read_failure_reports_persisted_pending_resolution()
    {
        var attempt = CreateAttempt(
            "SESSION-SUPERVISOR-POST-COMMIT",
            "TXN-SUPERVISOR-POST-COMMIT",
            LocalCardPaymentAttemptStatus.Recovering);
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            PostCommitGetAttemptException = new InvalidOperationException("post-commit read failed")
        };
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());
        var cart = new PosCartService();

        var result = await service.ResolvePaymentAsync(
            new CardPaymentSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardPaymentSupervisorDecision.ConfirmNotPaid,
                string.Empty,
                "MANAGER-01",
                Evidence: "Bank confirms no charge"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.ResolutionApplied);
        Assert.True(result.LockRetained);
        Assert.True(cart.IsEmpty);
        Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, attempts.ResponseCode);
    }

    [Fact]
    public async Task ResolveRefundAsync_linkly_post_commit_read_failure_reports_persisted_pending_resolution()
    {
        var draft = CreateRefundDraft("ANZ:ORIGINAL-POST-COMMIT");
        var attempt = CreateAttempt(
            sessionId: "SESSION-REFUND-POST-COMMIT",
            txnRef: "TXN-REFUND-POST-COMMIT",
            status: LocalCardPaymentAttemptStatus.Recovering,
            draft: draft) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            TxnType = "R"
        };
        var attempts = new FakeCardPaymentAttemptRepository(attempt)
        {
            PostCommitGetAttemptException = new InvalidOperationException("post-commit read failed")
        };
        var service = CreateService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeLinklyBackendTerminalClient());
        var cart = new PosCartService();

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Linkly,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                Reason: "Checked settlement",
                Evidence: "No matching refund"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.ResolutionApplied);
        Assert.True(result.LockRetained);
        Assert.False(result.RetryAllowed);
        Assert.True(cart.IsEmpty);
        Assert.Equal(CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded, attempts.ResponseCode);
    }

    [Fact]
    public async Task ResolveAttemptAsync_square_post_commit_read_failure_reports_persisted_pending_resolution()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Recovering,
            "CHECKOUT-SUPERVISOR-POST-COMMIT");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt)
        {
            PostCommitGetAttemptException = new InvalidOperationException("post-commit read failed")
        };
        var service = CreateSquareService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeSquareTerminalPaymentClient());
        var cart = new PosCartService();

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmNotProcessed,
            string.Empty,
            evidence: "Bank confirms no Square payment",
            reference: null,
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.ResolutionApplied);
        Assert.True(result.LockRetained);
        Assert.True(cart.IsEmpty);
        Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, attempts.ResponseCode);
    }

    [Fact]
    public async Task ResolveRefundAsync_square_post_commit_read_failure_reports_persisted_pending_resolution()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-POST-COMMIT");
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Recovering,
            checkoutId: "CHECKOUT-REFUND-POST-COMMIT") with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions)
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt)
        {
            PostCommitGetAttemptException = new InvalidOperationException("post-commit read failed")
        };
        var service = CreateSquareService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeSquareTerminalPaymentClient());
        var cart = new PosCartService();

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Square,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                Reason: "Checked Square settlement",
                Evidence: "No matching refund"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.ResolutionApplied);
        Assert.True(result.LockRetained);
        Assert.False(result.RetryAllowed);
        Assert.True(cart.IsEmpty);
        Assert.Equal(CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded, attempts.ResponseCode);
    }

    [Fact]
    public async Task ResolveRefundAsync_square_confirm_refunded_completes_return_without_second_terminal_call()
    {
        const string originalReference = "SQ:ORIGINAL-PAYMENT";
        const string refundReference = "SQ-REFUND-001";
        var draft = CreateRefundDraft(originalReference);
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Unknown,
            checkoutId: "CHECKOUT-REFUND",
            paymentId: null) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions)
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var terminal = new FakeSquareTerminalPaymentClient();
        var service = CreateSquareService(attempts, orders, terminal);
        var cart = new PosCartService();

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Square,
                CardRefundSupervisorDecision.ConfirmRefunded,
                Reason: "Matched Square settlement",
                RefundReference: refundReference),
            cart,
            Session);

        Assert.True(result.Succeeded);
        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.RecoveryResult?.Outcome);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(0, terminal.GetCheckoutCallCount);
        Assert.Equal(0, terminal.GetPaymentCallCount);
        var payment = Assert.Single(result.RecoveryResult!.Order!.Payments);
        Assert.Equal(PaymentMethodKind.Card, payment.Method);
        Assert.Equal(-10m, payment.Amount);
        Assert.True(CardRefundReference.TryGetRefundReference(payment.Reference, out var savedRefundReference));
        Assert.Equal(refundReference, savedRefundReference);
        Assert.True(CardRefundReference.TryGetOriginalReference(payment.Reference, out var savedOriginalReference));
        Assert.Equal(originalReference, savedOriginalReference);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task ResolveRefundAsync_square_confirm_refunded_partial_keeps_finalization_pending_until_order_save()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-PARTIAL-PENDING") with { CardAmount = 5m };
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Unknown,
            checkoutId: "CHECKOUT-REFUND-PARTIAL-PENDING",
            paymentId: null) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions)
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var service = CreateSquareService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeSquareTerminalPaymentClient());

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Square,
                CardRefundSupervisorDecision.ConfirmRefunded,
                Reason: "Matched partial Square refund",
                RefundReference: "SQUARE-REFUND-PARTIAL-PENDING"),
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult?.Outcome);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, attempts.RecoveryTargetStatus);
    }

    [Fact]
    public async Task RecoverAttemptAsync_square_partial_finalize_pending_with_matching_order_only_completes_cas()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-PARTIAL-EXISTING") with { CardAmount = 5m };
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Recovering,
            checkoutId: null) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions),
            ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedRefunded,
            SupervisorFinancialReference = "SQUARE-REFUND-PARTIAL-EXISTING",
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.OrderCompleted
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository(CreateExistingOrder(
            $"SQUARE_ATTEMPT:{attempt.AttemptGuid:N}"));
        var terminal = new FakeSquareTerminalPaymentClient();
        var cart = new PosCartService();
        var service = CreateSquareService(attempts, orders, terminal);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, terminal.GetCheckoutCallCount);
        Assert.Equal(0, terminal.GetPaymentCallCount);
        Assert.Equal(1, attempts.MarkOrderCompletedCount);
        Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
    }

    [Fact]
    public async Task RecoverAttemptAsync_square_partial_finalize_pending_with_mismatched_order_does_not_publish_cart()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-PARTIAL-MISMATCH") with { CardAmount = 5m };
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Recovering,
            checkoutId: null) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions),
            ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedRefunded,
            SupervisorFinancialReference = "SQUARE-REFUND-PARTIAL-MISMATCH",
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.OrderCompleted
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository(CreateExistingOrder(
            "SQUARE_ATTEMPT:11111111222233334444555555555555"));
        var terminal = new FakeSquareTerminalPaymentClient();
        var cart = new PosCartService();
        var service = CreateSquareService(attempts, orders, terminal);

        var result = await service.RecoverAttemptAsync(attempt.AttemptGuid, cart, Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, terminal.GetCheckoutCallCount);
        Assert.Equal(0, terminal.GetPaymentCallCount);
        Assert.Equal(0, attempts.MarkOrderCompletedCount);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
    }

    [Fact]
    public async Task ResolveAttemptAsync_square_paid_uses_real_supervisor_reference_without_provider_placeholder()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Recovering,
            "CHECKOUT-SUPERVISOR-PAID");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var service = CreateSquareService(attempts, orders, new FakeSquareTerminalPaymentClient());

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmProcessed,
            string.Empty,
            evidence: null,
            reference: "BANK-SQUARE-PAID-001",
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        var payment = Assert.Single(result.RecoveryResult!.Order!.Payments);
        Assert.Equal("BANK-SQUARE-PAID-001", payment.Reference);
        Assert.DoesNotContain("SUPERVISOR_CONFIRMED_PAID", payment.Reference, StringComparison.Ordinal);
        var transaction = Assert.Single(payment.CardTransactions!);
        Assert.Equal("Square Supervisor", transaction.Processor);
        Assert.Equal("BANK-SQUARE-PAID-001", transaction.TxnRef);
        Assert.DoesNotContain("SUPERVISOR_CONFIRMED_PAID", transaction.ResponseText ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(attempts.PaymentId);
    }

    [Fact]
    public async Task ResolveAttemptAsync_square_non_empty_cart_persists_not_paid_decision_without_overwriting_cart()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Recovering,
            "CHECKOUT-SUPERVISOR-NOT-PAID");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var service = CreateSquareService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeSquareTerminalPaymentClient());
        var cart = CreateCurrentCart();

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmNotProcessed,
            string.Empty,
            evidence: "Bank confirms no Square payment",
            reference: null,
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.Equal(ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, attempts.ResponseCode);
        Assert.Equal("CURRENT-SKU", Assert.Single(cart.Lines).ProductCode);
    }

    [Fact]
    public async Task ResolveAttemptAsync_square_not_paid_restores_sale_without_refund_retry_contract()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Recovering,
            "CHECKOUT-SUPERVISOR-NOT-PAID-RESTORE");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var service = CreateSquareService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeSquareTerminalPaymentClient());
        var cart = new PosCartService();

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmNotProcessed,
            string.Empty,
            evidence: "Bank confirms no Square payment",
            reference: null,
            cart,
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.False(result.RetryAllowed);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult?.Outcome);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Square, attempt.AttemptGuid),
            result.RecoveryResult?.DraftHandoffKey);
        Assert.Single(cart.Lines);

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));
        Assert.Equal(LocalSquarePaymentAttemptStatus.Abandoned, attempts.Status);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task ResolveAttemptAsync_square_saved_order_with_finalize_failure_succeeds_but_retains_lock()
    {
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Recovering,
            "CHECKOUT-SUPERVISOR-FINALIZE-FAIL");
        var attempts = new FakeSquarePaymentAttemptRepository(attempt)
        {
            FailRecoveryFinalization = true
        };
        var orders = new FakeLocalOrderRepository();
        var service = CreateSquareService(attempts, orders, new FakeSquareTerminalPaymentClient());

        var result = await service.ResolveAttemptAsync(
            attempt.AttemptGuid,
            CardRecoverySupervisorDecision.ConfirmProcessed,
            string.Empty,
            evidence: null,
            reference: "BANK-SQUARE-FINALIZE-FAIL",
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.True(result.RecoveryResult?.HasPostCommitWarning);
        Assert.Equal(1, orders.SaveCount);
    }

    [Fact]
    public async Task ResolveRefundAsync_square_order_save_failure_keeps_current_cart_and_persisted_resolution()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-SAVE-FAIL");
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Recovering,
            checkoutId: null) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions),
            SubmissionToken = "square-refund-save-fail"
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository
        {
            SaveException = new InvalidOperationException("database unavailable")
        };
        var service = CreateSquareService(attempts, orders, new FakeSquareTerminalPaymentClient());
        var cart = CreateCurrentCart();

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Square,
                CardRefundSupervisorDecision.ConfirmRefunded,
                Reason: "Matched settlement",
                RefundReference: "SQUARE-REFUND-SAVE-FAIL"),
            cart,
            Session);

        Assert.False(result.Succeeded);
        Assert.True(result.ResolutionPersisted);
        Assert.True(result.LockRetained);
        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.RecoveryResult?.Outcome);
        Assert.Equal("CURRENT-SKU", Assert.Single(cart.Lines).ProductCode);
    }

    [Fact]
    public async Task RecoverAttemptAsync_square_finalize_pending_refund_resumes_without_second_provider_lookup()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-RESUME");
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.PaymentVerified,
            checkoutId: null,
            paymentId: "SQUARE-REFUND-RESUME",
            paymentStatus: "COMPLETED") with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions),
            SubmissionToken = "square-refund-resume-token",
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.OrderCompleted
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var terminal = new FakeSquareTerminalPaymentClient();
        var orders = new FakeLocalOrderRepository();
        var service = CreateSquareService(attempts, orders, terminal);

        var result = await service.RecoverAttemptAsync(
            attempt.AttemptGuid,
            new PosCartService(),
            Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(0, terminal.GetRefundCallCount);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(LocalSquarePaymentAttemptStatus.OrderCompleted, attempts.Status);
    }

    [Fact]
    public async Task ResolveRefundAsync_square_confirm_not_refunded_preserves_idempotency_key_and_allows_retry()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-PAYMENT");
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Unknown,
            checkoutId: "CHECKOUT-REFUND",
            paymentId: null) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions)
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var service = CreateSquareService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeSquareTerminalPaymentClient());
        var cart = new PosCartService();

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Square,
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                Reason: "Checked Square settlement",
                Evidence: "No refund exists for this payment"),
            cart,
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.RetryAllowed);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult?.Outcome);
        Assert.Equal(
            new CardRecoveryAttemptKey(CardProcessorKind.Square, attempt.AttemptGuid),
            result.RecoveryResult?.DraftHandoffKey);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Pending, attempts.Status);
        Assert.Equal(CardRecoveryPhases.FinalizePending, attempts.RecoveryPhase);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Abandoned, attempts.RecoveryTargetStatus);
        Assert.Equal(attempt.IdempotencyKey, attempts.IdempotencyKey);
        Assert.Null(attempts.CheckoutId);
        Assert.Null(attempts.PaymentId);
        Assert.Single(cart.Lines);
        Assert.Equal(attempt.AttemptGuid, cart.RecoveryOwnerAttemptGuid);

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));

        Assert.Equal(LocalSquarePaymentAttemptStatus.Abandoned, attempts.Status);
        Assert.Equal(CardRecoveryPhases.None, attempts.RecoveryPhase);
        Assert.Null(attempts.RecoveryTargetStatus);
        Assert.Null(cart.RecoveryOwnerAttemptGuid);
    }

    [Fact]
    public async Task CompleteDraftHandoffAsync_square_missing_attempt_keeps_exact_owner_locked()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-HANDOFF-MISSING");
        var attemptGuid = Guid.NewGuid();
        var attemptKey = new CardRecoveryAttemptKey(CardProcessorKind.Square, attemptGuid);
        var cart = new PosCartService();
        Assert.True(cart.TryPublishRecoverySnapshot(
            attemptKey,
            cart.Revision,
            draft.CartSnapshot).Succeeded);
        var service = CreateSquareService(
            new FakeSquarePaymentAttemptRepository(null),
            new FakeLocalOrderRepository(),
            new FakeSquareTerminalPaymentClient());

        Assert.False(await service.CompleteDraftHandoffAsync(attemptGuid, cart));

        Assert.Equal(attemptKey, cart.RecoveryOwnerAttemptKey);
        Assert.False(cart.IsEmpty);
    }

    [Fact]
    public async Task CompleteDraftHandoffAsync_square_changed_decision_keeps_exact_owner_locked()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-HANDOFF-CHANGED");
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Pending,
            checkoutId: null) with
        {
            OperationKind = "Refund",
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions),
            ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.Abandoned
        };
        var changedWinner = attempt with
        {
            ResponseCode = CardRefundSupervisorResolutionCodes.ContinueWaiting,
            UpdatedAt = attempt.UpdatedAt.AddTicks(1)
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt)
        {
            RecoveryFinalizationWinner = changedWinner
        };
        var attemptKey = new CardRecoveryAttemptKey(CardProcessorKind.Square, attempt.AttemptGuid);
        var cart = new PosCartService();
        Assert.True(cart.TryPublishRecoverySnapshot(
            attemptKey,
            cart.Revision,
            draft.CartSnapshot).Succeeded);
        var service = CreateSquareService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeSquareTerminalPaymentClient());

        Assert.False(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));

        Assert.Equal(CardRefundSupervisorResolutionCodes.ContinueWaiting, attempts.ResponseCode);
        Assert.Equal(attemptKey, cart.RecoveryOwnerAttemptKey);
    }

    [Fact]
    public async Task CompleteDraftHandoffAsync_square_exact_cas_winner_releases_exact_owner()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-HANDOFF-WINNER");
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Pending,
            checkoutId: null) with
        {
            OperationKind = "Refund",
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions),
            ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
            RecoveryPhase = CardRecoveryPhases.FinalizePending,
            RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.Abandoned
        };
        var terminalWinner = attempt with
        {
            Status = LocalSquarePaymentAttemptStatus.Abandoned,
            RecoveryPhase = CardRecoveryPhases.None,
            RecoveryTargetStatus = null,
            UpdatedAt = attempt.UpdatedAt.AddTicks(1)
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt)
        {
            RecoveryFinalizationWinner = terminalWinner
        };
        var attemptKey = new CardRecoveryAttemptKey(CardProcessorKind.Square, attempt.AttemptGuid);
        var cart = new PosCartService();
        Assert.True(cart.TryPublishRecoverySnapshot(
            attemptKey,
            cart.Revision,
            draft.CartSnapshot).Succeeded);
        var service = CreateSquareService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeSquareTerminalPaymentClient());

        Assert.True(await service.CompleteDraftHandoffAsync(attempt.AttemptGuid, cart));

        Assert.Equal(LocalSquarePaymentAttemptStatus.Abandoned, attempts.Status);
        Assert.Null(cart.RecoveryOwnerAttemptKey);
        Assert.False(cart.IsEmpty);
    }

    [Fact]
    public async Task ResolveRefundAsync_square_continue_waiting_keeps_attempt_locked()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-PAYMENT");
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Unknown,
            checkoutId: "CHECKOUT-REFUND") with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions)
        };
        var attempts = new FakeSquarePaymentAttemptRepository(attempt);
        var service = CreateSquareService(
            attempts,
            new FakeLocalOrderRepository(),
            new FakeSquareTerminalPaymentClient());

        var result = await service.ResolveRefundAsync(
            new CardRefundSupervisorResolution(
                attempt.AttemptGuid,
                CardProcessorKind.Square,
                CardRefundSupervisorDecision.ContinueWaiting,
                Reason: "Settlement is still pending"),
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(CardRefundSupervisorResolutionCodes.ContinueWaiting, attempts.ResponseCode);
    }

    [Fact]
    public async Task RecoverLatestAsync_linkly_local_attempt_lookup_does_not_block_caller_thread()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var attempts = new FakeCardPaymentAttemptRepository(null)
        {
            GetLatestEntered = entered,
            GetLatestRelease = release
        };
        var service = CreateService(attempts, new FakeLocalOrderRepository(), new FakeLinklyBackendTerminalClient());

        var startedAt = Environment.TickCount64;
        var recovery = service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.True(Environment.TickCount64 - startedAt < 150);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(recovery.IsCompleted);

        release.Set();
        Assert.Equal(CardPaymentRecoveryOutcome.None, (await recovery).Outcome);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_local_attempt_lookup_does_not_block_caller_thread()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var attempts = new FakeSquarePaymentAttemptRepository(null)
        {
            GetLatestEntered = entered,
            GetLatestRelease = release
        };
        var service = CreateSquareService(attempts, new FakeLocalOrderRepository(), new FakeSquareTerminalPaymentClient());

        var startedAt = Environment.TickCount64;
        var recovery = service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.True(Environment.TickCount64 - startedAt < 150);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
        Assert.False(recovery.IsCompleted);

        release.Set();
        Assert.Equal(CardPaymentRecoveryOutcome.None, (await recovery).Outcome);
    }

    [Fact]
    public async Task RecoverLatestAsync_linkly_approved_after_ui_cancellation_persists_with_non_cancelable_token()
    {
        using var cancelled = new CancellationTokenSource();
        var attempts = new FakeCardPaymentAttemptRepository(CreateAttempt("SESSION-001", "TXN-001"));
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", "SESSION-001", "TXN-001", "00", "APPROVED", transactionSuccess: true),
            OnGetSessionStatus = cancelled.Cancel
        };
        var service = CreateService(attempts, orders, backend);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session, cancelled.Token);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.False(attempts.LastUpdateOutcomeCancellationToken.CanBeCanceled);
        Assert.False(attempts.LastMarkOrderCompletedCancellationToken.CanBeCanceled);
        Assert.False(orders.LastGetOrderCancellationToken.CanBeCanceled);
        Assert.False(orders.LastSaveCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_approved_after_ui_cancellation_persists_with_non_cancelable_token()
    {
        using var cancelled = new CancellationTokenSource();
        var attempts = new FakeSquarePaymentAttemptRepository(CreateSquareAttempt(LocalSquarePaymentAttemptStatus.CheckoutCreated, "CHECKOUT-001"));
        var orders = new FakeLocalOrderRepository();
        var terminal = new FakeSquareTerminalPaymentClient
        {
            OnGetPayment = cancelled.Cancel
        };
        var service = CreateSquareService(attempts, orders, terminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session, cancelled.Token);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.False(attempts.LastMarkPaymentVerifiedCancellationToken.CanBeCanceled);
        Assert.False(attempts.LastMarkOrderCompletedCancellationToken.CanBeCanceled);
        Assert.False(orders.LastGetOrderCancellationToken.CanBeCanceled);
        Assert.False(orders.LastSaveCancellationToken.CanBeCanceled);
    }

    private static CardPaymentRecoveryService CreateService(
        FakeCardPaymentAttemptRepository attempts,
        FakeLocalOrderRepository orders,
        FakeLinklyBackendTerminalClient backend,
        FakeCardTerminalSettingsProvider? settingsProvider = null,
        ILinklyTerminalClient? linklyTerminalClient = null,
        ISharedHeldOrderRepository? sharedHeldOrderRepository = null,
        ILocalizationService? localization = null)
    {
        return new CardPaymentRecoveryService(
            attempts,
            settingsProvider ?? new FakeCardTerminalSettingsProvider(),
            backend,
            new CashCheckoutService(),
            orders,
            new FakeSyncQueueRepository(),
            localization,
            linklyTerminalClient: linklyTerminalClient,
            sharedHeldOrderRepository: sharedHeldOrderRepository);
    }

    private static SquarePaymentRecoveryService CreateSquareService(
        FakeSquarePaymentAttemptRepository attempts,
        FakeLocalOrderRepository orders,
        FakeSquareTerminalPaymentClient terminal,
        ISharedHeldOrderRepository? sharedHeldOrderRepository = null)
    {
        return new SquarePaymentRecoveryService(
            attempts,
            new FakeSquareCardTerminalSettingsProvider(),
            terminal,
            new CashCheckoutService(),
            orders,
            sharedHeldOrderRepository: sharedHeldOrderRepository);
    }

    private static PaymentCasWinnerState CreatePaymentCasWinner(
        LocalCardPaymentAttempt attempt,
        ActiveSessionSupervisorDecision decision)
    {
        var resolvedAt = attempt.UpdatedAt.AddSeconds(1);
        var reason = "Concurrent supervisor winner";
        var evidence = decision == ActiveSessionSupervisorDecision.ConfirmNotPaid
            ? "Winner bank evidence"
            : null;
        var paymentReference = decision == ActiveSessionSupervisorDecision.ConfirmPaid
            ? "WINNER-PAYMENT-REFERENCE"
            : null;
        var resolution = new ActiveSessionResolution(
            attempt.AttemptGuid,
            attempt.SessionId ?? attempt.TxnRef ?? throw new InvalidOperationException("Missing payment session key."),
            decision,
            attempt.Status,
            attempt.UpdatedAt,
            reason,
            evidence,
            paymentReference,
            resolvedAt);
        var journal = new LocalFinancialSupervisorResolution(
            Guid.NewGuid(),
            LocalFinancialSupervisorResolutionTarget.ActiveSession,
            attempt.Processor,
            attempt.Environment,
            attempt.StoreCode,
            attempt.DeviceCode,
            attempt.AttemptGuid,
            RefundStepGuid: null,
            attempt.OperationGuid,
            attempt.SessionId ?? attempt.TxnRef,
            decision.ToString(),
            "WINNER-MANAGER",
            "WINNER-USER",
            "Winner Manager",
            reason,
            evidence,
            paymentReference,
            RetryReference: null,
            resolvedAt,
            Guid.NewGuid(),
            "{}");
        return new PaymentCasWinnerState(resolution, journal);
    }

    private static LocalCardPaymentAttempt CreateAttempt(
        string? sessionId,
        string? txnRef,
        LocalCardPaymentAttemptStatus status = LocalCardPaymentAttemptStatus.SessionStarted,
        DateTimeOffset? acknowledgedAt = null,
        string cashierId = "C001",
        LinklyConnectionMode connectionMode = LinklyConnectionMode.CloudBackendAsync,
        decimal? amount = null,
        CardPaymentOrderDraft? draft = null)
    {
        var now = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
        var attemptDraft = draft ?? CreateDraft();
        return new LocalCardPaymentAttempt(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            sessionId,
            txnRef,
            "Linkly",
            "Sandbox",
            connectionMode.ToString(),
            "P",
            amount ?? Math.Abs(attemptDraft.CardAmount),
            status,
            JsonSerializer.Serialize(attemptDraft, JsonOptions),
            "S001",
            "POS-01",
            cashierId,
            null,
            null,
            null,
            now.AddMinutes(-2),
            now.AddMinutes(-1),
            status == LocalCardPaymentAttemptStatus.OrderCompleted ? now.AddMinutes(-1) : null,
            acknowledgedAt);
    }

    private static CardTransactionDto CreateLocalCardTransaction(
        string txnRef,
        string responseCode,
        string responseText,
        decimal amount = 10m)
    {
        return new CardTransactionDto(
            "ANZ",
            txnRef,
            null,
            null,
            null,
            null,
            null,
            responseCode,
            responseText,
            null,
            DateTimeOffset.UtcNow,
            amount,
            "MERCHANT COPY");
    }

    private static LocalSquarePaymentAttempt CreateSquareAttempt(
        LocalSquarePaymentAttemptStatus status,
        string? checkoutId,
        string? paymentId = null,
        string? paymentStatus = null)
    {
        var now = DateTimeOffset.Parse("2026-06-05T10:00:00+10:00");
        return new LocalSquarePaymentAttempt(
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            checkoutId,
            "idem-square-001",
            "DEVICE-001",
            "LOCATION-001",
            "Sandbox",
            10m,
            1000,
            "AUD",
            status,
            checkoutId is null ? null : "COMPLETED",
            null,
            JsonSerializer.Serialize(CreateDraft(), JsonOptions),
            "S001",
            "POS-01",
            "C001",
            paymentId,
            paymentStatus,
            null,
            null,
            now.AddMinutes(-2),
            now.AddMinutes(-1),
            paymentId is null ? null : now.AddMinutes(-1),
            null,
            null);
    }

    private static CardPaymentOrderDraft CreateDraft(
        decimal actualAmount = 10m,
        decimal cardAmount = 10m,
        IReadOnlyList<PaymentTender>? currentTenders = null)
    {
        return new CardPaymentOrderDraft(
            CreateOrderGuid(),
            Session,
            new PosCartSnapshot(
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
                    Math.Abs(actualAmount),
                    0m,
                    null,
                    PriceSourceKind.StoreRetailPrice,
                    "Store price")
            ]),
            currentTenders ?? [],
            actualAmount,
            cardAmount,
            "P",
            null,
            DateTimeOffset.Parse("2026-06-05T10:00:00+10:00"));
    }

    private static CardPaymentOrderDraft CreateSemanticallyInvalidDraft()
    {
        var draft = CreateDraft();
        var invalidLine = draft.CartSnapshot.Lines[0] with { Quantity = 0.5m };
        return draft with { CartSnapshot = new PosCartSnapshot([invalidLine]) };
    }

    /// <summary>与 CreateDraft 冻结购物车（SKU-10, $10.00）逐行一致的 claim canonical。</summary>
    private static SharedHeldOrderCanonicalPayload CanonicalForDraftCart(decimal quantity = 1m)
    {
        return new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                1,
                SharedHeldOrderCanonicalConstants.SaleMode,
                "2026-07-28T00:00:00.000Z",
                [],
                [
                    new SharedHeldOrderPricingLine(
                        "line-1",
                        "SKU-10",
                        "ITEM-10",
                        "930010",
                        "Test Item",
                        quantity,
                        1000,
                        SharedHeldOrderCanonicalConstants.BasePriceSourceCatalog,
                        new SharedHeldOrderLineSyncProvenance(null, (int)PriceSourceKind.StoreRetailPrice),
                        SharedHeldOrderCanonicalConstants.LineKindSale,
                        null,
                        null,
                        null,
                        new SharedHeldOrderDiscountState(SharedHeldOrderCanonicalConstants.DiscountNone))
                ]));
    }

    private static CardPaymentOrderDraft CreateRefundDraft(string originalReference)
    {
        var cart = new PosCartService();
        cart.AddReturnLine(new ReturnCartLineRequest(
            "S001",
            "SKU-REFUND-10",
            null,
            "Returned Item",
            "930REFUND10",
            "ITEM-REFUND-10",
            null,
            1m,
            10m,
            PriceSourceKind.StoreRetailPrice,
            PriceSourceKind.StoreRetailPrice.ToString(),
            "RETURN-REFUND-10",
            Guid.Parse("22222222-3333-4444-5555-666666666666"),
            Guid.Parse("33333333-4444-5555-6666-777777777777")));
        return new CardPaymentOrderDraft(
            CreateOrderGuid(),
            Session,
            cart.CreateSnapshot(),
            [],
            cart.ActualAmount,
            10m,
            "R",
            originalReference,
            DateTimeOffset.Parse("2026-06-05T10:00:00+10:00"));
    }

    private static PosCartService CreateCurrentCart()
    {
        var cart = new PosCartService();
        AddCurrentCartItem(cart);
        return cart;
    }

    private static void AddCurrentCartItem(PosCartService cart)
    {
        cart.AddItem(new SellableItemDto(
            "S001",
            "CURRENT-SKU",
            null,
            "Current Cart Item",
            "930020",
            "ITEM-CURRENT",
            "930020",
            2m,
            PriceSourceKind.StoreRetailPrice,
            "Store price",
            1m,
            DateTimeOffset.UtcNow,
            null));
    }

    private static Guid CreateOrderGuid()
    {
        return Guid.Parse("11111111-2222-3333-4444-555555555555");
    }

    /// <summary>订单已保存（与 claim 同事务绑定完成）、attempt 未收尾的既有订单。</summary>
    private static LocalOrder CreateExistingOrder(string paymentIdempotencyKey)
    {
        return new LocalOrder(
            CreateOrderGuid(),
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
                    PaymentMethodKind.Card,
                    10m,
                    "ANZ:EXISTING-ORDER",
                    IdempotencyKey: paymentIdempotencyKey)
            ]);
    }

    private static LinklyCloudBackendSessionResponse CreateStatus(
        string status,
        string sessionId,
        string? txnRef,
        string? responseCode,
        string? responseText,
        bool? transactionSuccess = null,
        IReadOnlyList<LinklyCloudBackendNotificationDto>? notifications = null,
        string? receiptText = "RECEIPT")
    {
        return new LinklyCloudBackendSessionResponse(
            "Sandbox",
            "S001",
            "POS-01",
            sessionId,
            status,
            txnRef,
            responseCode,
            responseText,
            null,
            responseText,
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            null,
            receiptText,
            0,
            null,
            null,
            200,
            notifications ?? [],
            transactionSuccess);
    }

    private sealed record PaymentCasWinnerState(
        ActiveSessionResolution Resolution,
        LocalFinancialSupervisorResolution Journal);

    private sealed class FakeCardPaymentAttemptRepository(LocalCardPaymentAttempt? attempt) : ILocalCardPaymentAttemptRepository
    {
        private LocalCardPaymentAttempt? _attempt = attempt;

        public LocalCardPaymentAttemptStatus Status => _attempt?.Status ?? LocalCardPaymentAttemptStatus.Failed;

        public string? SessionId => _attempt?.SessionId;

        public string? TxnRef => _attempt?.TxnRef;

        public string? PaymentReference => _attempt?.PaymentReference;

        public string? ResponseCode => _attempt?.ResponseCode;

        public string? ResponseText => _attempt?.ResponseText;

        public DateTimeOffset? AcknowledgedAt => _attempt?.AcknowledgedAt;

        public string? RecoveryPhase => _attempt?.RecoveryPhase;

        public string? RecoveryTargetStatus => _attempt?.RecoveryTargetStatus;

        public string? LastCashierId { get; private set; }

        public ManualResetEventSlim? GetLatestEntered { get; init; }

        public ManualResetEventSlim? GetLatestRelease { get; init; }

        public CancellationToken LastUpdateOutcomeCancellationToken { get; private set; }

        public CancellationToken LastMarkOrderCompletedCancellationToken { get; private set; }

        public Exception? MarkOrderCompletedException { get; init; }

        public bool FailRecoveryFinalization { get; init; }

        public LocalCardPaymentAttempt? RecoveryFinalizationWinner { get; init; }

        public Exception? UpdateOutcomeException { get; init; }

        public int ResolveRefundCount { get; private set; }

        public CardRefundAttemptResolution? LastRefundResolution { get; private set; }

        public CardRefundAttemptResolution? RefundCasWinner { get; init; }

        public int CreateActiveSessionCount { get; private set; }

        public string? OperationKind => _attempt?.OperationKind;

        public LocalFinancialSupervisorResolution? LastPaymentJournal { get; private set; }

        public PaymentCasWinnerState? PaymentCasWinner { get; init; }

        public bool RejectPaymentResolution { get; init; }

        public Exception? PostCommitGetAttemptException { get; init; }

        public Task CreateAsync(LocalCardPaymentAttempt attempt, CancellationToken cancellationToken = default)
        {
            _attempt ??= attempt;
            return Task.CompletedTask;
        }

        public Task<LocalCardPaymentAttempt> CreateOrGetActiveSessionAsync(
            LocalCardPaymentAttempt attempt,
            CancellationToken cancellationToken = default)
        {
            CreateActiveSessionCount++;
            _attempt ??= attempt;
            return Task.FromResult(_attempt);
        }

        public Task UpdateSessionAsync(Guid attemptGuid, string sessionId, string? txnRef, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
        {
            _attempt = _attempt! with
            {
                SessionId = sessionId,
                TxnRef = txnRef ?? _attempt.TxnRef,
                Status = LocalCardPaymentAttemptStatus.SessionStarted,
                UpdatedAt = updatedAt
            };
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
            LastUpdateOutcomeCancellationToken = cancellationToken;
            if (UpdateOutcomeException is not null)
            {
                throw UpdateOutcomeException;
            }

            _attempt = _attempt! with
            {
                Status = status,
                ResponseCode = responseCode,
                ResponseText = responseText,
                PaymentReference = paymentReference,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            };
            return Task.CompletedTask;
        }

        public Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
        {
            LastMarkOrderCompletedCancellationToken = cancellationToken;
            if (MarkOrderCompletedException is not null)
            {
                throw MarkOrderCompletedException;
            }

            _attempt = _attempt! with
            {
                Status = LocalCardPaymentAttemptStatus.OrderCompleted,
                RecoveryPhase = CardRecoveryPhases.None,
                RecoveryTargetStatus = null,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            };
            return Task.CompletedTask;
        }

        public Task MarkAcknowledgedAsync(Guid attemptGuid, DateTimeOffset acknowledgedAt, CancellationToken cancellationToken = default)
        {
            _attempt = _attempt! with
            {
                AcknowledgedAt = acknowledgedAt,
                UpdatedAt = acknowledgedAt
            };
            return Task.CompletedTask;
        }

        public Task<bool> TryMarkAcknowledgedAsync(
            Guid attemptGuid,
            LocalCardPaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset acknowledgedAt,
            CancellationToken cancellationToken = default)
        {
            if (_attempt is null ||
                _attempt.AttemptGuid != attemptGuid ||
                _attempt.Status != expectedStatus ||
                _attempt.UpdatedAt != expectedUpdatedAt ||
                string.Equals(_attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal) &&
                !CanAcknowledgeFinalizePendingSale(_attempt))
            {
                return Task.FromResult(false);
            }

            _attempt = _attempt with
            {
                AcknowledgedAt = acknowledgedAt,
                UpdatedAt = acknowledgedAt
            };
            return Task.FromResult(true);
        }

        private static bool CanAcknowledgeFinalizePendingSale(LocalCardPaymentAttempt attempt)
        {
            if (!string.Equals(attempt.Processor, CardProcessorKind.Linkly.ToString(), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(attempt.OperationKind, "Sale", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(
                    attempt.RecoveryTargetStatus,
                    LocalCardPaymentAttemptStatus.Abandoned.ToString(),
                    StringComparison.Ordinal))
            {
                return string.Equals(
                    attempt.ResponseCode,
                    ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
                    StringComparison.Ordinal);
            }

            return Enum.TryParse<LocalCardPaymentAttemptStatus>(
                       attempt.RecoveryTargetStatus,
                       ignoreCase: false,
                       out var targetStatus) &&
                targetStatus is
                    LocalCardPaymentAttemptStatus.Declined or
                    LocalCardPaymentAttemptStatus.TimedOut or
                    LocalCardPaymentAttemptStatus.Cancelled or
                    LocalCardPaymentAttemptStatus.Failed &&
                !LinklyApprovalResponseCodes.IsApproved(attempt.ResponseCode) &&
                !string.Equals(attempt.ResponseCode, ActiveSessionSupervisorResolutionCodes.ConfirmedPaid, StringComparison.Ordinal) &&
                !string.Equals(attempt.ResponseCode, ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid, StringComparison.Ordinal) &&
                !string.Equals(attempt.ResponseCode, ActiveSessionSupervisorResolutionCodes.ContinueWaiting, StringComparison.Ordinal) &&
                !string.Equals(attempt.ResponseCode, CardRefundSupervisorResolutionCodes.ConfirmedRefunded, StringComparison.Ordinal) &&
                !string.Equals(attempt.ResponseCode, CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded, StringComparison.Ordinal);
        }

        public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
        {
            _attempt = _attempt! with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                UpdatedAt = updatedAt
            };
            return Task.CompletedTask;
        }

        public Task<bool> TryMarkRecoveringAsync(
            Guid attemptGuid,
            LocalCardPaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            if (_attempt is null ||
                _attempt.AttemptGuid != attemptGuid ||
                _attempt.Status != expectedStatus ||
                _attempt.UpdatedAt != expectedUpdatedAt ||
                string.Equals(_attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _attempt = _attempt with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                UpdatedAt = updatedAt
            };
            return Task.FromResult(true);
        }

        public Task<bool> TryPersistRecoveryOutcomeAsync(
            Guid attemptGuid,
            LocalCardPaymentAttemptStatus openStatus,
            string? responseCode,
            string? responseText,
            string? paymentReference,
            LocalCardPaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            LocalCardPaymentAttemptStatus recoveryTargetStatus,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            if (UpdateOutcomeException is not null)
            {
                throw UpdateOutcomeException;
            }

            if (_attempt is null ||
                _attempt.AttemptGuid != attemptGuid ||
                _attempt.Status != expectedStatus ||
                _attempt.UpdatedAt != expectedUpdatedAt ||
                string.Equals(_attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _attempt = _attempt with
            {
                Status = openStatus,
                ResponseCode = responseCode,
                ResponseText = responseText,
                PaymentReference = paymentReference,
                RecoveryPhase = CardRecoveryPhases.FinalizePending,
                RecoveryTargetStatus = recoveryTargetStatus.ToString(),
                UpdatedAt = updatedAt
            };
            return Task.FromResult(true);
        }

        public Task<bool> TryFinalizeRecoveryOutcomeAsync(
            Guid attemptGuid,
            LocalCardPaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            LocalCardPaymentAttemptStatus recoveryTargetStatus,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            LastMarkOrderCompletedCancellationToken = cancellationToken;
            if (MarkOrderCompletedException is not null)
            {
                throw MarkOrderCompletedException;
            }

            if (RecoveryFinalizationWinner is not null)
            {
                _attempt = RecoveryFinalizationWinner;
                return Task.FromResult(false);
            }

            if (FailRecoveryFinalization ||
                _attempt is null ||
                _attempt.AttemptGuid != attemptGuid ||
                _attempt.Status != expectedStatus ||
                _attempt.UpdatedAt != expectedUpdatedAt ||
                !string.Equals(_attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal) ||
                !string.Equals(_attempt.RecoveryTargetStatus, recoveryTargetStatus.ToString(), StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _attempt = _attempt with
            {
                Status = recoveryTargetStatus,
                RecoveryPhase = CardRecoveryPhases.None,
                RecoveryTargetStatus = null,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            };
            return Task.FromResult(true);
        }

        public Task<bool> TryRetargetRecoveryFinalizationAsync(
            Guid attemptGuid,
            LocalCardPaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            LocalCardPaymentAttemptStatus expectedTargetStatus,
            LocalCardPaymentAttemptStatus targetStatus,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            if (_attempt is null ||
                _attempt.AttemptGuid != attemptGuid ||
                _attempt.Status != expectedStatus ||
                _attempt.UpdatedAt != expectedUpdatedAt ||
                !string.Equals(_attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal) ||
                !string.Equals(_attempt.RecoveryTargetStatus, expectedTargetStatus.ToString(), StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _attempt = _attempt with
            {
                RecoveryTargetStatus = targetStatus.ToString(),
                UpdatedAt = updatedAt
            };
            return Task.FromResult(true);
        }

        public Task<LocalCardPaymentAttempt?> GetLatestOpenAttemptAsync(
            string storeCode,
            string deviceCode,
            string? cashierId,
            string environment,
            CancellationToken cancellationToken = default)
        {
            LastCashierId = cashierId;
            GetLatestEntered?.Set();
            GetLatestRelease?.Wait(cancellationToken);
            return Task.FromResult<LocalCardPaymentAttempt?>(_attempt);
        }

        public Task<IReadOnlyList<LocalCardPaymentAttempt>> GetOpenRefundAttemptsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalCardPaymentAttempt>>(
                _attempt?.OperationKind == "Refund" ? [_attempt] : []);
        }

        public Task<LocalCardPaymentAttempt?> GetLatestOpenActiveSessionAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _attempt?.OperationKind == "ActiveSession" && _attempt.AcknowledgedAt is null
                    ? _attempt
                    : null);
        }

        public Task<bool> ResolvePaymentWithJournalAsync(
            ActiveSessionResolution resolution,
            LocalFinancialSupervisorResolution journal,
            CancellationToken cancellationToken = default)
        {
            if (_attempt is null ||
                _attempt.AttemptGuid != resolution.AttemptGuid ||
                _attempt.Status != resolution.ExpectedStatus ||
                _attempt.UpdatedAt != resolution.ExpectedUpdatedAt)
            {
                return Task.FromResult(false);
            }

            if (PaymentCasWinner is not null)
            {
                PersistPaymentResolution(PaymentCasWinner.Resolution, PaymentCasWinner.Journal);
                return Task.FromResult(false);
            }

            if (RejectPaymentResolution)
            {
                return Task.FromResult(false);
            }

            PersistPaymentResolution(resolution, journal);
            return Task.FromResult(true);
        }

        private void PersistPaymentResolution(
            ActiveSessionResolution resolution,
            LocalFinancialSupervisorResolution journal)
        {
            LastPaymentJournal = journal;
            _attempt = _attempt! with
            {
                Status = resolution.Decision switch
                {
                    ActiveSessionSupervisorDecision.ConfirmPaid => LocalCardPaymentAttemptStatus.Approved,
                    ActiveSessionSupervisorDecision.ConfirmNotPaid => LocalCardPaymentAttemptStatus.Recovering,
                    _ => LocalCardPaymentAttemptStatus.Recovering
                },
                ResponseCode = resolution.Decision switch
                {
                    ActiveSessionSupervisorDecision.ConfirmPaid => ActiveSessionSupervisorResolutionCodes.ConfirmedPaid,
                    ActiveSessionSupervisorDecision.ConfirmNotPaid => ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
                    _ => ActiveSessionSupervisorResolutionCodes.ContinueWaiting
                },
                ResponseText = resolution.Reason,
                PaymentReference = resolution.PaymentReference,
                RecoveryPhase = resolution.Decision == ActiveSessionSupervisorDecision.ContinueWaiting
                    ? CardRecoveryPhases.None
                    : CardRecoveryPhases.FinalizePending,
                RecoveryTargetStatus = resolution.Decision switch
                {
                    ActiveSessionSupervisorDecision.ConfirmPaid => LocalCardPaymentAttemptStatus.OrderCompleted.ToString(),
                    ActiveSessionSupervisorDecision.ConfirmNotPaid => LocalCardPaymentAttemptStatus.Abandoned.ToString(),
                    _ => null
                },
                UpdatedAt = resolution.ResolvedAt
            };
        }

        public Task<bool> ResolveRefundAsync(
            CardRefundAttemptResolution resolution,
            CancellationToken cancellationToken = default)
        {
            ResolveRefundCount++;
            LastRefundResolution = resolution;
            if (_attempt is null ||
                _attempt.OperationKind != "Refund" ||
                _attempt.ResponseCode is CardRefundSupervisorResolutionCodes.ConfirmedRefunded or
                    CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded)
            {
                return Task.FromResult(false);
            }

            _attempt = resolution.Decision switch
            {
                CardRefundSupervisorDecision.ConfirmRefunded => _attempt with
                {
                    Status = LocalCardPaymentAttemptStatus.Approved,
                    ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedRefunded,
                    ResponseText = resolution.Reason,
                    PaymentReference = resolution.RefundReference,
                    RecoveryPhase = CardRecoveryPhases.FinalizePending,
                    RecoveryTargetStatus = LocalCardPaymentAttemptStatus.OrderCompleted.ToString(),
                    UpdatedAt = resolution.ResolvedAt
                },
                CardRefundSupervisorDecision.ConfirmNotRefunded => _attempt with
                {
                    SessionId = null,
                    TxnRef = resolution.RetryTxnRef,
                    Status = LocalCardPaymentAttemptStatus.Pending,
                    ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
                    ResponseText = resolution.Evidence,
                    PaymentReference = null,
                    RecoveryPhase = CardRecoveryPhases.FinalizePending,
                    RecoveryTargetStatus = LocalCardPaymentAttemptStatus.Abandoned.ToString(),
                    UpdatedAt = resolution.ResolvedAt
                },
                _ => _attempt with
                {
                    Status = LocalCardPaymentAttemptStatus.Recovering,
                    ResponseCode = CardRefundSupervisorResolutionCodes.ContinueWaiting,
                    ResponseText = resolution.Reason,
                    RecoveryPhase = CardRecoveryPhases.None,
                    RecoveryTargetStatus = null,
                    UpdatedAt = resolution.ResolvedAt
                }
            };
            return Task.FromResult(true);
        }

        public async Task<bool> ResolveRefundWithJournalAsync(
            CardRefundAttemptResolution resolution,
            LocalCardPaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            LocalFinancialSupervisorResolution journal,
            CancellationToken cancellationToken = default)
        {
            if (RefundCasWinner is null)
            {
                return await ResolveRefundAsync(resolution, cancellationToken);
            }

            // 中文注释：在被测 CAS 内注入相反主管决议，模拟另一个调用先提交成功。
            _ = await ResolveRefundAsync(RefundCasWinner, cancellationToken);
            return false;
        }

        public Task<LocalCardPaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default)
        {
            if (PostCommitGetAttemptException is not null &&
                (LastPaymentJournal is not null || LastRefundResolution is not null))
            {
                throw PostCommitGetAttemptException;
            }

            return Task.FromResult<LocalCardPaymentAttempt?>(_attempt);
        }
    }

    private sealed class FakeCardTerminalSettingsProvider(
        LinklyConnectionMode connectionMode = LinklyConnectionMode.CloudBackendAsync,
        IReadOnlyList<LinklyConnectionMode>? priority = null) : ICardTerminalSettingsProvider
    {
        public Task<CardTerminalSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CardTerminalSettings(
                CardProcessorKind.Linkly,
                CardTerminalEnvironment.Sandbox,
                "127.0.0.1",
                2011,
                null,
                null,
                null,
                "https://connect.squareupsandbox.com",
                TimeSpan.FromSeconds(30),
                connectionMode,
                LinklyConnectionModePriority: priority));
        }
    }

    private sealed class FakeLinklyTerminalClient(PaymentAuthorizationResult recoverResult) : ILinklyTerminalClient
    {
        public int RecoverCallCount { get; private set; }

        public string? LastTxnRef { get; private set; }

        public Task<LinklyConnectionTestResult> TestConnectionAsync(
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentAuthorizationResult> PurchaseAsync(
            decimal amount,
            PosSessionState session,
            CardTerminalSettings settings,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentAuthorizationResult> PurchaseWithReferenceAsync(
            decimal amount,
            PosSessionState session,
            CardTerminalSettings settings,
            string txnRef,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentAuthorizationResult> RecoverLastTransactionAsync(
            decimal amount,
            PosSessionState session,
            CardTerminalSettings settings,
            string txnRef,
            CancellationToken cancellationToken = default)
        {
            RecoverCallCount++;
            LastTxnRef = txnRef;
            return Task.FromResult(recoverResult);
        }

        public Task<PaymentAuthorizationResult> RefundAsync(
            decimal amount,
            PosSessionState session,
            CardTerminalSettings settings,
            string? originalReference,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentAuthorizationResult> VoidAsync(
            decimal amount,
            PosSessionState session,
            CardTerminalSettings settings,
            string? originalReference,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeSquareCardTerminalSettingsProvider : ICardTerminalSettingsProvider
    {
        public Task<CardTerminalSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CardTerminalSettings(
                CardProcessorKind.Square,
                CardTerminalEnvironment.Sandbox,
                "127.0.0.1",
                2011,
                "DEVICE-001",
                "LOCATION-001",
                "token",
                "https://connect.squareupsandbox.com",
                TimeSpan.FromSeconds(30),
                LinklyConnectionMode.LocalIp));
        }
    }

    private sealed class FakeSquarePaymentAttemptRepository(LocalSquarePaymentAttempt? attempt) : ILocalSquarePaymentAttemptRepository
    {
        private LocalSquarePaymentAttempt? _attempt = attempt;

        public LocalSquarePaymentAttemptStatus Status { get; private set; } = attempt?.Status ?? LocalSquarePaymentAttemptStatus.Failed;

        public string? IdempotencyKey => _attempt?.IdempotencyKey;

        public string? CheckoutId => _attempt?.CheckoutId;

        public string? PaymentId => _attempt?.PaymentId;

        public string? ResponseCode => _attempt?.ResponseCode;

        public string? RecoveryPhase => _attempt?.RecoveryPhase;

        public LocalSquarePaymentAttemptStatus? RecoveryTargetStatus => _attempt?.RecoveryTargetStatus;

        public int MarkFailedCount { get; private set; }

        public int UpdateCheckoutStatusCount { get; private set; }

        public int MarkOrderCompletedCount { get; private set; }

        public ManualResetEventSlim? GetLatestEntered { get; init; }

        public ManualResetEventSlim? GetLatestRelease { get; init; }

        public CancellationToken LastMarkPaymentVerifiedCancellationToken { get; private set; }

        public CancellationToken LastMarkOrderCompletedCancellationToken { get; private set; }

        public Exception? MarkOrderCompletedException { get; init; }

        public bool FailRecoveryFinalization { get; init; }

        public LocalSquarePaymentAttempt? RecoveryFinalizationWinner { get; init; }

        public Exception? PostCommitGetAttemptException { get; init; }

        public Task CreateAsync(LocalSquarePaymentAttempt attempt, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> TryRecordRefundResponseAsync(
            Guid attemptGuid,
            string submissionToken,
            string refundId,
            string refundStatus,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task MarkCheckoutCreatedAsync(Guid attemptGuid, string checkoutId, string? checkoutStatus, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
        {
            Status = LocalSquarePaymentAttemptStatus.CheckoutCreated;
            _attempt = _attempt! with
            {
                CheckoutId = checkoutId,
                CheckoutStatus = checkoutStatus,
                Status = Status,
                UpdatedAt = updatedAt
            };
            return Task.CompletedTask;
        }

        public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
        {
            Status = LocalSquarePaymentAttemptStatus.Recovering;
            _attempt = _attempt! with { Status = Status, UpdatedAt = updatedAt };
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
            UpdateCheckoutStatusCount++;
            Status = status;
            _attempt = _attempt! with
            {
                Status = status,
                CheckoutStatus = checkoutStatus ?? _attempt.CheckoutStatus,
                CancelReason = cancelReason ?? _attempt.CancelReason,
                UpdatedAt = updatedAt
            };
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
            LastMarkPaymentVerifiedCancellationToken = cancellationToken;
            Status = LocalSquarePaymentAttemptStatus.PaymentVerified;
            _attempt = _attempt! with
            {
                Status = Status,
                PaymentId = paymentId,
                PaymentStatus = paymentStatus,
                ResponseCode = responseCode,
                ResponseText = responseText,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            };
            return Task.CompletedTask;
        }

        public Task<bool> TryPersistPaymentVerifiedRecoveryAsync(
            Guid attemptGuid,
            LocalSquarePaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            string paymentId,
            string paymentStatus,
            string? responseCode,
            string? responseText,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            if (_attempt is null ||
                _attempt.AttemptGuid != attemptGuid ||
                _attempt.Status != expectedStatus ||
                _attempt.UpdatedAt != expectedUpdatedAt ||
                string.Equals(_attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            LastMarkPaymentVerifiedCancellationToken = cancellationToken;
            Status = LocalSquarePaymentAttemptStatus.PaymentVerified;
            _attempt = _attempt with
            {
                Status = Status,
                PaymentId = paymentId,
                PaymentStatus = paymentStatus,
                ResponseCode = responseCode,
                ResponseText = responseText,
                RecoveryPhase = CardRecoveryPhases.FinalizePending,
                RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.OrderCompleted,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            };
            return Task.FromResult(true);
        }

        public Task<bool> TryMarkRefundPaymentVerifiedAsync(
            Guid attemptGuid,
            LocalSquarePaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            string submissionToken,
            string paymentId,
            string paymentStatus,
            string? responseCode,
            string? responseText,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default) =>
            TryPersistPaymentVerifiedRecoveryAsync(
                attemptGuid,
                expectedStatus,
                expectedUpdatedAt,
                paymentId,
                paymentStatus,
                responseCode,
                responseText,
                completedAt,
                cancellationToken);

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
            MarkFailedCount++;
            Status = status;
            _attempt = _attempt! with
            {
                Status = status,
                CheckoutStatus = checkoutStatus ?? _attempt.CheckoutStatus,
                PaymentStatus = paymentStatus ?? _attempt.PaymentStatus,
                ResponseCode = responseCode,
                ResponseText = responseText,
                ResolvedAt = resolvedAt,
                UpdatedAt = resolvedAt
            };
            return Task.CompletedTask;
        }

        public Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
        {
            LastMarkOrderCompletedCancellationToken = cancellationToken;
            MarkOrderCompletedCount++;
            if (MarkOrderCompletedException is not null)
            {
                throw MarkOrderCompletedException;
            }

            Status = LocalSquarePaymentAttemptStatus.OrderCompleted;
            _attempt = _attempt! with
            {
                Status = Status,
                RecoveryPhase = CardRecoveryPhases.None,
                RecoveryTargetStatus = null,
                OrderCompletedAt = completedAt,
                UpdatedAt = completedAt
            };
            return Task.CompletedTask;
        }

        public Task<bool> TryBeginRecoveryFinalizationAsync(
            Guid attemptGuid,
            LocalSquarePaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            LocalSquarePaymentAttemptStatus targetStatus,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            if (_attempt is null ||
                _attempt.AttemptGuid != attemptGuid ||
                _attempt.Status != expectedStatus ||
                _attempt.UpdatedAt != expectedUpdatedAt ||
                string.Equals(_attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _attempt = _attempt with
            {
                RecoveryPhase = CardRecoveryPhases.FinalizePending,
                RecoveryTargetStatus = targetStatus,
                UpdatedAt = updatedAt
            };
            return Task.FromResult(true);
        }

        public Task<bool> TryCompleteRecoveryFinalizationAsync(
            Guid attemptGuid,
            LocalSquarePaymentAttemptStatus expectedStatus,
            DateTimeOffset expectedUpdatedAt,
            LocalSquarePaymentAttemptStatus targetStatus,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            LastMarkOrderCompletedCancellationToken = cancellationToken;
            MarkOrderCompletedCount++;
            if (MarkOrderCompletedException is not null)
            {
                throw MarkOrderCompletedException;
            }

            if (RecoveryFinalizationWinner is not null)
            {
                _attempt = RecoveryFinalizationWinner;
                Status = RecoveryFinalizationWinner.Status;
                return Task.FromResult(false);
            }

            if (FailRecoveryFinalization ||
                _attempt is null ||
                _attempt.AttemptGuid != attemptGuid ||
                _attempt.Status != expectedStatus ||
                _attempt.UpdatedAt != expectedUpdatedAt ||
                !string.Equals(_attempt.RecoveryPhase, CardRecoveryPhases.FinalizePending, StringComparison.Ordinal) ||
                _attempt.RecoveryTargetStatus != targetStatus)
            {
                return Task.FromResult(false);
            }

            Status = targetStatus;
            _attempt = _attempt with
            {
                Status = targetStatus,
                RecoveryPhase = CardRecoveryPhases.None,
                RecoveryTargetStatus = null,
                OrderCompletedAt = targetStatus == LocalSquarePaymentAttemptStatus.OrderCompleted
                    ? completedAt
                    : _attempt.OrderCompletedAt,
                ResolvedAt = targetStatus == LocalSquarePaymentAttemptStatus.OrderCompleted
                    ? _attempt.ResolvedAt
                    : completedAt,
                UpdatedAt = completedAt
            };
            return Task.FromResult(true);
        }

        public Task<LocalSquarePaymentAttempt?> GetLatestOpenAttemptAsync(
            string storeCode,
            string deviceCode,
            string? cashierId,
            string environment,
            CancellationToken cancellationToken = default)
        {
            GetLatestEntered?.Set();
            GetLatestRelease?.Wait(cancellationToken);
            return Task.FromResult(_attempt);
        }

        public Task<IReadOnlyList<LocalSquarePaymentAttempt>> GetOpenRefundAttemptsAsync(
            string storeCode,
            string deviceCode,
            string environment,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalSquarePaymentAttempt>>(
                _attempt?.OperationKind == "Refund" ? [_attempt] : []);
        }

        public Task<LocalSquarePaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default)
        {
            if (PostCommitGetAttemptException is not null &&
                _attempt?.ResponseCode is
                    ActiveSessionSupervisorResolutionCodes.ConfirmedPaid or
                    ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid or
                    ActiveSessionSupervisorResolutionCodes.ContinueWaiting or
                    CardRefundSupervisorResolutionCodes.ConfirmedRefunded or
                    CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded or
                    CardRefundSupervisorResolutionCodes.ContinueWaiting)
            {
                throw PostCommitGetAttemptException;
            }

            return Task.FromResult(_attempt);
        }

        public Task<bool> ResolveRefundAsync(
            CardRefundAttemptResolution resolution,
            CancellationToken cancellationToken = default)
        {
            if (_attempt is null ||
                _attempt.OperationKind != "Refund" ||
                _attempt.ResponseCode is CardRefundSupervisorResolutionCodes.ConfirmedRefunded or
                    CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded)
            {
                return Task.FromResult(false);
            }

            _attempt = resolution.Decision switch
            {
                CardRefundSupervisorDecision.ConfirmRefunded => _attempt with
                {
                    Status = LocalSquarePaymentAttemptStatus.Recovering,
                    SupervisorFinancialReference = resolution.RefundReference,
                    ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedRefunded,
                    ResponseText = resolution.Reason,
                    RecoveryPhase = CardRecoveryPhases.FinalizePending,
                    RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.OrderCompleted,
                    CompletedAt = resolution.ResolvedAt,
                    ResolvedAt = resolution.ResolvedAt,
                    UpdatedAt = resolution.ResolvedAt
                },
                CardRefundSupervisorDecision.ConfirmNotRefunded => _attempt with
                {
                    Status = LocalSquarePaymentAttemptStatus.Pending,
                    CheckoutId = null,
                    CheckoutStatus = null,
                    CancelReason = null,
                    PaymentId = null,
                    PaymentStatus = null,
                    ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedNotRefunded,
                    ResponseText = resolution.Evidence,
                    RecoveryPhase = CardRecoveryPhases.FinalizePending,
                    RecoveryTargetStatus = LocalSquarePaymentAttemptStatus.Abandoned,
                    CompletedAt = null,
                    OrderCompletedAt = null,
                    ResolvedAt = null,
                    UpdatedAt = resolution.ResolvedAt
                },
                _ => _attempt with
                {
                    Status = LocalSquarePaymentAttemptStatus.Recovering,
                    ResponseCode = CardRefundSupervisorResolutionCodes.ContinueWaiting,
                    ResponseText = resolution.Reason,
                    RecoveryPhase = CardRecoveryPhases.None,
                    RecoveryTargetStatus = null,
                    UpdatedAt = resolution.ResolvedAt
                }
            };
            Status = _attempt.Status;
            return Task.FromResult(true);
        }

        public Task<bool> ResolvePaymentWithJournalAsync(
            SquarePaymentResolution resolution,
            LocalFinancialSupervisorResolution journal,
            CancellationToken cancellationToken = default)
        {
            if (_attempt is null ||
                _attempt.AttemptGuid != resolution.AttemptGuid ||
                !string.Equals(_attempt.OperationKind, "Sale", StringComparison.Ordinal) ||
                _attempt.Status != resolution.ExpectedStatus ||
                _attempt.UpdatedAt != resolution.ExpectedUpdatedAt ||
                !string.Equals(_attempt.RecoveryPhase, CardRecoveryPhases.None, StringComparison.Ordinal) ||
                _attempt.PaymentId is not null ||
                _attempt.PaymentStatus is not null)
            {
                return Task.FromResult(false);
            }

            _attempt = _attempt with
            {
                Status = resolution.Decision switch
                {
                    CardRecoverySupervisorDecision.ConfirmProcessed => LocalSquarePaymentAttemptStatus.Recovering,
                    CardRecoverySupervisorDecision.ConfirmNotProcessed => LocalSquarePaymentAttemptStatus.Pending,
                    _ => LocalSquarePaymentAttemptStatus.Recovering
                },
                PaymentId = null,
                PaymentStatus = null,
                SupervisorFinancialReference = resolution.Decision == CardRecoverySupervisorDecision.ConfirmProcessed
                    ? resolution.PaymentReference
                    : null,
                ResponseCode = resolution.Decision switch
                {
                    CardRecoverySupervisorDecision.ConfirmProcessed => ActiveSessionSupervisorResolutionCodes.ConfirmedPaid,
                    CardRecoverySupervisorDecision.ConfirmNotProcessed => ActiveSessionSupervisorResolutionCodes.ConfirmedNotPaid,
                    _ => ActiveSessionSupervisorResolutionCodes.ContinueWaiting
                },
                ResponseText = resolution.Reason,
                RecoveryPhase = resolution.Decision == CardRecoverySupervisorDecision.ContinueWaiting
                    ? CardRecoveryPhases.None
                    : CardRecoveryPhases.FinalizePending,
                RecoveryTargetStatus = resolution.Decision switch
                {
                    CardRecoverySupervisorDecision.ConfirmProcessed => LocalSquarePaymentAttemptStatus.OrderCompleted,
                    CardRecoverySupervisorDecision.ConfirmNotProcessed => LocalSquarePaymentAttemptStatus.Abandoned,
                    _ => null
                },
                CompletedAt = resolution.Decision == CardRecoverySupervisorDecision.ContinueWaiting
                    ? null
                    : resolution.ResolvedAt,
                UpdatedAt = resolution.ResolvedAt
            };
            Status = _attempt.Status;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSquareTerminalPaymentClient : ISquareTerminalPaymentClient
    {
        public SquareCheckoutStatusResult Checkout { get; set; } =
            new("CHECKOUT-001", "COMPLETED", 1000, "AUD", ["PAYMENT-001"], null);

        public SquarePaymentStatusResult Payment { get; set; } =
            new("PAYMENT-001", "COMPLETED", 1000, "AUD");

        public SquareRefundStatusResult Refund { get; set; } =
            new("REFUND-001", "COMPLETED", "PAYMENT-001", 1000, "AUD");

        public Action? OnGetPayment { get; init; }

        public int GetCheckoutCallCount { get; private set; }

        public int GetPaymentCallCount { get; private set; }

        public int GetRefundCallCount { get; private set; }

        public Task<SquareCheckoutStatusResult> GetCheckoutAsync(
            CardTerminalSettings settings,
            string checkoutId,
            CancellationToken cancellationToken = default)
        {
            GetCheckoutCallCount++;
            return Task.FromResult(Checkout);
        }

        public Task<SquarePaymentStatusResult> GetPaymentAsync(
            CardTerminalSettings settings,
            string paymentId,
            CancellationToken cancellationToken = default)
        {
            GetPaymentCallCount++;
            OnGetPayment?.Invoke();
            return Task.FromResult(Payment);
        }

        public Task<SquareRefundStatusResult> GetRefundAsync(
            CardTerminalSettings settings,
            string refundId,
            CancellationToken cancellationToken = default)
        {
            GetRefundCallCount++;
            return Task.FromResult(Refund);
        }
    }

    private sealed class FakeLinklyBackendTerminalClient : ILinklyBackendTerminalClient
    {
        public LinklyCloudBackendSessionResponse? Status { get; set; }

        public LinklyCloudBackendSessionResponse? ResumableStatus { get; set; }

        public Exception? StatusException { get; set; }

        public Action? OnGetSessionStatus { get; init; }

        public int AcknowledgeCallCount { get; private set; }

        public string? AcknowledgedSessionId { get; private set; }

        public int StatusCallCount { get; private set; }

        public string? StatusSessionId { get; private set; }

        public int ResumeCallCount { get; private set; }

        public string? ResumedSessionId { get; private set; }

        public Exception? ResumeException { get; set; }

        public Exception? AcknowledgeException { get; set; }

        public Task<LinklyConnectionTestResult> TestConnectionAsync(CardTerminalEnvironment environment, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LinklyConnectionTestResult(true, "ok"));
        }

        public Task<LinklyConnectionTestResult> TestTransactionStatusAsync(CardTerminalEnvironment environment, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LinklyConnectionTestResult(true, "status ok"));
        }

        public Task<PaymentAuthorizationResult> PurchaseAsync(decimal amount, PosSessionState session, CardTerminalSettings settings, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaymentAuthorizationResult(false));
        }

        public Task<PaymentAuthorizationResult> RefundAsync(decimal amount, PosSessionState session, CardTerminalSettings settings, string? originalReference, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaymentAuthorizationResult(false));
        }

        public Task<LinklyCloudBackendSessionResponse?> GetResumableSessionAsync(CardTerminalSettings settings, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ResumableStatus);
        }

        public Task<LinklyCloudBackendSessionResponse> RecoverSessionAsync(CardTerminalSettings settings, string sessionId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Status ?? ResumableStatus ?? throw new InvalidOperationException("Missing status."));
        }

        public Task<LinklyCloudBackendSessionResponse> ResumeSessionUntilFinalAsync(CardTerminalSettings settings, LinklyCloudBackendSessionResponse activeStatus, CancellationToken cancellationToken = default)
        {
            ResumeCallCount++;
            ResumedSessionId = activeStatus.SessionId;
            if (ResumeException is not null)
            {
                throw ResumeException;
            }

            return Task.FromResult(Status ?? ResumableStatus ?? activeStatus);
        }

        public Task<LinklyCloudBackendSessionResponse> GetSessionStatusAsync(CardTerminalSettings settings, string sessionId, CancellationToken cancellationToken = default)
        {
            StatusCallCount++;
            StatusSessionId = sessionId;
            OnGetSessionStatus?.Invoke();
            if (StatusException is not null)
            {
                throw StatusException;
            }

            return Task.FromResult(Status ?? throw new InvalidOperationException("Missing status."));
        }

        public Task AcknowledgeSessionAsync(CardTerminalSettings settings, string sessionId, CancellationToken cancellationToken = default)
        {
            AcknowledgeCallCount++;
            AcknowledgedSessionId = sessionId;
            if (AcknowledgeException is not null)
            {
                throw AcknowledgeException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeLocalOrderRepository : ILocalOrderRepository
    {
        private LocalOrder? _saved;
        private readonly LocalOrder? _existingOrder;

        public FakeLocalOrderRepository(LocalOrder? existingOrder = null)
        {
            _existingOrder = existingOrder;
        }

        public int SaveCount { get; private set; }

        public List<(LocalOrder Order, LocalHeldOrderCompletionContext Context)> HeldSources { get; } = [];

        public CancellationToken LastSaveCancellationToken { get; private set; }

        public CancellationToken LastGetOrderCancellationToken { get; private set; }

        public Action? BeforeSave { get; init; }

        public Exception? SaveException { get; init; }

        public Task SavePendingOrderAsync(LocalOrder order, CancellationToken cancellationToken = default)
        {
            LastSaveCancellationToken = cancellationToken;
            SaveCount++;
            BeforeSave?.Invoke();
            if (SaveException is not null)
            {
                throw SaveException;
            }

            _saved = order;
            return Task.CompletedTask;
        }

        public async Task SavePendingOrderWithHeldSourceAsync(
            LocalOrder order,
            LocalHeldOrderCompletionContext heldOrder,
            CancellationToken cancellationToken = default)
        {
            await SavePendingOrderAsync(order, cancellationToken);
            HeldSources.Add((order, heldOrder));
        }

        public Task UpdatePaymentReferenceAsync(
            Guid paymentGuid,
            string? reference,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(int take = 50, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);
        }

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(LocalOrderHistoryQuery query, int take = 50, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);
        }

        public Task<LocalOrder?> GetOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            LastGetOrderCancellationToken = cancellationToken;
            var existing = _existingOrder is not null && _existingOrder.OrderGuid == orderGuid
                ? _existingOrder
                : null;
            return Task.FromResult(existing ?? (_saved is not null && _saved.OrderGuid == orderGuid ? _saved : null));
        }
    }

    private sealed class FakeSyncQueueRepository : ISyncQueueRepository
    {
        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<SyncQueueOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SyncQueueOverview(0, 0, 0, null));
        }

        public Task<IReadOnlyList<SyncQueueListItem>> GetActiveItemsAsync(int take = 20, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SyncQueueListItem>>([]);
        }
    }
}
