using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using static Hbpos.Client.Tests.SharedHeldOrderClientTestSupport;

namespace Hbpos.Client.Tests;

/// <summary>
/// 取单完成来源解析器：显式 cart binding 是唯一来源身份；召回后合法编辑仍须绑定
/// 原 claim。Prepared、已绑定、缺失或重复 durable facts 一律抛错阻断，不能降级普通订单。
/// </summary>
public sealed class SharedHeldOrderPaymentSourceResolverTests
{
    private static readonly ISharedHeldOrderReverseMapper Mapper = new SharedHeldOrderReverseMapper();

    [Fact]
    public async Task TryResolveAsync_exact_active_unbound_match_returns_completion_context()
    {
        var claimId = Guid.NewGuid();
        var holdGuid = Guid.NewGuid();
        var resolver = CreateResolver([
            Recovery(claimId, holdGuid, SampleCanonical())
        ]);

        var context = await resolver.TryResolveAsync(Session(), CartFrom(SampleCanonical(), claimId));

        Assert.NotNull(context);
        Assert.Equal(holdGuid, context!.HoldGuid);
        Assert.Equal(claimId, context.ClaimId);
        Assert.Equal(SharedHeldOrderClaimSource.RemoteClaim, context.Source);
        Assert.Equal("prepare-1", context.PrepareIdempotencyKey);
        Assert.Equal("activate-1", context.ActivateIdempotencyKey);
        Assert.Null(context.BoundOrderGuid);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", context.CompletedAtIso);
    }

    [Fact]
    public async Task TryResolveAsync_identical_rebuilt_cart_without_explicit_claim_binding_returns_null()
    {
        var resolver = CreateResolver([
            Recovery(Guid.NewGuid(), Guid.NewGuid(), SampleCanonical())
        ]);

        var context = await resolver.TryResolveAsync(Session(), CartFrom(SampleCanonical()));

        Assert.Null(context);
    }

    [Fact]
    public async Task TryResolveAsync_wrong_explicit_claim_binding_fails_closed()
    {
        var claimId = Guid.NewGuid();
        var resolver = CreateResolver([
            Recovery(claimId, Guid.NewGuid(), SampleCanonical())
        ]);

        await Assert.ThrowsAsync<InvalidDataException>(() => resolver.TryResolveAsync(
            Session(),
            CartFrom(SampleCanonical(), Guid.NewGuid())));
    }

    [Fact]
    public async Task TryResolveAsync_edited_quantity_with_same_binding_returns_completion_context()
    {
        var claimId = Guid.NewGuid();
        var resolver = CreateResolver([
            Recovery(claimId, Guid.NewGuid(), SampleCanonical(quantity: 2m))
        ]);

        var context = await resolver.TryResolveAsync(
            Session(),
            CartFrom(SampleCanonical(quantity: 1m), claimId));

        Assert.Equal(claimId, context!.ClaimId);
    }

    [Fact]
    public async Task TryResolveAsync_edited_unit_price_with_same_binding_returns_completion_context()
    {
        var claimId = Guid.NewGuid();
        var resolver = CreateResolver([
            Recovery(claimId, Guid.NewGuid(), SampleCanonical(unitPriceCents: 1200))
        ]);

        var context = await resolver.TryResolveAsync(
            Session(),
            CartFrom(SampleCanonical(unitPriceCents: 1100), claimId));

        Assert.Equal(claimId, context!.ClaimId);
    }

    [Fact]
    public async Task TryResolveAsync_prepared_claim_never_binds_even_with_exact_match()
    {
        var claimId = Guid.NewGuid();
        var resolver = CreateResolver([
            Recovery(
                claimId,
                Guid.NewGuid(),
                SampleCanonical(),
                status: SharedHeldOrderClaimStatus.Prepared)
        ]);

        await Assert.ThrowsAsync<InvalidDataException>(() => resolver.TryResolveAsync(
            Session(),
            CartFrom(SampleCanonical(), claimId)));
    }

