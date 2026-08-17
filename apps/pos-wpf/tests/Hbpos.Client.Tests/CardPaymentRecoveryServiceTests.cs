using System.Text.Json;
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

        var attempt = CreateAttempt(sessionId: "SESSION-001", txnRef: "TXN-001");
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient
        {
            Status = CreateStatus("Completed", sessionId: "SESSION-001", txnRef: "TXN-001", responseCode: "00", responseText: "APPROVED", transactionSuccess: true)
        };
        var service = CreateService(attempts, orders, backend, sharedHeldOrderRepository: scope.Repository);

        // 当前 UI 购物车为空：恢复必须使用 draft 中冻结的 CartSnapshot 解析来源。
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
        var orders = new FakeLocalOrderRepository(CreateExistingOrder());
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
        Assert.Single(cart.Lines);
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
        Assert.Equal(LocalCardPaymentAttemptStatus.RequiresReview, attempts.Status);
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
        Assert.Equal(LocalCardPaymentAttemptStatus.RequiresReview, attempts.Status);
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
    public async Task RecoverLatestAsync_local_ip_approved_partial_amount_restores_tender_without_saving_order()
    {
        var draft = CreateDraft(cardAmount: 5m);
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "LOCAL-TXN-PARTIAL",
            connectionMode: LinklyConnectionMode.LocalIp,
            draft: draft,
            amount: 5m);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            true,
            "ANZ:LOCAL-TXN-PARTIAL",
            "ANZ Linkly",
            5m,
            [CreateLocalCardTransaction("LOCAL-TXN-PARTIAL", "00", "APPROVED", 5m)],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "LOCAL-TXN-PARTIAL",
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
        Assert.Equal("ANZ:LOCAL-TXN-PARTIAL", attempts.PaymentReference);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(1, localTerminal.RecoverCallCount);
        Assert.Equal(0, orders.SaveCount);
        Assert.Single(cart.Lines);
    }

    [Fact]
    public async Task RecoverLatestAsync_local_ip_get_last_declined_restores_draft_without_backend_acknowledgement()
    {
        var attempt = CreateAttempt(
            sessionId: null,
            txnRef: "LOCAL-TXN-DECLINED",
            connectionMode: LinklyConnectionMode.LocalIp);
        var attempts = new FakeCardPaymentAttemptRepository(attempt);
        var orders = new FakeLocalOrderRepository();
        var backend = new FakeLinklyBackendTerminalClient();
        var localTerminal = new FakeLinklyTerminalClient(new PaymentAuthorizationResult(
            false,
            "ANZ:LOCAL-TXN-DECLINED",
            "DECLINED",
            10m,
            [CreateLocalCardTransaction("LOCAL-TXN-DECLINED", "05", "DECLINED")],
            "ANZ",
            "Sandbox",
            LinklyConnectionMode.LocalIp.ToString(),
            "P",
            null,
            "LOCAL-TXN-DECLINED",
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
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined, attempts.Status);
        Assert.Equal("05", attempts.ResponseCode);
        Assert.Equal("DECLINED", attempts.ResponseText);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(1, localTerminal.RecoverCallCount);
        Assert.Equal("LOCAL-TXN-DECLINED", localTerminal.LastTxnRef);
        Assert.Equal(0, orders.SaveCount);
        Assert.Single(cart.Lines);
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
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined, attempts.Status);
        Assert.Equal("05", attempts.ResponseCode);
        Assert.Single(cart.Lines);
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
        var attempt = CreateAttempt(sessionId: null, txnRef: "TXN-FAILED");
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
        Assert.Equal(expectedStatus, attempts.Status);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-FAILED", backend.AcknowledgedSessionId);
        Assert.NotNull(attempts.AcknowledgedAt);
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
        var attempt = CreateAttempt(sessionId: null, txnRef: "TXN-PENDING");
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
        Assert.Equal(LocalCardPaymentAttemptStatus.Declined, attempts.Status);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-PENDING", backend.AcknowledgedSessionId);
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
        var attempt = CreateAttempt(sessionId: "SESSION-DECLINED", txnRef: "TXN-DECLINED");
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
        Assert.Equal(LocalCardPaymentAttemptStatus.Failed, attempts.Status);
        Assert.Equal(1, backend.AcknowledgeCallCount);
        Assert.Equal("SESSION-DECLINED", backend.AcknowledgedSessionId);
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
    public async Task RecoverLatestAsync_approved_with_non_empty_current_cart_defers_without_saving_or_acknowledging()
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

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Null(attempts.AcknowledgedAt);
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
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Null(attempts.AcknowledgedAt);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_verified_with_non_empty_current_cart_defers_without_saving_or_completing()
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

        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.Outcome);
        Assert.Contains("current cart already contains items", result.Message);
        Assert.Single(cart.Lines);
        Assert.Equal("CURRENT-SKU", cart.Lines[0].ProductCode);
        Assert.Equal(0, orders.SaveCount);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(0, attempts.MarkOrderCompletedCount);
        Assert.Equal(0, attempts.MarkFailedCount);
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
    public async Task ResolvePaymentAsync_confirm_not_paid_restores_draft_and_clears_backend_once()
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
        Assert.False(result.LockRetained);
        Assert.Equal(CardPaymentRecoveryOutcome.DraftRestored, result.RecoveryResult?.Outcome);
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
                "MANAGER-01"),
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.LockRetained);
        Assert.Equal(CardPaymentRecoveryOutcome.Unknown, result.RecoveryResult?.Outcome);
        Assert.Equal(0, backend.AcknowledgeCallCount);
        Assert.Equal(LocalCardPaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(string.Empty, attempts.LastPaymentJournal?.Reason);
        var journal = Assert.IsType<LocalFinancialSupervisorResolution>(attempts.LastPaymentJournal);
        using var audit = JsonDocument.Parse(journal.AuditPayloadJson);
        Assert.Equal("Succeeded", audit.RootElement.GetProperty("outcome").GetString());
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

    [Fact]
    public async Task ResolveRefundAsync_confirm_not_refunded_persists_new_linkly_txn_ref_and_allows_retry()
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
                CardRefundSupervisorDecision.ConfirmNotRefunded,
                Reason: "Checked settlement report",
                Evidence: "No refund entry for this reference"),
            new PosCartService(),
            Session);

        Assert.True(result.Succeeded);
        Assert.True(result.RetryAllowed);
        Assert.Equal(LocalCardPaymentAttemptStatus.Pending, attempts.Status);
        Assert.Null(attempts.SessionId);
        Assert.NotNull(attempts.LastRefundResolution);
        Assert.False(string.IsNullOrWhiteSpace(attempts.LastRefundResolution.RetryTxnRef));
        Assert.NotEqual("TXN-REFUND", attempts.LastRefundResolution.RetryTxnRef);
        Assert.Equal(attempts.LastRefundResolution.RetryTxnRef, attempts.TxnRef);
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
    public async Task ResolveRefundAsync_square_confirm_refunded_completes_return_without_second_terminal_call()
    {
        const string originalReference = "SQ:ORIGINAL-PAYMENT";
        const string refundReference = "SQ-REFUND-001";
        var draft = CreateRefundDraft(originalReference);
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Unknown,
            checkoutId: "CHECKOUT-REFUND",
            paymentId: "PAYMENT-ORIGINAL") with
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
    public async Task ResolveRefundAsync_square_confirm_not_refunded_preserves_idempotency_key_and_allows_retry()
    {
        var draft = CreateRefundDraft("SQ:ORIGINAL-PAYMENT");
        var attempt = CreateSquareAttempt(
            LocalSquarePaymentAttemptStatus.Unknown,
            checkoutId: "CHECKOUT-REFUND",
            paymentId: "PAYMENT-ORIGINAL") with
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
        Assert.Equal(LocalSquarePaymentAttemptStatus.Pending, attempts.Status);
        Assert.Equal(attempt.IdempotencyKey, attempts.IdempotencyKey);
        Assert.Null(attempts.CheckoutId);
        Assert.Null(attempts.PaymentId);
        Assert.Single(cart.Lines);
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
        ISharedHeldOrderRepository? sharedHeldOrderRepository = null)
    {
        return new CardPaymentRecoveryService(
            attempts,
            settingsProvider ?? new FakeCardTerminalSettingsProvider(),
            backend,
            new CashCheckoutService(),
            orders,
            new FakeSyncQueueRepository(),
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
    private static LocalOrder CreateExistingOrder()
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
            []);
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

        public string? LastCashierId { get; private set; }

        public ManualResetEventSlim? GetLatestEntered { get; init; }

        public ManualResetEventSlim? GetLatestRelease { get; init; }

        public CancellationToken LastUpdateOutcomeCancellationToken { get; private set; }

        public CancellationToken LastMarkOrderCompletedCancellationToken { get; private set; }

        public Exception? MarkOrderCompletedException { get; init; }

        public Exception? UpdateOutcomeException { get; init; }

        public int ResolveRefundCount { get; private set; }

        public CardRefundAttemptResolution? LastRefundResolution { get; private set; }

        public int CreateActiveSessionCount { get; private set; }

        public string? OperationKind => _attempt?.OperationKind;

        public LocalFinancialSupervisorResolution? LastPaymentJournal { get; private set; }

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

        public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
        {
            _attempt = _attempt! with
            {
                Status = LocalCardPaymentAttemptStatus.Recovering,
                UpdatedAt = updatedAt
            };
            return Task.CompletedTask;
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

            LastPaymentJournal = journal;
            _attempt = _attempt with
            {
                Status = resolution.Decision switch
                {
                    ActiveSessionSupervisorDecision.ConfirmPaid => LocalCardPaymentAttemptStatus.Approved,
                    ActiveSessionSupervisorDecision.ConfirmNotPaid => LocalCardPaymentAttemptStatus.Cancelled,
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
                UpdatedAt = resolution.ResolvedAt
            };
            return Task.FromResult(true);
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
                    UpdatedAt = resolution.ResolvedAt
                },
                _ => _attempt with
                {
                    Status = LocalCardPaymentAttemptStatus.Recovering,
                    ResponseCode = CardRefundSupervisorResolutionCodes.ContinueWaiting,
                    ResponseText = resolution.Reason,
                    UpdatedAt = resolution.ResolvedAt
                }
            };
            return Task.FromResult(true);
        }

        public Task<LocalCardPaymentAttempt?> GetAttemptAsync(Guid attemptGuid, CancellationToken cancellationToken = default)
        {
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

        public int MarkFailedCount { get; private set; }

        public int UpdateCheckoutStatusCount { get; private set; }

        public int MarkOrderCompletedCount { get; private set; }

        public ManualResetEventSlim? GetLatestEntered { get; init; }

        public ManualResetEventSlim? GetLatestRelease { get; init; }

        public CancellationToken LastMarkPaymentVerifiedCancellationToken { get; private set; }

        public CancellationToken LastMarkOrderCompletedCancellationToken { get; private set; }

        public Exception? MarkOrderCompletedException { get; init; }

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
            return Task.CompletedTask;
        }

        public Task MarkRecoveringAsync(Guid attemptGuid, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
        {
            Status = LocalSquarePaymentAttemptStatus.Recovering;
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
            MarkFailedCount++;
            Status = status;
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
            return Task.CompletedTask;
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
                    Status = LocalSquarePaymentAttemptStatus.PaymentVerified,
                    PaymentId = resolution.RefundReference,
                    PaymentStatus = CardRefundSupervisorResolutionCodes.ConfirmedRefunded,
                    ResponseCode = CardRefundSupervisorResolutionCodes.ConfirmedRefunded,
                    ResponseText = resolution.Reason,
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
                    UpdatedAt = resolution.ResolvedAt
                }
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
