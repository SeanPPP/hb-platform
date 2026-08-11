using System.Net;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.HeldOrders;
using LocalClaimStatus = Hbpos.Client.Wpf.Models.SharedHeldOrderClaimStatus;
using ServerClaimStatus = Hbpos.Contracts.HeldOrders.SharedHeldOrderClaimStatus;
using static Hbpos.Client.Tests.SharedHeldOrderClientTestSupport;

namespace Hbpos.Client.Tests;

/// <summary>
/// 取单协调器：固定顺序 server prepare -> 本地 durable fence -> server activate ->
/// 本地 Active -> 反向映射恢复购物车；本地 durable 写失败绝不 activate；
/// cart restore 失败清空 cart 但保留 Active、绝不自动 release；
/// 离线 recall 不访问 API；claims/mine 对账同 facts 一致才恢复，mismatch fail-closed。
/// </summary>
public sealed class SharedHeldOrderCoordinatorTests
{
    [Fact]
    public async Task TakeRemoteHoldAsync_follows_prepare_durable_activate_restore_order()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var calls = new List<string>();
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = (actualHoldGuid, request, _) =>
            {
                calls.Add("prepare");
                Assert.Equal(holdGuid, actualHoldGuid);
                Assert.Equal(claimGuid, request.ClaimGuid);
                return Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
                    holdGuid,
                    claimGuid,
                    ServerClaimStatus.Prepared,
                    SampleSaleCartV1(quantity: 1.5m, unitPriceCents: 1999),
                    "POS-01",
                    "cashier-1",
                    "Cashier One",
                    new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
                    new DateTimeOffset(2026, 7, 28, 1, 4, 3, TimeSpan.Zero),
                    Revision: 3L));
            },
            Activate = (actualHoldGuid, actualClaimGuid, _) =>
            {
                calls.Add("activate");
                Assert.Equal(holdGuid, actualHoldGuid);
                Assert.Equal(claimGuid, actualClaimGuid);
                return Task.FromResult(ActiveClaimDto(holdGuid, claimGuid, Revision: 4L));
            },
            Release = (_, _, _) =>
            {
                calls.Add("release");
                throw new InvalidOperationException("release must never be called automatically");
            }
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.TakeRemoteHoldAsync(holdGuid, session, claimGuid);

        Assert.Equal(new[] { "prepare", "activate" }, calls);
        Assert.Equal(claimGuid, result.ClaimId);
        Assert.True(result.RestoredToCart);
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.NotNull(claim);
        Assert.Equal(LocalClaimStatus.Active, claim!.Status);
        Assert.Equal(4L, claim.ServerRevision);
        Assert.Equal(SharedHeldOrderClaimSource.RemoteClaim, claim.Source);
        Assert.Equal(holdGuid, claim.HoldGuid);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(1.5m, line.Quantity);
        Assert.Equal(19.99m, line.UnitPrice);
        Assert.Equal(claimGuid, cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public async Task Concurrent_take_rejects_second_operation_before_server_prepare()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var firstPrepareStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstPrepare = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepareCount = 0;
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = async (holdGuid, request, cancellationToken) =>
            {
                var call = Interlocked.Increment(ref prepareCount);
                if (call == 1)
                {
                    firstPrepareStarted.TrySetResult();
                    await allowFirstPrepare.Task.WaitAsync(cancellationToken);
                }

                return new SharedHeldOrderClaimPrepareResponse(
                    holdGuid,
                    request.ClaimGuid,
                    ServerClaimStatus.Prepared,
                    SampleSaleCartV1(),
                    "POS-01",
                    "cashier-1",
                    "Cashier One",
                    new DateTimeOffset(2026, 7, 28, 1, 6, 1, TimeSpan.Zero),
                    new DateTimeOffset(2026, 7, 28, 1, 8, 1, TimeSpan.Zero),
                    Revision: 1L);
            },
            Activate = (holdGuid, claimGuid, _) => Task.FromResult(
                ActiveClaimDto(holdGuid, claimGuid, Revision: 2L))
        };
        var coordinator = CreateCoordinator(scope, api, cart);
        var firstClaimGuid = Guid.NewGuid();
        var first = coordinator.TakeRemoteHoldAsync(Guid.NewGuid(), session, firstClaimGuid);
        await firstPrepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            var exception = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
                () => coordinator.TakeRemoteHoldAsync(Guid.NewGuid(), session, Guid.NewGuid()));
            Assert.Equal("FENCE_CONFLICT", exception.Code);
            Assert.Equal(1, Volatile.Read(ref prepareCount));
        }
        finally
        {
            allowFirstPrepare.TrySetResult();
        }

        var result = await first.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(firstClaimGuid, result.ClaimId);
        Assert.Equal(1, Volatile.Read(ref prepareCount));
        Assert.Equal(firstClaimGuid, cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_rejects_non_empty_cart_before_any_api_call()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        cart.AddItem(TestCatalog.SellableItem("P-1", "CODE-1", 10m));
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = (_, _, _) => throw new InvalidOperationException("prepare must not run")
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.TakeRemoteHoldAsync(Guid.NewGuid(), session));

        Assert.Empty(await scope.Repository.FindRecoverableClaimsAsync("S001", "POS-01"));
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_terminal_prepare_never_saves_local_claim()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = (_, _, _) => Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
                holdGuid,
                claimGuid,
                ServerClaimStatus.Released,
                SampleSaleCartV1(),
                "POS-01",
                "cashier-1",
                "Cashier One",
                new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
                null,
                Revision: 2L)),
            Activate = (_, _, _) => throw new InvalidOperationException("activate must not run")
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var exception = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.TakeRemoteHoldAsync(holdGuid, session, claimGuid));

        Assert.Equal("CONFLICT", exception.Code);
        Assert.Null(await scope.Repository.GetClaimAsync(claimGuid));
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_prepare_response_identity_mismatch_fails_before_durable_fence()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var requestedHoldGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var activateCalls = 0;
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = (_, _, _) => Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
                Guid.NewGuid(),
                claimGuid,
                ServerClaimStatus.Prepared,
                SampleSaleCartV1(),
                "OTHER-DEVICE",
                session.CashierId,
                session.CashierName,
                new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
                null,
                Revision: 1L)),
            Activate = (_, _, _) =>
            {
                activateCalls++;
                throw new InvalidOperationException("activate must not run");
            }
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var exception = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.TakeRemoteHoldAsync(requestedHoldGuid, session, claimGuid));

        Assert.Equal("INVALID", exception.Code);
        Assert.Equal(0, activateCalls);
        Assert.Null(await scope.Repository.GetClaimAsync(claimGuid));
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_durable_fence_failure_never_activates()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var firstClaimGuid = Guid.NewGuid();
        var secondClaimGuid = Guid.NewGuid();
        var activateCalls = 0;
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = (_, request, _) => Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
                holdGuid,
                request.ClaimGuid,
                ServerClaimStatus.Prepared,
                SampleSaleCartV1(),
                "POS-01",
                "cashier-1",
                "Cashier One",
                new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 28, 1, 4, 3, TimeSpan.Zero),
                Revision: 1L)),
            Activate = (_, _, _) =>
            {
                activateCalls++;
                throw new InvalidOperationException("activate must not run when durable fence fails");
            }
        };
        var coordinator = CreateCoordinator(scope, api, cart);
        // 先占满本机 open fence，使第二个 claim 的 durable 写入失败。
        // 注意：fence 必须是未过期的有效 claim（ExpiresAt 晚于当前时钟），
        // 过期 Prepared fence 会被目标 1 在下次 prepare 前幂等推进 Released，不再阻塞。
        var firstDraft = new SharedHeldOrderClaimDraft(
            firstClaimGuid,
            holdGuid,
            session.StoreCode,
            session.DeviceCode,
            SharedHeldOrderClaimSource.RemoteClaim,
            "prepare-first",
            SampleCanonical(),
            "2026-07-28T01:02:00.000Z",
            "2026-07-28T01:10:00.000Z");
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(firstDraft));

        var exception = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.TakeRemoteHoldAsync(holdGuid, session, secondClaimGuid));

        Assert.Equal("FENCE_CONFLICT", exception.Code);
        Assert.Equal(0, activateCalls);
        Assert.Equal(
            LocalClaimStatus.Prepared,
            (await scope.Repository.GetClaimAsync(firstClaimGuid))!.Status);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_activate_unknown_keeps_local_prepared_and_does_not_restore()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = (_, _, _) => Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
                holdGuid,
                claimGuid,
                ServerClaimStatus.Prepared,
                SampleSaleCartV1(),
                "POS-01",
                "cashier-1",
                "Cashier One",
                new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 28, 1, 4, 3, TimeSpan.Zero),
                Revision: 1L)),
            Activate = (_, _, _) => throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Retryable,
                "activate result unknown",
                null,
                HttpStatusCode.GatewayTimeout),
            Release = (_, _, _) => throw new InvalidOperationException("release must never run")
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var exception = await Assert.ThrowsAsync<SharedHeldOrderApiException>(
            () => coordinator.TakeRemoteHoldAsync(holdGuid, session, claimGuid));

        Assert.Equal(SharedHeldOrderApiErrorKind.Retryable, exception.Kind);
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.NotNull(claim);
        Assert.Equal(LocalClaimStatus.Prepared, claim!.Status);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_activate_response_mismatch_keeps_local_prepared_and_does_not_restore()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = (_, _, _) => Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
                holdGuid,
                claimGuid,
                ServerClaimStatus.Prepared,
                SampleSaleCartV1(),
                session.DeviceCode,
                session.CashierId,
                session.CashierName,
                new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
                null,
                Revision: 1L)),
            Activate = (_, _, _) => Task.FromResult(
                ActiveClaimDto(holdGuid, Guid.NewGuid(), Revision: 2L))
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var exception = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.TakeRemoteHoldAsync(holdGuid, session, claimGuid));

        Assert.Equal("INVALID", exception.Code);
        Assert.Equal(
            LocalClaimStatus.Prepared,
            (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_prepare_without_expiry_uses_120_second_local_fallback()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = (_, _, _) => Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
                holdGuid,
                claimGuid,
                ServerClaimStatus.Prepared,
                SampleSaleCartV1(),
                session.DeviceCode,
                session.CashierId,
                session.CashierName,
                new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
                null,
                Revision: 1L)),
            Activate = (_, _, _) => throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Retryable,
                "activate result unknown",
                null,
                HttpStatusCode.GatewayTimeout)
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        await Assert.ThrowsAsync<SharedHeldOrderApiException>(
            () => coordinator.TakeRemoteHoldAsync(holdGuid, session, claimGuid));

        Assert.Equal(
            "2026-07-28T01:08:00.000Z",
            (await scope.Repository.GetClaimAsync(claimGuid))!.ExpiresAtIso);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_restore_crash_clears_cart_but_keeps_active_and_never_releases()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = (_, _, _) => Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
                holdGuid,
                claimGuid,
                ServerClaimStatus.Prepared,
                SampleSaleCartV1(),
                "POS-01",
                "cashier-1",
                "Cashier One",
                new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 28, 1, 4, 3, TimeSpan.Zero),
                Revision: 1L)),
            Activate = (_, _, _) => Task.FromResult(ActiveClaimDto(holdGuid, claimGuid, Revision: 2L)),
            Release = (_, _, _) => throw new InvalidOperationException("release must never run")
        };
        var failingMapper = new FailingReverseMapper();
        var coordinator = CreateCoordinator(scope, api, cart, failingMapper);

        var exception = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.TakeRemoteHoldAsync(holdGuid, session, claimGuid));

        Assert.Equal("RESTORE_FAILED", exception.Code);
        Assert.True(failingMapper.WasCalled);
        Assert.True(cart.IsEmpty);
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.NotNull(claim);
        Assert.Equal(LocalClaimStatus.Active, claim!.Status);
    }

    [Fact]
    public async Task RecallLocalPublicationAsync_restores_offline_without_api_access()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var payload = SampleCanonical(quantity: 2.25m, unitPriceCents: 888);
        Assert.True(await scope.Repository.UpsertPublicationAsync(
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderPublicationStatus.NeedsEvaluation,
            payloadCiphertext: null,
            "2026-07-28T00:59:00.000Z",
            "2026-07-28T00:59:00.000Z",
            "2026-07-28T00:59:00.000Z"));
        Assert.True(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid,
            expectedRevision: 1,
            payload,
            "2026-07-28T01:00:00.000Z"));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => throw new InvalidOperationException("offline recall must not access API")
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.RecallLocalPublicationAsync(holdGuid, session);

        Assert.True(result.RestoredToCart);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(2.25m, line.Quantity);
        Assert.Equal(8.88m, line.UnitPrice);
        var claims = await scope.Repository.FindRecoverableClaimsAsync("S001", "POS-01");
        var claim = Assert.Single(claims);
        Assert.Equal(SharedHeldOrderClaimSource.OfflineOrigin, claim.Source);
        Assert.Equal(LocalClaimStatus.Active, claim.Status);
        Assert.Equal(holdGuid, claim.HoldGuid);
        Assert.Null(claim.ServerRevision);
    }

    [Fact]
    public async Task RecallLocalPublicationAsync_requires_publication_copy()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var coordinator = CreateCoordinator(
            scope,
            new StubSharedHeldOrderApiClient(),
            cart);

        var exception = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.RecallLocalPublicationAsync(Guid.NewGuid(), session));

        Assert.Equal("NOT_FOUND", exception.Code);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_local_prepared_plus_server_active_completes_and_restores()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(
            ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "prepare-1", SampleCanonical(quantity: 3m, unitPriceCents: 1200))));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(
                    holdGuid,
                    claimGuid,
                    ServerClaimStatus.Active,
                    SampleSaleCartV1(quantity: 3m, unitPriceCents: 1200),
                    Revision: 2L)
            ])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        Assert.Contains(claimGuid, result.RestoredClaimIds);
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.Equal(LocalClaimStatus.Active, claim!.Status);
        Assert.Equal(2L, claim.ServerRevision);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal(12m, line.UnitPrice);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_local_active_plus_server_active_restores_from_local_facts()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "prepare-1", SampleCanonical(quantity: 1m, unitPriceCents: 500));
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid,
            "prepare-1",
            "activate-1",
            serverRevision: 2L,
            "2026-07-28T01:05:00.000Z"));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(
                    holdGuid,
                    claimGuid,
                    ServerClaimStatus.Active,
                    SampleSaleCartV1(quantity: 1m, unitPriceCents: 500),
                    Revision: 2L)
            ])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        Assert.Contains(claimGuid, result.RestoredClaimIds);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(5m, line.UnitPrice);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_server_prepared_without_local_is_saved_by_device_after_cashier_change()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(
                    holdGuid,
                    claimGuid,
                    ServerClaimStatus.Prepared,
                    SampleSaleCartV1(),
                    Revision: 1L,
                    cashierId: "previous-cashier")
            ])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        Assert.Contains(claimGuid, result.ReconciledPreparedClaimIds);
        Assert.Empty(result.RestoredClaimIds);
        Assert.True(cart.IsEmpty);
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.NotNull(claim);
        Assert.Equal(LocalClaimStatus.Prepared, claim!.Status);
        Assert.Equal(SharedHeldOrderClaimSource.RemoteClaim, claim.Source);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_server_active_without_local_durable_fact_fails_closed()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(holdGuid, claimGuid, ServerClaimStatus.Active, SampleSaleCartV1(), Revision: 2L)
            ])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        Assert.Empty(result.RestoredClaimIds);
        Assert.Null(await scope.Repository.GetClaimAsync(claimGuid));
        Assert.True(cart.IsEmpty);
        Assert.Contains(result.Mismatches, mismatch => mismatch.ClaimId == claimGuid);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_cross_scope_server_prepared_is_not_saved()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(
                    holdGuid,
                    claimGuid,
                    ServerClaimStatus.Prepared,
                    SampleSaleCartV1(),
                    Revision: 1L,
                    storeCode: "OTHER",
                    deviceCode: "OTHER-DEVICE")
            ])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        Assert.Null(await scope.Repository.GetClaimAsync(claimGuid));
        Assert.Empty(result.ReconciledPreparedClaimIds);
        Assert.Contains(result.Mismatches, mismatch => mismatch.ClaimId == claimGuid);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_payload_mismatch_keeps_local_prepared_and_does_not_restore()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(
            ClaimDraft(
                claimGuid,
                holdGuid,
                session,
                SharedHeldOrderClaimSource.RemoteClaim,
                "prepare-payload",
                SampleCanonical(quantity: 1m, unitPriceCents: 500))));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(
                    holdGuid,
                    claimGuid,
                    ServerClaimStatus.Active,
                    SampleSaleCartV1(quantity: 2m, unitPriceCents: 500),
                    Revision: 2L)
            ])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        Assert.Empty(result.RestoredClaimIds);
        Assert.True(cart.IsEmpty);
        Assert.Contains(result.Mismatches, mismatch => mismatch.ClaimId == claimGuid);
        Assert.Equal(
            LocalClaimStatus.Prepared,
            (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_offline_origin_without_server_claim_is_not_a_mismatch()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var payload = SampleCanonical();
        Assert.True(await scope.Repository.UpsertPublicationAsync(
            holdGuid,
            session.StoreCode,
            session.DeviceCode,
            SharedHeldOrderPublicationStatus.NeedsEvaluation,
            payloadCiphertext: null,
            "2026-07-28T00:59:00.000Z",
            "2026-07-28T00:59:00.000Z",
            "2026-07-28T00:59:00.000Z"));
        Assert.True(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid,
            expectedRevision: 1,
            payload,
            "2026-07-28T01:00:00.000Z"));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>([])
        };
        var coordinator = CreateCoordinator(scope, api, cart);
        var taken = await coordinator.RecallLocalPublicationAsync(holdGuid, session);
        cart.Clear();

        var result = await coordinator.ReconcileClaimsAsync(session);

        Assert.DoesNotContain(result.Mismatches, mismatch => mismatch.ClaimId == taken.ClaimId);
        Assert.Equal(
            LocalClaimStatus.Active,
            (await scope.Repository.GetClaimAsync(taken.ClaimId))!.Status);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_local_active_with_mismatched_hold_guid_fails_closed()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var localHoldGuid = Guid.NewGuid();
        var serverHoldGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, localHoldGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "prepare-1", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid,
            "prepare-1",
            "activate-1",
            serverRevision: 2L,
            "2026-07-28T01:05:00.000Z"));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(serverHoldGuid, claimGuid, ServerClaimStatus.Active, SampleSaleCartV1(), Revision: 2L)
            ])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        Assert.Empty(result.RestoredClaimIds);
        Assert.True(cart.IsEmpty);
        Assert.Contains(result.Mismatches, mismatch => mismatch.ClaimId == claimGuid);
        Assert.Equal(
            LocalClaimStatus.Active,
            (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_local_active_without_server_fact_keeps_local_active()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "prepare-1", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid,
            "prepare-1",
            "activate-1",
            serverRevision: 1L,
            "2026-07-28T01:05:00.000Z"));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>([])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        Assert.Empty(result.RestoredClaimIds);
        Assert.True(cart.IsEmpty);
        Assert.Contains(result.Mismatches, mismatch => mismatch.ClaimId == claimGuid);
        Assert.Equal(
            LocalClaimStatus.Active,
            (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_local_active_plus_server_prepared_is_mismatch_not_restored()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "prepare-1", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid,
            "prepare-1",
            "activate-1",
            serverRevision: 2L,
            "2026-07-28T01:05:00.000Z"));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(holdGuid, claimGuid, ServerClaimStatus.Prepared, SampleSaleCartV1(), Revision: 1L)
            ])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        Assert.Empty(result.RestoredClaimIds);
        Assert.True(cart.IsEmpty);
        Assert.Contains(result.Mismatches, mismatch => mismatch.ClaimId == claimGuid);
        Assert.Equal(
            LocalClaimStatus.Active,
            (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task RecoverLocalClaimsAsync_offline_origin_prepared_is_activated_and_restored_without_api()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.OfflineOrigin, "offline-1", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        // API stub 未配置任何 handler：一旦访问 API 立即抛 InvalidOperationException。
        var api = new StubSharedHeldOrderApiClient();
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.RecoverLocalClaimsAsync(session);

        Assert.Equal([claimGuid], result.RestoredClaimIds);
        Assert.Empty(result.Mismatches);
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.Equal(LocalClaimStatus.Active, claim!.Status);
        Assert.Null(claim.ServerRevision);
        Assert.Equal($"wpf-offline-activate:{claimGuid:D}", claim.ActivateIdempotencyKey);
        Assert.Equal(claimGuid, cart.CreateSnapshot().SharedHeldOrderClaimId);
        Assert.Single(cart.Lines);
    }

    [Fact]
    public async Task RecoverLocalClaimsAsync_offline_origin_active_restores_cart_without_api()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.OfflineOrigin, "offline-2", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid,
            "offline-2",
            "offline-activate-2",
            serverRevision: null,
            "2026-07-28T01:05:00.000Z"));
        var api = new StubSharedHeldOrderApiClient();
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.RecoverLocalClaimsAsync(session);

        Assert.Equal([claimGuid], result.RestoredClaimIds);
        Assert.Empty(result.Mismatches);
        Assert.Equal(LocalClaimStatus.Active, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
        Assert.Equal(claimGuid, cart.CreateSnapshot().SharedHeldOrderClaimId);
        Assert.Single(cart.Lines);
    }

    [Fact]
    public async Task RecoverLocalClaimsAsync_skips_remote_claims_and_bound_active_and_keeps_active()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var remoteHold = Guid.NewGuid();
        var remoteClaim = Guid.NewGuid();
        var remoteDraft = ClaimDraft(remoteClaim, remoteHold, session, SharedHeldOrderClaimSource.RemoteClaim, "remote-1", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(remoteDraft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            remoteClaim,
            "remote-1",
            "remote-activate-1",
            serverRevision: 3L,
            "2026-07-28T01:05:00.000Z"));
        var boundHold = Guid.NewGuid();
        var boundClaim = Guid.NewGuid();
        var boundDraft = ClaimDraft(boundClaim, boundHold, session, SharedHeldOrderClaimSource.OfflineOrigin, "offline-bound", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(boundDraft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            boundClaim,
            "offline-bound",
            "offline-bound-activate",
            serverRevision: null,
            "2026-07-28T01:05:00.000Z"));
        Assert.True(await scope.Repository.TryBindOrderAsync(
            boundClaim,
            "offline-bound-activate",
            Guid.NewGuid().ToString("D"),
            "2026-07-28T01:06:00.000Z"));
        var api = new StubSharedHeldOrderApiClient();
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.RecoverLocalClaimsAsync(session);

        // RemoteClaim 不触碰；已绑定 OfflineOrigin 不回灌购物车；两者都保持 Active。
        Assert.Empty(result.RestoredClaimIds);
        Assert.Contains(result.Mismatches, mismatch => mismatch.ClaimId == boundClaim);
        Assert.True(cart.IsEmpty);
        Assert.Equal(LocalClaimStatus.Active, (await scope.Repository.GetClaimAsync(remoteClaim))!.Status);
        Assert.Equal(LocalClaimStatus.Active, (await scope.Repository.GetClaimAsync(boundClaim))!.Status);
    }

    [Fact]
    public async Task RecoverLocalClaimsAsync_non_empty_cart_keeps_active_fact_without_restore()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        cart.AddItem(TestCatalog.SellableItem("P-9", "CODE-9", 5m));
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.OfflineOrigin, "offline-3", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        var coordinator = CreateCoordinator(scope, new StubSharedHeldOrderApiClient(), cart);

        var result = await coordinator.RecoverLocalClaimsAsync(session);

        Assert.Empty(result.RestoredClaimIds);
        Assert.Contains(result.Mismatches, mismatch => mismatch.ClaimId == claimGuid);
        Assert.Single(cart.Lines);
        // 补激活先完成本地事实；购物车冲突只跳过恢复，Active 绝不自动释放。
        Assert.Equal(LocalClaimStatus.Active, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task ReleaseActiveClaimAsync_remote_owner_release_closes_claim_and_exact_cart_binding()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(
            ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "release-remote", SampleCanonical())));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid, "release-remote", "release-remote-active", 1L, "2026-07-28T01:05:00.000Z"));
        cart.RestoreSharedSaleSnapshot(
            new SharedHeldOrderReverseMapper().Map(SampleCanonical(), session.StoreCode) with
            {
                SharedHeldOrderClaimId = claimGuid
            });
        var releaseCalls = 0;
        var api = new StubSharedHeldOrderApiClient
        {
            Release = (actualHoldGuid, actualClaimGuid, _) =>
            {
                releaseCalls++;
                Assert.Equal(holdGuid, actualHoldGuid);
                Assert.Equal(claimGuid, actualClaimGuid);
                return Task.FromResult(OwnerReleasedClaimDto(holdGuid, claimGuid, Revision: 2L));
            }
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        await coordinator.ReleaseActiveClaimAsync(claimGuid, session);

        Assert.Equal(1, releaseCalls);
        Assert.True(cart.IsEmpty);
        Assert.Equal(LocalClaimStatus.Released, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task ReleaseActiveClaimAsync_offline_origin_is_local_only_and_clears_cart()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(
            ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.OfflineOrigin, "release-offline", SampleCanonical())));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid, "release-offline", "release-offline-active", null, "2026-07-28T01:05:00.000Z"));
        cart.RestoreSharedSaleSnapshot(
            new SharedHeldOrderReverseMapper().Map(SampleCanonical(), session.StoreCode) with
            {
                SharedHeldOrderClaimId = claimGuid
            });
        var api = new StubSharedHeldOrderApiClient
        {
            Release = (_, _, _) => throw new InvalidOperationException("OfflineOrigin 不得访问 release API")
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        await coordinator.ReleaseActiveClaimAsync(claimGuid, session);

        Assert.True(cart.IsEmpty);
        Assert.Equal(LocalClaimStatus.Released, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task ReleaseActiveClaimAsync_server_failure_preserves_claim_and_cart()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(
            ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "release-fail", SampleCanonical())));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid, "release-fail", "release-fail-active", 1L, "2026-07-28T01:05:00.000Z"));
        cart.RestoreSharedSaleSnapshot(
            new SharedHeldOrderReverseMapper().Map(SampleCanonical(), session.StoreCode) with
            {
                SharedHeldOrderClaimId = claimGuid
            });
        var api = new StubSharedHeldOrderApiClient
        {
            Release = (_, _, _) => throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Retryable,
                "network unavailable",
                "NETWORK",
                HttpStatusCode.ServiceUnavailable)
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        await Assert.ThrowsAsync<SharedHeldOrderApiException>(
            () => coordinator.ReleaseActiveClaimAsync(claimGuid, session));

        Assert.Single(cart.Lines);
        Assert.Equal(claimGuid, cart.CreateSnapshot().SharedHeldOrderClaimId);
        Assert.Equal(LocalClaimStatus.Active, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task ReleaseActiveClaimAsync_retries_cart_cleanup_after_local_release_crash_window()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(
            ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "release-retry", SampleCanonical())));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid, "release-retry", "release-retry-active", 1L, "2026-07-28T01:05:00.000Z"));
        cart.RestoreSharedSaleSnapshot(
            new SharedHeldOrderReverseMapper().Map(SampleCanonical(), session.StoreCode) with
            {
                SharedHeldOrderClaimId = claimGuid
            });
        Assert.True(await scope.Repository.TryReleaseClaimAsync(
            claimGuid,
            $"wpf-release:{claimGuid:D}",
            LocalClaimStatus.Active,
            "2026-07-28T01:05:30.000Z"));
        var api = new StubSharedHeldOrderApiClient
        {
            Release = (_, _, _) => throw new InvalidOperationException("已完成本地 release 的重试不得再访问 API")
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        await coordinator.ReleaseActiveClaimAsync(claimGuid, session);

        Assert.True(cart.IsEmpty);
        Assert.Equal(LocalClaimStatus.Released, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task ForceReleaseAsync_server_success_cleans_exact_active_cart_and_releases_claim()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "fr-remote-1", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid,
            "fr-remote-1",
            "fr-remote-activate-1",
            serverRevision: 4L,
            "2026-07-28T01:05:00.000Z"));
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
            [
                new PosCartLineSnapshot(
                    "S001",
                    "P-1",
                    "REF-1",
                    "Product 1",
                    "CODE-1",
                    "ITEM-1",
                    null,
                    1m,
                    19.99m,
                    0m,
                    null,
                    Hbpos.Contracts.Catalog.PriceSourceKind.StoreRetailPrice,
                    "Store Retail Price")
            ], claimGuid));
        var api = new StubSharedHeldOrderApiClient
        {
            ForceRelease = (actualHoldGuid, actualClaimGuid, request, _) =>
            {
                Assert.Equal(holdGuid, actualHoldGuid);
                Assert.Equal(claimGuid, actualClaimGuid);
                Assert.Equal("主管强制释放", request.Reason);
                return Task.FromResult(ReleasedClaimDto(holdGuid, claimGuid, Revision: 5L));
            }
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        await coordinator.ForceReleaseAsync(holdGuid, claimGuid, "主管强制释放", session);

        Assert.True(cart.IsEmpty);
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.Equal(LocalClaimStatus.Released, claim!.Status);
        Assert.NotNull(claim.ReleaseIdempotencyKey);
        Assert.Null(claim.BoundOrderGuid);
    }

    [Fact]
    public async Task ForceReleaseAsync_mismatched_cart_binding_preserves_local_claim_and_can_retry_after_cart_is_safe()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var otherClaim = Guid.NewGuid();
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
            [
                new PosCartLineSnapshot(
                    "S001",
                    "P-1",
                    "REF-1",
                    "Product 1",
                    "CODE-1",
                    "ITEM-1",
                    null,
                    1m,
                    19.99m,
                    0m,
                    null,
                    Hbpos.Contracts.Catalog.PriceSourceKind.StoreRetailPrice,
                    "Store Retail Price")
            ], otherClaim));
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "fr-remote-2", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid,
            "fr-remote-2",
            "fr-remote-activate-2",
            serverRevision: 1L,
            "2026-07-28T01:05:00.000Z"));
        var api = new StubSharedHeldOrderApiClient
        {
            ForceRelease = (_, _, _, _) => Task.FromResult(ReleasedClaimDto(holdGuid, claimGuid, Revision: 2L))
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var mismatch = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.ForceReleaseAsync(holdGuid, claimGuid, "主管强制释放", session));

        Assert.Equal("FENCE_CONFLICT", mismatch.Code);
        // 购物车绑定的是其他 claim：不得误清，也不得提前释放本地 durable fence。
        Assert.Single(cart.Lines);
        Assert.Equal(otherClaim, cart.CreateSnapshot().SharedHeldOrderClaimId);
        Assert.Equal(LocalClaimStatus.Active, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);

        // 服务端 Released 可幂等重放；本地购物车变安全后再次调用即可收口本地 claim。
        cart.Clear();
        await coordinator.ForceReleaseAsync(holdGuid, claimGuid, "主管强制释放", session);
        Assert.Equal(LocalClaimStatus.Released, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task ForceReleaseAsync_rejects_mismatched_server_response_before_local_cleanup()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "fr-response", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid,
            "fr-response",
            "fr-response-activate",
            serverRevision: 1L,
            "2026-07-28T01:05:00.000Z"));
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
            [
                new PosCartLineSnapshot(
                    "S001",
                    "P-1",
                    "REF-1",
                    "Product 1",
                    "CODE-1",
                    "ITEM-1",
                    null,
                    1m,
                    19.99m,
                    0m,
                    null,
                    Hbpos.Contracts.Catalog.PriceSourceKind.StoreRetailPrice,
                    "Store Retail Price")
            ], claimGuid));
        var api = new StubSharedHeldOrderApiClient
        {
            ForceRelease = (_, _, _, _) => Task.FromResult(
                ReleasedClaimDto(holdGuid, claimGuid, Revision: 2L) with
                {
                    StoreCode = "OTHER"
                })
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var mismatch = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.ForceReleaseAsync(holdGuid, claimGuid, "主管强制释放", session));

        Assert.Equal("INVALID", mismatch.Code);
        Assert.Single(cart.Lines);
        Assert.Equal(claimGuid, cart.CreateSnapshot().SharedHeldOrderClaimId);
        Assert.Equal(LocalClaimStatus.Active, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
    }

    [Fact]
    public async Task ForceReleaseAsync_prepared_claim_never_clears_cart()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        cart.AddItem(TestCatalog.SellableItem("P-7", "CODE-7", 8m));
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "fr-remote-3", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        var api = new StubSharedHeldOrderApiClient
        {
            ForceRelease = (_, _, _, _) => Task.FromResult(ReleasedClaimDto(holdGuid, claimGuid, Revision: 2L))
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        await coordinator.ForceReleaseAsync(holdGuid, claimGuid, "主管强制释放", session);

        // Prepared 未恢复购物车：即使购物车非空也绝不误清。
        Assert.Single(cart.Lines);
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.Equal(LocalClaimStatus.Released, claim!.Status);
    }

    [Fact]
    public async Task ForceReleaseAsync_server_failure_keeps_claim_and_cart_retryable()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var draft = ClaimDraft(claimGuid, holdGuid, session, SharedHeldOrderClaimSource.RemoteClaim, "fr-remote-4", SampleCanonical());
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimGuid,
            "fr-remote-4",
            "fr-remote-activate-4",
            serverRevision: 1L,
            "2026-07-28T01:05:00.000Z"));
        cart.RestoreSharedSaleSnapshot(new PosCartSnapshot(
            [
                new PosCartLineSnapshot(
                    "S001",
                    "P-1",
                    "REF-1",
                    "Product 1",
                    "CODE-1",
                    "ITEM-1",
                    null,
                    1m,
                    19.99m,
                    0m,
                    null,
                    Hbpos.Contracts.Catalog.PriceSourceKind.StoreRetailPrice,
                    "Store Retail Price")
            ], claimGuid));
        var api = new StubSharedHeldOrderApiClient
        {
            ForceRelease = (_, _, _, _) => throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Retryable,
                "network unavailable",
                "NETWORK",
                HttpStatusCode.ServiceUnavailable)
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        await Assert.ThrowsAsync<SharedHeldOrderApiException>(
            () => coordinator.ForceReleaseAsync(holdGuid, claimGuid, "主管强制释放", session));

        // 服务端失败：本地 claim 与购物车原样保留，可安全重试。
        Assert.Single(cart.Lines);
        Assert.Equal(claimGuid, cart.CreateSnapshot().SharedHeldOrderClaimId);
        Assert.Equal(LocalClaimStatus.Active, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
        Assert.Null((await scope.Repository.GetClaimAsync(claimGuid))!.ReleaseIdempotencyKey);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_expires_stale_remote_prepared_and_clears_fence()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            claimGuid,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            "prepare-expired",
            SampleCanonical(),
            "2026-07-28T01:02:00.000Z",
            "2026-07-28T01:04:00.000Z")));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>([])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        // 本地 RemoteClaim Prepared 且可信 ExpiresAt 已过时：reconcile 前幂等推进
        // 本地终态并清 fence；服务端缺失也不再是 mismatch。
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.NotNull(claim);
        Assert.Equal(LocalClaimStatus.Released, claim!.Status);
        Assert.StartsWith("wpf-expired-prepare:", claim.ReleaseIdempotencyKey!);
        Assert.Empty(await scope.Repository.FindRecoverableClaimsAsync("S001", "POS-01"));
        Assert.Empty(result.RestoredClaimIds);
        Assert.Empty(result.ReconciledPreparedClaimIds);
        Assert.Empty(result.Mismatches);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_expired_local_prepared_with_server_still_prepared_preserves_server_blocking_fact()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            claimGuid,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            "prepare-expired-server-stale",
            SampleCanonical(),
            "2026-07-28T01:02:00.000Z",
            "2026-07-28T01:04:00.000Z")));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(
                    holdGuid,
                    claimGuid,
                    ServerClaimStatus.Prepared,
                    SampleSaleCartV1(),
                    Revision: 3L)
            ])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.ReconcileClaimsAsync(session);

        // 时钟偏差窗口：claims/mine 仍返回 Prepared，说明服务端仍认为它阻塞；
        // 本地不得单方面释放 fence。
        Assert.Equal(LocalClaimStatus.Prepared, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
        Assert.Empty(result.RestoredClaimIds);
        Assert.Contains(claimGuid, result.ReconciledPreparedClaimIds);
        Assert.Empty(result.Mismatches);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_repeated_server_prepared_reconcile_preserves_local_fence_idempotently()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            claimGuid,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            "prepare-expired-replay",
            SampleCanonical(),
            "2026-07-28T01:02:00.000Z",
            "2026-07-28T01:04:00.000Z")));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(
                    holdGuid,
                    claimGuid,
                    ServerClaimStatus.Prepared,
                    SampleSaleCartV1(),
                    Revision: 3L)
            ])
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        // 第一次 reconcile：服务端仍报告 Prepared，本地保留阻塞事实。
        var first = await coordinator.ReconcileClaimsAsync(session);
        Assert.Equal(LocalClaimStatus.Prepared, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
        Assert.Contains(claimGuid, first.ReconciledPreparedClaimIds);
        Assert.Empty(first.Mismatches);

        // 重放仍不释放、不重存，也不报 mismatch 噪音。
        var second = await coordinator.ReconcileClaimsAsync(session);
        Assert.Equal(LocalClaimStatus.Prepared, (await scope.Repository.GetClaimAsync(claimGuid))!.Status);
        Assert.Empty(second.RestoredClaimIds);
        Assert.Contains(claimGuid, second.ReconciledPreparedClaimIds);
        Assert.Empty(second.Mismatches);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_expires_stale_remote_prepared_fence_before_new_prepare()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var staleHoldGuid = Guid.NewGuid();
        var staleClaimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            staleClaimGuid,
            staleHoldGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            "prepare-stale",
            SampleCanonical(),
            "2026-07-28T01:02:00.000Z",
            "2026-07-28T01:04:00.000Z")));

        var newHoldGuid = Guid.NewGuid();
        var newClaimGuid = Guid.NewGuid();
        var calls = new List<string>();
        var api = new StubSharedHeldOrderApiClient
        {
            Prepare = (actualHoldGuid, request, _) =>
            {
                calls.Add("prepare");
                Assert.Equal(newHoldGuid, actualHoldGuid);
                Assert.Equal(newClaimGuid, request.ClaimGuid);
                return Task.FromResult(new SharedHeldOrderClaimPrepareResponse(
                    newHoldGuid,
                    newClaimGuid,
                    ServerClaimStatus.Prepared,
                    SampleSaleCartV1(),
                    "POS-01",
                    "cashier-1",
                    "Cashier One",
                    new DateTimeOffset(2026, 7, 28, 1, 6, 1, TimeSpan.Zero),
                    new DateTimeOffset(2026, 7, 28, 1, 8, 1, TimeSpan.Zero),
                    Revision: 1L));
            },
            Activate = (_, _, _) => Task.FromResult(ActiveClaimDto(newHoldGuid, newClaimGuid, Revision: 2L))
        };
        // 旧 claim 服务端已不存在（服务端同样会过期 Prepared）：本地推进 Released。
        api.ClaimsMine = _ =>
        {
            calls.Add("claims-mine");
            return Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>([]);
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var result = await coordinator.TakeRemoteHoldAsync(newHoldGuid, session, newClaimGuid);

        // 下一次 prepare 前：过期旧 fence 已推进 Released，新取单不再 FENCE_CONFLICT。
        Assert.Equal(new[] { "claims-mine", "prepare", "activate" }, calls);
        Assert.Equal(newClaimGuid, result.ClaimId);
        Assert.Equal(LocalClaimStatus.Released, (await scope.Repository.GetClaimAsync(staleClaimGuid))!.Status);
        Assert.Equal(LocalClaimStatus.Active, (await scope.Repository.GetClaimAsync(newClaimGuid))!.Status);
        Assert.Equal(newClaimGuid, cart.CreateSnapshot().SharedHeldOrderClaimId);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_server_active_stale_local_prepared_is_not_expired_and_take_fails_closed()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var staleHoldGuid = Guid.NewGuid();
        var staleClaimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            staleClaimGuid,
            staleHoldGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            "prepare-stale-active",
            SampleCanonical(),
            "2026-07-28T01:02:00.000Z",
            "2026-07-28T01:04:00.000Z")));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(
                    staleHoldGuid,
                    staleClaimGuid,
                    ServerClaimStatus.Active,
                    SampleSaleCartV1(),
                    Revision: 2L)
            ]),
            Prepare = (_, _, _) => throw new InvalidOperationException(
                "prepare must not run while an existing server Active claim still owns the local fence")
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        // 崩溃窗口：服务端 Active、本地仍 Prepared 且本地 ExpiresAt 已过 ——
        // 绝不能按过期释放（否则遗留服务端 Active）；本地 fence 保留，新取单 fail-closed。
        var exception = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.TakeRemoteHoldAsync(Guid.NewGuid(), session, Guid.NewGuid()));
        Assert.Equal("FENCE_CONFLICT", exception.Code);
        var stale = await scope.Repository.GetClaimAsync(staleClaimGuid);
        Assert.NotNull(stale);
        Assert.Equal(LocalClaimStatus.Prepared, stale!.Status);
        Assert.Null(stale.ReleaseIdempotencyKey);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_server_prepared_stale_local_prepared_does_not_create_new_claim()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var staleHoldGuid = Guid.NewGuid();
        var staleClaimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            staleClaimGuid,
            staleHoldGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            "prepare-stale-server-prepared",
            SampleCanonical(),
            "2026-07-28T01:02:00.000Z",
            "2026-07-28T01:04:00.000Z")));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => Task.FromResult<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            [
                RecoveryClaimDto(
                    staleHoldGuid,
                    staleClaimGuid,
                    ServerClaimStatus.Prepared,
                    SampleSaleCartV1(),
                    Revision: 2L)
            ]),
            Prepare = (_, _, _) => throw new InvalidOperationException(
                "prepare must not run while an existing server Prepared claim still owns the local fence")
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var exception = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.TakeRemoteHoldAsync(Guid.NewGuid(), session, Guid.NewGuid()));

        Assert.Equal("FENCE_CONFLICT", exception.Code);
        var stale = await scope.Repository.GetClaimAsync(staleClaimGuid);
        Assert.NotNull(stale);
        Assert.Equal(LocalClaimStatus.Prepared, stale!.Status);
        Assert.Null(stale.ReleaseIdempotencyKey);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_unexpired_local_prepared_does_not_call_claims_mine_or_prepare()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var existingClaimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            existingClaimGuid,
            Guid.NewGuid(),
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            "prepare-unexpired",
            SampleCanonical(),
            "2026-07-28T01:05:00.000Z",
            "2026-07-28T01:08:00.000Z")));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => throw new InvalidOperationException("claims/mine must not run for an unexpired fence"),
            Prepare = (_, _, _) => throw new InvalidOperationException("prepare must not run for an occupied local fence")
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        var exception = await Assert.ThrowsAsync<SharedHeldOrderCoordinatorException>(
            () => coordinator.TakeRemoteHoldAsync(Guid.NewGuid(), session, Guid.NewGuid()));

        Assert.Equal("FENCE_CONFLICT", exception.Code);
        Assert.Equal(LocalClaimStatus.Prepared, (await scope.Repository.GetClaimAsync(existingClaimGuid))!.Status);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task ReconcileClaimsAsync_claims_mine_failure_releases_nothing()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            claimGuid,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            "prepare-claims-mine-fail",
            SampleCanonical(),
            "2026-07-28T01:02:00.000Z",
            "2026-07-28T01:04:00.000Z")));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Retryable,
                "network unavailable",
                "NETWORK",
                HttpStatusCode.ServiceUnavailable)
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        // ClaimsMine 失败：绝不按过期释放本地 Prepared，事实与 fence 原样保留可重试。
        await Assert.ThrowsAsync<SharedHeldOrderApiException>(
            () => coordinator.ReconcileClaimsAsync(session));
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.NotNull(claim);
        Assert.Equal(LocalClaimStatus.Prepared, claim!.Status);
        Assert.Null(claim.ReleaseIdempotencyKey);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task TakeRemoteHoldAsync_claims_mine_failure_keeps_stale_prepared_and_does_not_prepare()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var session = Session();
        var cart = new PosCartService();
        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            claimGuid,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            "prepare-take-mine-fail",
            SampleCanonical(),
            "2026-07-28T01:02:00.000Z",
            "2026-07-28T01:04:00.000Z")));
        var api = new StubSharedHeldOrderApiClient
        {
            ClaimsMine = _ => throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Retryable,
                "network unavailable",
                "NETWORK",
                HttpStatusCode.ServiceUnavailable),
            Prepare = (_, _, _) => throw new InvalidOperationException("prepare must not run before claims/mine succeeds")
        };
        var coordinator = CreateCoordinator(scope, api, cart);

        // ClaimsMine 失败：不释放、不 prepare，本地 Prepared fence 原样保留。
        await Assert.ThrowsAsync<SharedHeldOrderApiException>(
            () => coordinator.TakeRemoteHoldAsync(Guid.NewGuid(), session, Guid.NewGuid()));
        var claim = await scope.Repository.GetClaimAsync(claimGuid);
        Assert.NotNull(claim);
        Assert.Equal(LocalClaimStatus.Prepared, claim!.Status);
        Assert.Null(claim.ReleaseIdempotencyKey);
        Assert.True(cart.IsEmpty);
    }

    private static SharedHeldOrderCoordinator CreateCoordinator(
        RepositoryScope scope,
        StubSharedHeldOrderApiClient api,
        PosCartService cart,
        ISharedHeldOrderReverseMapper? reverseMapper = null)
    {
        return new SharedHeldOrderCoordinator(
            api,
            scope.Repository,
            reverseMapper ?? new SharedHeldOrderReverseMapper(),
            cart,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 1, 6, 0, TimeSpan.Zero)));
    }

    private static SharedHeldOrderClaimDraft ClaimDraft(
        Guid claimGuid,
        Guid holdGuid,
        PosSessionState session,
        SharedHeldOrderClaimSource source,
        string prepareKey,
        SharedHeldOrderCanonicalPayload payload)
    {
        return new SharedHeldOrderClaimDraft(
            claimGuid,
            holdGuid,
            session.StoreCode,
            session.DeviceCode,
            source,
            prepareKey,
            payload,
            "2026-07-28T01:02:00.000Z",
            "2026-07-28T01:04:00.000Z");
    }

    private static SharedHeldOrderClaimDto ActiveClaimDto(Guid holdGuid, Guid claimGuid, long Revision)
    {
        return new SharedHeldOrderClaimDto(
            holdGuid,
            claimGuid,
            ServerClaimStatus.Active,
            "S001",
            "POS-01",
            "cashier-1",
            "Cashier One",
            new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 28, 1, 5, 0, TimeSpan.Zero),
            null,
            new DateTimeOffset(2026, 7, 28, 1, 5, 0, TimeSpan.Zero),
            null,
            false,
            null,
            null,
            null,
            null,
            Revision);
    }

    private static SharedHeldOrderClaimDto ReleasedClaimDto(Guid holdGuid, Guid claimGuid, long Revision)
    {
        return new SharedHeldOrderClaimDto(
            holdGuid,
            claimGuid,
            ServerClaimStatus.Released,
            "S001",
            "POS-01",
            "cashier-1",
            "Cashier One",
            new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 28, 1, 5, 0, TimeSpan.Zero),
            null,
            null,
            new DateTimeOffset(2026, 7, 28, 1, 7, 0, TimeSpan.Zero),
            true,
            "主管强制释放",
            "C999",
            "Boss",
            new DateTimeOffset(2026, 7, 28, 1, 7, 0, TimeSpan.Zero),
            Revision);
    }

    private static SharedHeldOrderClaimDto OwnerReleasedClaimDto(Guid holdGuid, Guid claimGuid, long Revision)
    {
        return ReleasedClaimDto(holdGuid, claimGuid, Revision) with
        {
            ForceReleased = false,
            ForceReleaseReason = null,
            ForceReleaseCashierId = null,
            ForceReleaseCashierName = null,
            ForceReleasedAtUtc = null
        };
    }

    private static SharedHeldOrderRecoveryClaimDto RecoveryClaimDto(
        Guid holdGuid,
        Guid claimGuid,
        ServerClaimStatus status,
        SharedSaleCartV1 payload,
        long Revision,
        string storeCode = "S001",
        string deviceCode = "POS-01",
        string cashierId = "cashier-1")
    {
        return new SharedHeldOrderRecoveryClaimDto(
            holdGuid,
            claimGuid,
            status,
            storeCode,
            deviceCode,
            cashierId,
            "Cashier One",
            payload,
            new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 28, 1, 5, 0, TimeSpan.Zero),
            null,
            status == ServerClaimStatus.Active
                ? new DateTimeOffset(2026, 7, 28, 1, 5, 0, TimeSpan.Zero)
                : null,
            Revision);
    }

    private sealed class FailingReverseMapper : ISharedHeldOrderReverseMapper
    {
        public bool WasCalled { get; private set; }

        public PosCartSnapshot Map(SharedHeldOrderCanonicalPayload payload, string storeCode)
        {
            WasCalled = true;
            throw new InvalidOperationException("simulated restore crash");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }

    private static class TestCatalog
    {
        public static Hbpos.Contracts.Catalog.SellableItemDto SellableItem(
            string productCode,
            string lookupCode,
            decimal price)
        {
            return new Hbpos.Contracts.Catalog.SellableItemDto(
                "S001",
                productCode,
                null,
                "Product " + productCode,
                lookupCode,
                null,
                lookupCode,
                price,
                Hbpos.Contracts.Catalog.PriceSourceKind.ProductBase,
                "Product Base",
                1m,
                null,
                null);
        }
    }
}