    [Fact]
    public async Task TryResolveAsync_bound_active_claim_never_binds_even_with_exact_match()
    {
        var claimId = Guid.NewGuid();
        var resolver = CreateResolver([
            Recovery(
                claimId,
                Guid.NewGuid(),
                SampleCanonical(),
                boundOrderGuid: "11111111-2222-3333-4444-555555555555")
        ]);

        await Assert.ThrowsAsync<InvalidDataException>(() => resolver.TryResolveAsync(
            Session(),
            CartFrom(SampleCanonical(), claimId)));
    }

    [Fact]
    public async Task TryResolveAsync_duplicate_records_for_bound_claim_fail_closed()
    {
        var claimId = Guid.NewGuid();
        var resolver = CreateResolver([
            Recovery(claimId, Guid.NewGuid(), SampleCanonical()),
            Recovery(claimId, Guid.NewGuid(), SampleCanonical())
        ]);

        await Assert.ThrowsAsync<InvalidDataException>(() => resolver.TryResolveAsync(
            Session(),
            CartFrom(SampleCanonical(), claimId)));
    }

    [Fact]
    public async Task TryResolveAsync_explicit_active_binding_does_not_reparse_original_payload()
    {
        var corruptClaimId = Guid.NewGuid();
        var corrupt = SampleCanonical() with
        {
            PricingState = SampleCanonical().PricingState with
            {
                Lines =
                [
                    SampleCanonical().PricingState.Lines[0] with { Kind = "open-item" }
                ]
            }
        };
        var resolver = CreateResolver([
            Recovery(corruptClaimId, Guid.NewGuid(), corrupt),
            Recovery(Guid.NewGuid(), Guid.NewGuid(), SampleCanonical())
        ]);

        var context = await resolver.TryResolveAsync(
            Session(),
            CartFrom(SampleCanonical(), corruptClaimId));

        Assert.Equal(corruptClaimId, context!.ClaimId);
    }

    [Fact]
    public async Task TryResolveAsync_edited_discount_with_same_binding_returns_completion_context()
    {
        var claimId = Guid.NewGuid();
        var claimPayload = SampleCanonical(
            discountMode: SharedHeldOrderCanonicalConstants.DiscountManualAmount,
            discountCents: 500);
        var resolver = CreateResolver([
            Recovery(claimId, Guid.NewGuid(), claimPayload)
        ]);

        var context = await resolver.TryResolveAsync(
            Session(),
            CartFrom(SampleCanonical(), claimId));

        Assert.Equal(claimId, context!.ClaimId);
    }

    private static ISharedHeldOrderPaymentSourceResolver CreateResolver(
        IReadOnlyList<SharedHeldOrderClaimRecovery> claims)
    {
        return new SharedHeldOrderPaymentSourceResolver(
            new StubSharedHeldOrderRepository(claims),
            Mapper);
    }

    private static PosCartSnapshot CartFrom(
        SharedHeldOrderCanonicalPayload payload,
        Guid? claimId = null)
    {
        return Mapper.Map(payload, "S001") with { SharedHeldOrderClaimId = claimId };
    }

    private static SharedHeldOrderClaimRecovery Recovery(
        Guid claimId,
        Guid holdGuid,
        SharedHeldOrderCanonicalPayload payload,
        SharedHeldOrderClaimStatus status = SharedHeldOrderClaimStatus.Active,
        string? boundOrderGuid = null)
    {
        return new SharedHeldOrderClaimRecovery(
            claimId,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            status,
            "prepare-1",
            status == SharedHeldOrderClaimStatus.Prepared ? null : "activate-1",
            null,
            payload,
            null,
            null,
            boundOrderGuid,
            null,
            "2026-07-28T00:00:00.000Z",
            "2026-07-28T00:00:01.000Z");
    }

