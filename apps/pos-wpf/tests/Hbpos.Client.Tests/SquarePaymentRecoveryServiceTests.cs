using System.Text.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;
using static Hbpos.Client.Tests.SharedHeldOrderClientTestSupport;

namespace Hbpos.Client.Tests;

/// <summary>
/// Square 批准恢复完成路径：必须用 payment draft 冻结的 durable claim binding 解析取单来源；
/// 当前 UI 购物车内容绝不参与来源匹配。
/// </summary>
public sealed class SquarePaymentRecoveryServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PosSessionState Session = new("HB POS", "S001", "Main Branch", "POS-01", "C001", "Alice", true, 0);

    [Fact]
    public async Task RecoverLatestAsync_square_verified_binds_matching_active_claim_from_frozen_draft_snapshot()
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
            "prepare-sq",
            CanonicalForDraftCart(),
            "2026-07-28T00:00:00.000Z")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimId, "prepare-sq", "activate-sq", serverRevision: null, "2026-07-28T00:00:01.000Z"));

        var draft = CreateDraft();
        var attempts = new FakeSquarePaymentAttemptRepository(
            CreateSquareAttempt(LocalSquarePaymentAttemptStatus.CheckoutCreated, "CHECKOUT-001") with
            {
                OrderDraftJson = JsonSerializer.Serialize(
                    draft with
                    {
                        CartSnapshot = draft.CartSnapshot with { SharedHeldOrderClaimId = claimId }
                    },
                    JsonOptions)
            });
        var orders = new FakeLocalOrderRepository();
        var service = CreateService(attempts, orders, new FakeSquareTerminalPaymentClient(), scope.Repository);

        // 当前 UI 购物车为空：Square 批准恢复必须使用 draft 中冻结的 durable claim binding 解析来源。
        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        var heldCompletion = Assert.Single(orders.HeldSources);
        Assert.Equal(result.Order!.OrderGuid, heldCompletion.Order.OrderGuid);
        Assert.Equal(holdGuid, heldCompletion.Context.HoldGuid);
        Assert.Equal(claimId, heldCompletion.Context.ClaimId);
        Assert.Equal(SharedHeldOrderClaimSource.OfflineOrigin, heldCompletion.Context.Source);
        Assert.Equal("prepare-sq", heldCompletion.Context.PrepareIdempotencyKey);
        Assert.Equal("activate-sq", heldCompletion.Context.ActivateIdempotencyKey);
        Assert.Equal(1, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_verified_with_non_matching_claim_does_not_bind_held_source()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var claimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            claimId,
            Guid.NewGuid(),
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.OfflineOrigin,
            "prepare-sq-nomatch",
            CanonicalForDraftCart(quantity: 2m),
            "2026-07-28T00:00:00.000Z")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimId, "prepare-sq-nomatch", "activate-sq-nomatch", serverRevision: null, "2026-07-28T00:00:01.000Z"));

        var attempts = new FakeSquarePaymentAttemptRepository(
            CreateSquareAttempt(LocalSquarePaymentAttemptStatus.CheckoutCreated, "CHECKOUT-NOMATCH"));
        var orders = new FakeLocalOrderRepository();
        var service = CreateService(attempts, orders, new FakeSquareTerminalPaymentClient(), scope.Repository);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(1, orders.SaveCount);
        Assert.Empty(orders.HeldSources);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_verified_with_existing_order_skips_bound_claim_resolution()
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
            "prepare-sq-existing",
            CanonicalForDraftCart(),
            "2026-07-28T00:00:00.000Z")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimId,
            "prepare-sq-existing",
            "activate-sq-existing",
            serverRevision: null,
            "2026-07-28T00:00:01.000Z"));
        // 订单已保存、attempt 未收尾：claim 已绑定并完成（与 LocalOrder 同事务）。
        Assert.True(await scope.Repository.TryBindOrderAsync(
            claimId,
            "activate-sq-existing",
            CreateDraftOrderGuid().ToString("D"),
            "2026-07-28T00:00:02.000Z"));
        Assert.True(await scope.Repository.TryCompleteClaimAsync(
            claimId,
            "activate-sq-existing",
            "release-sq-existing",
            "2026-07-28T00:00:03.000Z"));

        var attempts = new FakeSquarePaymentAttemptRepository(
            CreateSquareAttempt(LocalSquarePaymentAttemptStatus.CheckoutCreated, "CHECKOUT-EXISTING"));
        var orders = new FakeLocalOrderRepository(CreateExistingOrder());
        var service = CreateService(attempts, orders, new FakeSquareTerminalPaymentClient(), scope.Repository);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        // 既有订单幂等收尾：不再解析已 Completed/bound 的 held claim，直接完成 attempt，
        // 不重复保存订单，也绝不把来源静默降级为普通订单。
        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(CreateDraftOrderGuid(), result.Order!.OrderGuid);
        Assert.Equal(0, orders.SaveCount);
        Assert.Empty(orders.HeldSources);
        Assert.Equal(1, attempts.MarkOrderCompletedCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_pending_refund_stays_locked_without_creating_order()
    {
        var attempts = new FakeSquarePaymentAttemptRepository(CreateSquareRefundAttempt());
        var orders = new FakeLocalOrderRepository();
        var terminal = new FakeSquareTerminalPaymentClient
        {
            Refund = new SquareRefundStatusResult(
                "REFUND-001",
                "PENDING",
                "PAYMENT-001",
                1000,
                "AUD")
        };
        var service = CreateService(attempts, orders, terminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.Checking, result.Outcome);
        Assert.Equal(LocalSquarePaymentAttemptStatus.Recovering, attempts.Status);
        Assert.Equal(0, attempts.MarkOrderCompletedCount);
        Assert.Equal(0, orders.SaveCount);
    }

    [Fact]
    public async Task RecoverLatestAsync_square_completed_refund_creates_return_without_second_refund()
    {
        var attempts = new FakeSquarePaymentAttemptRepository(CreateSquareRefundAttempt());
        var orders = new FakeLocalOrderRepository();
        var terminal = new FakeSquareTerminalPaymentClient
        {
            Refund = new SquareRefundStatusResult(
                "REFUND-001",
                "COMPLETED",
                "PAYMENT-001",
                1000,
                "AUD")
        };
        var service = CreateService(attempts, orders, terminal);

        var result = await service.RecoverLatestAsync(new PosCartService(), Session);

        Assert.Equal(CardPaymentRecoveryOutcome.OrderCompleted, result.Outcome);
        Assert.Equal(1, terminal.GetRefundCallCount);
        Assert.Equal(1, attempts.MarkOrderCompletedCount);
        Assert.Equal(1, orders.SaveCount);
        Assert.Equal(-10m, Assert.Single(result.Order!.Payments).Amount);
    }

    private static SquarePaymentRecoveryService CreateService(
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

    private static LocalSquarePaymentAttempt CreateSquareAttempt(
        LocalSquarePaymentAttemptStatus status,
        string? checkoutId)
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
            null,
            null,
            null,
            null,
            now.AddMinutes(-2),
            now.AddMinutes(-1),
            null,
            null,
            null);
    }

    private static LocalSquarePaymentAttempt CreateSquareRefundAttempt()
    {
        var draft = CreateRefundDraft("SQ:PAYMENT-001");
        return CreateSquareAttempt(LocalSquarePaymentAttemptStatus.Recovering, checkoutId: null) with
        {
            OperationKind = "Refund",
            OperationGuid = draft.OrderGuid,
            OrderDraftJson = JsonSerializer.Serialize(draft, JsonOptions),
            PaymentId = "REFUND-001",
            PaymentStatus = "PENDING",
            SubmissionToken = "refund-submission-001"
        };
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
            CreateDraftOrderGuid(),
            Session,
            cart.CreateSnapshot(),
            [],
            cart.ActualAmount,
            10m,
            "R",
            originalReference,
            DateTimeOffset.Parse("2026-06-05T10:00:00+10:00"));
    }

    private static CardPaymentOrderDraft CreateDraft()
    {
        return new CardPaymentOrderDraft(
            CreateDraftOrderGuid(),
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
                    10m,
                    0m,
                    null,
                    PriceSourceKind.StoreRetailPrice,
                    "Store price")
            ]),
            [],
            10m,
            10m,
            "P",
            null,
            DateTimeOffset.Parse("2026-06-05T10:00:00+10:00"));
    }

    private static Guid CreateDraftOrderGuid()
    {
        return Guid.Parse("11111111-2222-3333-4444-555555555555");
    }

    /// <summary>订单已保存（与 claim 同事务绑定完成）、attempt 未收尾的既有订单。</summary>
    private static LocalOrder CreateExistingOrder()
    {
        return new LocalOrder(
            CreateDraftOrderGuid(),
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
                    "SQ:PAYMENT-EXISTING",
                    IdempotencyKey: "SQUARE_ATTEMPT:bbbbbbbbccccddddeeeeffffffffffff")
            ]);
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

    private sealed class FakeSquarePaymentAttemptRepository(LocalSquarePaymentAttempt? attempt)
        : ILocalSquarePaymentAttemptRepository
    {
        private LocalSquarePaymentAttempt? _attempt = attempt;

        public LocalSquarePaymentAttemptStatus Status { get; private set; } = attempt?.Status ?? LocalSquarePaymentAttemptStatus.Failed;

        public int MarkOrderCompletedCount { get; private set; }

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
            if (_attempt is not null)
            {
                _attempt = _attempt with
                {
                    Status = LocalSquarePaymentAttemptStatus.Recovering,
                    PaymentId = refundId,
                    PaymentStatus = refundStatus,
                    UpdatedAt = updatedAt
                };
                Status = _attempt.Status;
            }

            return Task.FromResult(true);
        }

        public Task MarkCheckoutCreatedAsync(
            Guid attemptGuid,
            string checkoutId,
            string? checkoutStatus,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
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
            Status = status;
            return Task.CompletedTask;
        }

        public Task MarkOrderCompletedAsync(Guid attemptGuid, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
        {
            MarkOrderCompletedCount++;
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
    }

    private sealed class FakeSquareTerminalPaymentClient : ISquareTerminalPaymentClient
    {
        public SquareCheckoutStatusResult Checkout { get; set; } =
            new("CHECKOUT-001", "COMPLETED", 1000, "AUD", ["PAYMENT-001"], null);

        public SquarePaymentStatusResult Payment { get; set; } =
            new("PAYMENT-001", "COMPLETED", 1000, "AUD");

        public SquareRefundStatusResult Refund { get; set; } =
            new("REFUND-001", "COMPLETED", "PAYMENT-001", 1000, "AUD");

        public int GetRefundCallCount { get; private set; }

        public Task<SquareCheckoutStatusResult> GetCheckoutAsync(
            CardTerminalSettings settings,
            string checkoutId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Checkout);
        }

        public Task<SquarePaymentStatusResult> GetPaymentAsync(
            CardTerminalSettings settings,
            string paymentId,
            CancellationToken cancellationToken = default)
        {
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

        public Task SavePendingOrderAsync(LocalOrder order, CancellationToken cancellationToken = default)
        {
            SaveCount++;
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

        public Task<IReadOnlyList<LocalOrderSummary>> GetRecentOrdersAsync(
            LocalOrderHistoryQuery query,
            int take = 50,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LocalOrderSummary>>([]);
        }

        public Task<LocalOrder?> GetOrderAsync(Guid orderGuid, CancellationToken cancellationToken = default)
        {
            var existing = _existingOrder is not null && _existingOrder.OrderGuid == orderGuid
                ? _existingOrder
                : null;
            return Task.FromResult(existing ?? (_saved is not null && _saved.OrderGuid == orderGuid ? _saved : null));
        }
    }
}