    /// <summary>只提供 FindRecoverableClaimsAsync，其余接口成员测试不会触达。</summary>
    private sealed class StubSharedHeldOrderRepository(IReadOnlyList<SharedHeldOrderClaimRecovery> claims)
        : ISharedHeldOrderRepository
    {
        public Task<IReadOnlyList<SharedHeldOrderClaimRecovery>> FindRecoverableClaimsAsync(
            string storeCode,
            string deviceCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(claims);
        }

        public Task<SharedHeldOrderShareRequestResult> TryRequestShareAsync(
            Guid holdGuid,
            string storeCode,
            string deviceCode,
            string requestedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<SharedHeldOrderShareRequestResult>(new NotSupportedException());
        }

        public Task<bool> TryExpirePreparedRemoteClaimAsync(
            Guid claimId,
            string releaseIdempotencyKey,
            string nowIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<IReadOnlyList<SuspendedOrder>> ListLegacyOrdersNeedingEvaluationAsync(
            string storeCode,
            string? deviceCode = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IReadOnlyList<SuspendedOrder>>(new NotSupportedException());
        }

        public Task<bool> UpsertPublicationAsync(
            Guid localHoldGuid,
            string storeCode,
            string deviceCode,
            SharedHeldOrderPublicationStatus status,
            byte[]? payloadCiphertext,
            string heldAtIso,
            string createdAtIso,
            string updatedAtIso,
            string? errorCode = null,
            string? errorMessage = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<IReadOnlyList<SharedHeldOrderPublication>> ListDuePublicationsAsync(
            string nowIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IReadOnlyList<SharedHeldOrderPublication>>(new NotSupportedException());
        }

        public Task<bool> TryAdvancePublicationAsync(
            Guid localHoldGuid,
            SharedHeldOrderPublicationStatus expectedStatus,
            int expectedRevision,
            SharedHeldOrderPublicationStatus newStatus,
            string updatedAtIso,
            string? errorCode = null,
            string? errorMessage = null,
            string? lastAttemptAtIso = null,
            string? nextAttemptAtIso = null,
            long? remoteRevision = null,
            string? remoteUpdatedAtIso = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<SharedHeldOrderPublication?> GetPublicationAsync(
            Guid localHoldGuid,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<SharedHeldOrderPublication?>(new NotSupportedException());
        }

        public Task<SharedHeldOrderDeleteStage?> TryStageDeletePendingAsync(
            Guid holdGuid,
            string storeCode,
            string deviceCode,
            string updatedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<SharedHeldOrderDeleteStage?>(new NotSupportedException());
        }

        public Task<bool> TryCompleteDeletePendingAsync(
            Guid holdGuid,
            string storeCode,
            string deviceCode,
            string completedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<bool> TryStagePendingPublishAsync(
            Guid localHoldGuid,
            int expectedRevision,
            SharedHeldOrderCanonicalPayload payload,
            string updatedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<bool> TryBlockPublicationAsync(
            Guid localHoldGuid,
            int expectedRevision,
            string errorCode,
            string errorMessage,
            string updatedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<SharedHeldOrderCanonicalPayload?> GetPublicationPayloadAsync(
            Guid localHoldGuid,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<SharedHeldOrderCanonicalPayload?>(new NotSupportedException());
        }

        public Task<bool> TrySavePreparedClaimAsync(
            SharedHeldOrderClaimDraft draft,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<bool> TryActivateClaimAsync(
            Guid claimId,
            string prepareIdempotencyKey,
            string activateIdempotencyKey,
            long? serverRevision,
            string updatedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<bool> TryBindOrderAsync(
            Guid claimId,
            string activateIdempotencyKey,
            string boundOrderGuid,
            string updatedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<bool> TryCompleteClaimAsync(
            Guid claimId,
            string activateIdempotencyKey,
            string releaseIdempotencyKey,
            string updatedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<bool> TryReleaseClaimAsync(
            Guid claimId,
            string releaseIdempotencyKey,
            SharedHeldOrderClaimStatus expectedStatus,
            string updatedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<bool> TryForceReleaseClaimAsync(
            Guid claimId,
            string releaseIdempotencyKey,
            SharedHeldOrderClaimStatus expectedStatus,
            string updatedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<bool> TrySupersedeClaimAsync(
            Guid claimId,
            string supersedeIdempotencyKey,
            SharedHeldOrderClaimStatus expectedStatus,
            string updatedAtIso,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<bool>(new NotSupportedException());
        }

        public Task<SharedHeldOrderClaimRecord?> GetClaimAsync(
            Guid claimId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<SharedHeldOrderClaimRecord?>(new NotSupportedException());
        }
    }
}
