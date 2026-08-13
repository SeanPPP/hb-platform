using System.Reflection;
using System.Text.Json;
using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.HeldOrders;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class SharedHeldOrderServiceTests
{
    [Fact]
    public async Task Publish_is_idempotent_for_the_same_fingerprint_and_rejects_different_payload()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();

        var first = await harness.Service.PublishAsync(
            request,
            harness.Identity,
            CancellationToken.None);
        var replay = await harness.Service.PublishAsync(
            request,
            harness.Identity,
            CancellationToken.None);

        Assert.False(first.AlreadyExists);
        Assert.True(replay.AlreadyExists);
        Assert.Equal(SharedHeldOrderStatus.Pending, replay.Status);

        var cart = Assert.IsType<SharedSaleCartV1>(request.Cart);
        var changed = request with
        {
            Cart = cart with
            {
                PricingState = cart.PricingState with { Revision = 2 }
            }
        };
        var mismatch = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.PublishAsync(changed, harness.Identity, CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Mismatch, mismatch.Code);
    }

    [Fact]
    public async Task Publish_replay_requires_the_same_hold_and_idempotency_facts()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);

        var differentKey = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.PublishAsync(
                request with { IdempotencyKey = "publish-different-key" },
                harness.Identity,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Mismatch, differentKey.Code);

        var differentHold = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.PublishAsync(
                request with { HoldGuid = Guid.NewGuid() },
                harness.Identity,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Mismatch, differentHold.Code);
    }

    [Fact]
    public async Task Cancel_changes_pending_to_cancelled_and_retries_idempotently()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);

        var cancelled = await harness.Service.CancelAsync(
            request.HoldGuid,
            harness.Identity,
            CancellationToken.None);
        var replay = await harness.Service.CancelAsync(
            request.HoldGuid,
            harness.Identity,
            CancellationToken.None);

        Assert.Equal(4, (int)SharedHeldOrderStatus.Cancelled);
        Assert.Equal(SharedHeldOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(2, cancelled.Revision);
        Assert.False(cancelled.AlreadyCancelled);
        Assert.True(replay.AlreadyCancelled);
        Assert.Equal(cancelled.UpdatedAtUtc, replay.UpdatedAtUtc);
        Assert.Equal(SharedHeldOrderStatus.Cancelled,
            (await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None))?.Status);
        Assert.Empty(await harness.Service.ListPendingAsync(harness.Identity, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_requires_same_store_and_original_publish_device_even_for_cancelled_replay()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);

        var otherDevice = SharedHeldOrderServiceTestSupport.Identity(
            storeCode: "S01",
            deviceCode: "POS-02");
        var crossStore = SharedHeldOrderServiceTestSupport.Identity(
            storeCode: "S02",
            deviceCode: "POS-02");

        var deviceDenied = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.CancelAsync(request.HoldGuid, otherDevice, CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.PermissionDenied, deviceDenied.Code);

        var storeDenied = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.CancelAsync(request.HoldGuid, crossStore, CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.CrossStore, storeDenied.Code);

        await harness.Service.CancelAsync(request.HoldGuid, harness.Identity, CancellationToken.None);
        var replayDenied = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.CancelAsync(request.HoldGuid, otherDevice, CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.PermissionDenied, replayDenied.Code);
    }

    [Fact]
    public async Task Cancel_rejects_prepared_and_active_blocking_claims()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var preparedRequest = SharedHeldOrderServiceTestSupport.PublishRequest(
            holdGuid: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            idempotencyKey: "publish-prepared-cancel");
        await harness.Service.PublishAsync(preparedRequest, harness.Identity, CancellationToken.None);
        await harness.Service.PrepareAsync(
            preparedRequest.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);

        var preparedDenied = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.CancelAsync(preparedRequest.HoldGuid, harness.Identity, CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Busy, preparedDenied.Code);

        var activeRequest = SharedHeldOrderServiceTestSupport.PublishRequest(
            holdGuid: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            idempotencyKey: "publish-active-cancel");
        await harness.Service.PublishAsync(activeRequest, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            activeRequest.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(
                claimGuid: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                idempotencyKey: "claim-active-cancel"),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ActivateAsync(
            activeRequest.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        var activeDenied = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.CancelAsync(activeRequest.HoldGuid, harness.Identity, CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Busy, activeDenied.Code);
    }

    [Fact]
    public async Task Expired_prepared_claim_is_released_before_cancel_continues()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);

        harness.Time.UtcNow = harness.Time.UtcNow.AddSeconds(121);
        var cancelled = await harness.Service.CancelAsync(
            request.HoldGuid,
            harness.Identity,
            CancellationToken.None);

        Assert.Equal(SharedHeldOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(SharedHeldOrderClaimStatus.Released,
            (await harness.Repository.GetClaimAsync(prepared.ClaimGuid, CancellationToken.None))?.Status);
        Assert.False((await harness.Repository.GetClaimAsync(prepared.ClaimGuid, CancellationToken.None))?.IsBlocking);
    }

    [Fact]
    public async Task Cancel_rejects_claimed_and_completed_holds()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var claimedRequest = SharedHeldOrderServiceTestSupport.PublishRequest(
            holdGuid: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            idempotencyKey: "publish-claimed-cancel");
        await harness.Service.PublishAsync(claimedRequest, harness.Identity, CancellationToken.None);
        var claim = await harness.Service.PrepareAsync(
            claimedRequest.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(
                claimGuid: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                idempotencyKey: "claim-claimed-cancel"),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ActivateAsync(
            claimedRequest.HoldGuid,
            claim.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        var claimedDenied = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.CancelAsync(claimedRequest.HoldGuid, harness.Identity, CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Busy, claimedDenied.Code);

        var completedRequest = SharedHeldOrderServiceTestSupport.PublishRequest(
            holdGuid: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            idempotencyKey: "publish-completed-cancel");
        await harness.Service.PublishAsync(completedRequest, harness.Identity, CancellationToken.None);
        var completedHold = await harness.Repository.GetHoldAsync(completedRequest.HoldGuid, CancellationToken.None);
        Assert.NotNull(completedHold);
        Assert.True(await harness.Repository.TryUpdateHoldAsync(
            completedHold! with
            {
                Status = SharedHeldOrderStatus.Completed,
                Revision = completedHold.Revision + 1,
                UpdatedAtUtc = harness.Time.UtcNow
            },
            completedHold.Revision,
            CancellationToken.None));

        var completedDenied = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.CancelAsync(completedRequest.HoldGuid, harness.Identity, CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Mismatch, completedDenied.Code);
    }

    [Fact]
    public async Task Publish_rejects_request_scope_that_conflicts_with_device_claims()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest() with { StoreCode = "S02" };

        var exception = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None));

        Assert.Equal(SharedHeldOrderErrorCodes.CrossStore, exception.Code);
        Assert.Empty(await harness.Repository.ListPendingAsync("S01", CancellationToken.None));
    }

    [Fact]
    public async Task Publish_persists_ciphertext_never_plaintext_and_roundtrips_payload()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();

        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);

        var stored = await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.NotEqual(
            JsonSerializer.Serialize(request.Cart),
            stored!.PayloadCiphertext);
        Assert.DoesNotContain("特价促销", stored.PayloadCiphertext, StringComparison.Ordinal);
        var decrypted = harness.Protector.Unprotect(stored.PayloadCiphertext);
        Assert.Equal(JsonSerializer.Serialize(request.Cart), JsonSerializer.Serialize(decrypted));
    }

    [Fact]
    public async Task List_pending_only_returns_pending_summaries_and_hides_active_and_completed()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var pending = SharedHeldOrderServiceTestSupport.PublishRequest(
            holdGuid: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            cart: SharedHeldOrderServiceTestSupport.DiscountedCart(
                SharedSaleCartV1Constants.DiscountModeManualAmount,
                cents: 2000),
            idempotencyKey: "publish-pending");
        var active = SharedHeldOrderServiceTestSupport.PublishRequest(
            holdGuid: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            idempotencyKey: "publish-active");
        await harness.Service.PublishAsync(pending, harness.Identity, CancellationToken.None);
        await harness.Service.PublishAsync(active, harness.Identity, CancellationToken.None);
        var claim = await harness.Service.PrepareAsync(
            active.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ActivateAsync(
            active.HoldGuid,
            claim.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        var list = await harness.Service.ListPendingAsync(harness.Identity, CancellationToken.None);

        var item = Assert.Single(list);
        Assert.Equal(pending.HoldGuid, item.HoldGuid);
        Assert.Equal(1, item.LineCount);
        Assert.Equal(3000L, item.TotalCents);
        Assert.Equal(2000L, item.DiscountCents);
        Assert.Equal(1000L, item.ActualCents);
        Assert.DoesNotContain(
            typeof(SharedHeldOrderListItemDto).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Ciphertext", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Concurrent_prepare_has_exactly_one_winner()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var firstClaim = SharedHeldOrderServiceTestSupport.PrepareRequest(
            claimGuid: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            idempotencyKey: "claim-race-a");
        var secondClaim = SharedHeldOrderServiceTestSupport.PrepareRequest(
            claimGuid: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            idempotencyKey: "claim-race-b");
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () =>
        {
            await start.Task;
            try
            {
                await harness.Service.PrepareAsync(
                    request.HoldGuid,
                    firstClaim,
                    harness.Identity,
                    CancellationToken.None);
                return true;
            }
            catch (SharedHeldOrderException ex) when (ex.Code == SharedHeldOrderErrorCodes.Busy)
            {
                return false;
            }
        });
        var secondTask = Task.Run(async () =>
        {
            await start.Task;
            try
            {
                await harness.Service.PrepareAsync(
                    request.HoldGuid,
                    secondClaim,
                    harness.Identity,
                    CancellationToken.None);
                return true;
            }
            catch (SharedHeldOrderException ex) when (ex.Code == SharedHeldOrderErrorCodes.Busy)
            {
                return false;
            }
        });

        start.SetResult(true);
        var firstWon = await firstTask;
        var secondWon = await secondTask;

        Assert.NotEqual(firstWon, secondWon);
        var blocking = await harness.Repository.GetBlockingClaimAsync(
            request.HoldGuid,
            CancellationToken.None);
        Assert.NotNull(blocking);
        Assert.True(blocking!.IsBlocking);
    }

    [Fact]
    public async Task Prepare_replays_same_claim_and_returns_decrypted_payload()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepare = SharedHeldOrderServiceTestSupport.PrepareRequest();

        var first = await harness.Service.PrepareAsync(
            request.HoldGuid,
            prepare,
            harness.Identity,
            CancellationToken.None);
        var replay = await harness.Service.PrepareAsync(
            request.HoldGuid,
            prepare,
            harness.Identity,
            CancellationToken.None);

        Assert.False(first.AlreadyExists);
        Assert.True(replay.AlreadyExists);
        Assert.Equal(JsonSerializer.Serialize(request.Cart), JsonSerializer.Serialize(first.Payload));
        Assert.Equal(JsonSerializer.Serialize(request.Cart), JsonSerializer.Serialize(replay.Payload));
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, replay.Status);
        Assert.Equal(harness.Time.UtcNow.AddSeconds(120), replay.ExpiresAtUtc);
    }

    [Fact]
    public async Task Prepared_claim_lazily_expires_after_ttl_and_frees_the_hold()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var first = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(
                claimGuid: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
            harness.Identity,
            CancellationToken.None);

        harness.Time.UtcNow = harness.Time.UtcNow.AddSeconds(121);
        var second = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(
                claimGuid: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                idempotencyKey: "claim-2"),
            harness.Identity,
            CancellationToken.None);

        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, second.Status);
        var expired = await harness.Repository.GetClaimAsync(first.ClaimGuid, CancellationToken.None);
        Assert.Equal(SharedHeldOrderClaimStatus.Released, expired?.Status);
        Assert.False(expired?.IsBlocking);
    }

    [Fact]
    public async Task Active_claim_never_auto_expires_and_blocks_the_hold()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        var activated = await harness.Service.ActivateAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        harness.Time.UtcNow = harness.Time.UtcNow.AddDays(30);
        var replay = await harness.Service.ActivateAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        Assert.Equal(SharedHeldOrderClaimStatus.Active, activated.Status);
        Assert.True(replay.AlreadyExists);
        Assert.Equal(SharedHeldOrderClaimStatus.Active, replay.Status);
        Assert.Null(replay.ExpiresAtUtc);
        var blocking = await harness.Repository.GetBlockingClaimAsync(
            request.HoldGuid,
            CancellationToken.None);
        Assert.Equal(SharedHeldOrderClaimStatus.Active, blocking?.Status);
        Assert.Empty(await harness.Service.ListPendingAsync(harness.Identity, CancellationToken.None));
    }

    [Fact]
    public async Task Owner_release_returns_the_hold_to_pending_and_replays_idempotently()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ActivateAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        var released = await harness.Service.ReleaseAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);
        var replay = await harness.Service.ReleaseAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        Assert.Equal(SharedHeldOrderClaimStatus.Released, released.Status);
        Assert.True(replay.AlreadyExists);
        var hold = await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None);
        Assert.Equal(SharedHeldOrderStatus.Pending, hold?.Status);
        var item = Assert.Single(await harness.Service.ListPendingAsync(
            harness.Identity,
            CancellationToken.None));
        Assert.Equal(request.HoldGuid, item.HoldGuid);
    }

    [Fact]
    public async Task Cross_store_claim_operations_are_rejected()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var otherStore = SharedHeldOrderServiceTestSupport.Identity(
            storeCode: "S02",
            deviceCode: "POS-99");

        var prepare = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.PrepareAsync(
                request.HoldGuid,
                SharedHeldOrderServiceTestSupport.PrepareRequest(),
                otherStore,
                CancellationToken.None));

        Assert.Equal(SharedHeldOrderErrorCodes.CrossStore, prepare.Code);
    }

    [Fact]
    public async Task Force_release_requires_history_recall_permission_and_non_empty_reason_with_audit_fields()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ActivateAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        var noPermission = SharedHeldOrderServiceTestSupport.Identity(permissionCodes: []);
        var denied = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.ForceReleaseAsync(
                request.HoldGuid,
                prepared.ClaimGuid,
                new SharedHeldOrderForceReleaseRequest("客人在店等待，主管强制释放"),
                noPermission,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.PermissionDenied, denied.Code);

        var supervisor = SharedHeldOrderServiceTestSupport.Identity(
            cashierId: "SUP-1",
            cashierName: "主管",
            permissionCodes: ["Permissions.PosTerminal.History.Recall"],
            cashierUserGuid: "U-SUPERVISOR");
        var emptyReason = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.ForceReleaseAsync(
                request.HoldGuid,
                prepared.ClaimGuid,
                new SharedHeldOrderForceReleaseRequest("   "),
                supervisor,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Invalid, emptyReason.Code);

        var released = await harness.Service.ForceReleaseAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            new SharedHeldOrderForceReleaseRequest("客人在店等待，主管强制释放"),
            supervisor,
            CancellationToken.None);

        Assert.Equal(SharedHeldOrderClaimStatus.Released, released.Status);
        Assert.True(released.ForceReleased);
        Assert.Equal("客人在店等待，主管强制释放", released.ForceReleaseReason);
        Assert.Equal("SUP-1", released.ForceReleaseCashierId);
        Assert.Equal("主管", released.ForceReleaseCashierName);
        Assert.Equal(harness.Time.UtcNow, released.ForceReleasedAtUtc);
        var hold = await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None);
        Assert.Equal(SharedHeldOrderStatus.Pending, hold?.Status);
    }

    [Fact]
    public async Task Claims_mine_returns_only_own_device_blocking_claims_without_payload()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        var otherDevice = SharedHeldOrderServiceTestSupport.Identity(
            storeCode: "S01",
            deviceCode: "POS-02");

        var myClaims = await harness.Service.ListMyClaimsAsync(
            harness.Identity,
            CancellationToken.None);
        var otherDeviceClaims = await harness.Service.ListMyClaimsAsync(
            otherDevice,
            CancellationToken.None);

        var mine = Assert.Single(myClaims);
        Assert.Equal(prepared.ClaimGuid, mine.ClaimGuid);
        Assert.Equal(JsonSerializer.Serialize(request.Cart), JsonSerializer.Serialize(mine.Payload));
        Assert.Empty(otherDeviceClaims);
        Assert.DoesNotContain(
            typeof(SharedHeldOrderClaimDto).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Ciphertext", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Claims_mine_expires_stale_prepared_claims_but_never_releases_active_claims()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);

        harness.Time.UtcNow = harness.Time.UtcNow.AddSeconds(121);
        Assert.Empty(await harness.Service.ListMyClaimsAsync(
            harness.Identity,
            CancellationToken.None));
        var expired = await harness.Repository.GetClaimAsync(prepared.ClaimGuid, CancellationToken.None);
        Assert.Equal(SharedHeldOrderClaimStatus.Released, expired?.Status);

        var second = SharedHeldOrderServiceTestSupport.PublishRequest(
            holdGuid: Guid.Parse("99999999-9999-9999-9999-999999999999"),
            idempotencyKey: "publish-active-mine");
        await harness.Service.PublishAsync(second, harness.Identity, CancellationToken.None);
        var activePrepared = await harness.Service.PrepareAsync(
            second.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(
                claimGuid: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                idempotencyKey: "claim-active-mine"),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ActivateAsync(
            second.HoldGuid,
            activePrepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        harness.Time.UtcNow = harness.Time.UtcNow.AddDays(30);
        var recovery = Assert.Single(await harness.Service.ListMyClaimsAsync(
            harness.Identity,
            CancellationToken.None));
        Assert.Equal(SharedHeldOrderClaimStatus.Active, recovery.Status);
    }

    [Fact]
    public async Task Prepare_rejects_new_claim_when_hold_completed_and_replays_own_claim()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        var hold = await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None);
        var completed = hold! with
        {
            Status = SharedHeldOrderStatus.Completed,
            UpdatedAtUtc = harness.Time.UtcNow,
            Revision = hold.Revision + 1
        };
        Assert.True(await harness.Repository.TryUpdateHoldAsync(
            completed,
            hold.Revision,
            CancellationToken.None));

        var newClaim = SharedHeldOrderServiceTestSupport.PrepareRequest(
            claimGuid: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            idempotencyKey: "claim-after-completed");
        var denied = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.PrepareAsync(
                request.HoldGuid,
                newClaim,
                harness.Identity,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Mismatch, denied.Code);

        var replay = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(
                claimGuid: prepared.ClaimGuid,
                idempotencyKey: "claim-1"),
            harness.Identity,
            CancellationToken.None);
        Assert.True(replay.AlreadyExists);
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, replay.Status);
    }

    [Fact]
    public async Task Prepare_rejects_new_claim_when_hold_claimed_and_replays_own_claim()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var first = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ActivateAsync(
            request.HoldGuid,
            first.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        var otherDevice = SharedHeldOrderServiceTestSupport.Identity(
            storeCode: "S01",
            deviceCode: "POS-02");
        var busy = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.PrepareAsync(
                request.HoldGuid,
                SharedHeldOrderServiceTestSupport.PrepareRequest(
                    claimGuid: Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                    idempotencyKey: "claim-other-device"),
                otherDevice,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Busy, busy.Code);

        var denied = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.PrepareAsync(
                request.HoldGuid,
                SharedHeldOrderServiceTestSupport.PrepareRequest(
                    claimGuid: first.ClaimGuid,
                    idempotencyKey: "claim-1"),
                otherDevice,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.PermissionDenied, denied.Code);

        var replay = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(
                claimGuid: first.ClaimGuid,
                idempotencyKey: "claim-1"),
            harness.Identity,
            CancellationToken.None);
        Assert.True(replay.AlreadyExists);
        Assert.Equal(SharedHeldOrderClaimStatus.Active, replay.Status);
    }

    [Fact]
    public async Task Released_claim_prepare_replays_terminal_state_without_revive()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ReleaseAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);

        var replay = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(
                claimGuid: prepared.ClaimGuid,
                idempotencyKey: "claim-1"),
            harness.Identity,
            CancellationToken.None);

        Assert.True(replay.AlreadyExists);
        Assert.Equal(SharedHeldOrderClaimStatus.Released, replay.Status);
        Assert.Null(await harness.Repository.GetBlockingClaimAsync(
            request.HoldGuid,
            CancellationToken.None));

        // 新取单必须使用新 ClaimGuid。
        var fresh = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(
                claimGuid: Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111"),
                idempotencyKey: "claim-fresh"),
            harness.Identity,
            CancellationToken.None);
        Assert.False(fresh.AlreadyExists);
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, fresh.Status);
    }

    [Fact]
    public async Task Cross_device_cannot_activate_or_release_a_claim()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        var otherDevice = SharedHeldOrderServiceTestSupport.Identity(
            storeCode: "S01",
            deviceCode: "POS-02");

        var deniedActivate = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.ActivateAsync(
                request.HoldGuid,
                prepared.ClaimGuid,
                otherDevice,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.PermissionDenied, deniedActivate.Code);

        var deniedRelease = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.ReleaseAsync(
                request.HoldGuid,
                prepared.ClaimGuid,
                otherDevice,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.PermissionDenied, deniedRelease.Code);

        var hold = await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None);
        Assert.Equal(SharedHeldOrderStatus.Pending, hold?.Status);
    }

    [Fact]
    public async Task Cross_device_cannot_replay_active_or_released_claim_as_success()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ActivateAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);
        var otherDevice = SharedHeldOrderServiceTestSupport.Identity(
            storeCode: "S01",
            deviceCode: "POS-02");

        // Active 终态/幂等分支：另一设备不得读取成功。
        var deniedActiveReplay = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.ActivateAsync(
                request.HoldGuid,
                prepared.ClaimGuid,
                otherDevice,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.PermissionDenied, deniedActiveReplay.Code);

        // owner 释放到 Released 终态后，另一设备 replay release 同样被拒。
        await harness.Service.ReleaseAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);
        var deniedReleasedReplay = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.ReleaseAsync(
                request.HoldGuid,
                prepared.ClaimGuid,
                otherDevice,
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.PermissionDenied, deniedReleasedReplay.Code);

        var claim = await harness.Repository.GetClaimAsync(prepared.ClaimGuid, CancellationToken.None);
        Assert.Equal(SharedHeldOrderClaimStatus.Released, claim?.Status);
    }

    [Fact]
    public async Task Manual_percent_discount_uses_exact_decimal_math_at_extreme_line_values()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        // 999000015001 分 * 9999bps：纯 decimal 结果 998900114999.4999 -> AwayFromZero 998900114999；
        // 若经 double 除法会漂移成 998900115000（review 边界用例）。
        var cart = SharedHeldOrderServiceTestSupport.ValidCart() with
        {
            PricingState = SharedHeldOrderServiceTestSupport.ValidCart().PricingState with
            {
                Lines =
                [
                    SharedHeldOrderServiceTestSupport.ValidCart().PricingState.Lines[0] with
                    {
                        Quantity = 1m,
                        UnitPriceCents = 999_000_015_001,
                        DiscountState = new SharedLineDiscountStateV1(
                            SharedSaleCartV1Constants.DiscountModeManualPercent,
                            BasisPoints: 9999)
                    }
                ]
            }
        };
        await harness.Service.PublishAsync(
            SharedHeldOrderServiceTestSupport.PublishRequest(
                cart: cart,
                idempotencyKey: "publish-decimal-boundary"),
            harness.Identity,
            CancellationToken.None);

        var item = Assert.Single(await harness.Service.ListPendingAsync(
            harness.Identity,
            CancellationToken.None));
        Assert.Equal(999_000_015_001L, item.TotalCents);
        Assert.Equal(998_900_114_999L, item.DiscountCents);
        Assert.Equal(99_900_002L, item.ActualCents);
    }

    [Fact]
    public async Task Supervisor_force_release_can_cross_devices_in_same_store()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ActivateAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);
        var supervisor = SharedHeldOrderServiceTestSupport.Identity(
            storeCode: "S01",
            deviceCode: "POS-02",
            cashierId: "SUP-2",
            cashierName: "主管B",
            permissionCodes: ["Permissions.PosTerminal.History.Recall"],
            cashierUserGuid: "U-SUPERVISOR-2");

        var released = await harness.Service.ForceReleaseAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            new SharedHeldOrderForceReleaseRequest("主管跨设备强制释放"),
            supervisor,
            CancellationToken.None);

        Assert.Equal(SharedHeldOrderClaimStatus.Released, released.Status);
        Assert.True(released.ForceReleased);
        Assert.Equal("SUP-2", released.ForceReleaseCashierId);
        var hold = await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None);
        Assert.Equal(SharedHeldOrderStatus.Pending, hold?.Status);
    }

    [Fact]
    public async Task Atomic_claim_hold_transition_requires_both_revisions_to_match()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var prepared = await harness.Service.PrepareAsync(
            request.HoldGuid,
            SharedHeldOrderServiceTestSupport.PrepareRequest(),
            harness.Identity,
            CancellationToken.None);
        await harness.Service.ActivateAsync(
            request.HoldGuid,
            prepared.ClaimGuid,
            harness.Identity,
            CancellationToken.None);
        var claim = await harness.Repository.GetClaimAsync(prepared.ClaimGuid, CancellationToken.None);
        var hold = await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None);
        var now = harness.Time.UtcNow;
        var releasedClaim = claim! with
        {
            Status = SharedHeldOrderClaimStatus.Released,
            IsBlocking = false,
            ReleasedAtUtc = now,
            UpdatedAtUtc = now,
            Revision = claim.Revision + 1
        };
        var pendingHold = hold! with
        {
            Status = SharedHeldOrderStatus.Pending,
            UpdatedAtUtc = now,
            Revision = hold.Revision + 1
        };

        var staleClaim = await harness.Repository.TryUpdateClaimAndHoldAsync(
            releasedClaim,
            claim.Revision + 99,
            pendingHold,
            hold.Revision,
            CancellationToken.None);
        Assert.False(staleClaim);
        Assert.Equal(SharedHeldOrderClaimStatus.Active,
            (await harness.Repository.GetClaimAsync(prepared.ClaimGuid, CancellationToken.None))?.Status);
        Assert.Equal(SharedHeldOrderStatus.Claimed,
            (await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None))?.Status);

        var staleHold = await harness.Repository.TryUpdateClaimAndHoldAsync(
            releasedClaim,
            claim.Revision,
            pendingHold,
            hold.Revision + 99,
            CancellationToken.None);
        Assert.False(staleHold);
        Assert.Equal(SharedHeldOrderClaimStatus.Active,
            (await harness.Repository.GetClaimAsync(prepared.ClaimGuid, CancellationToken.None))?.Status);
        Assert.Equal(SharedHeldOrderStatus.Claimed,
            (await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None))?.Status);

        var applied = await harness.Repository.TryUpdateClaimAndHoldAsync(
            releasedClaim,
            claim.Revision,
            pendingHold,
            hold.Revision,
            CancellationToken.None);
        Assert.True(applied);
        Assert.Equal(SharedHeldOrderClaimStatus.Released,
            (await harness.Repository.GetClaimAsync(prepared.ClaimGuid, CancellationToken.None))?.Status);
        Assert.Equal(SharedHeldOrderStatus.Pending,
            (await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None))?.Status);
    }

    [Fact]
    public async Task Explicit_unsupported_versions_filter_to_empty_and_prepare_rejects_before_claim()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = SharedHeldOrderServiceTestSupport.PublishRequest();
        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);

        var list = await harness.Service.ListPendingAsync(
            harness.Identity,
            supportedPayloadVersions: [99],
            CancellationToken.None);
        Assert.Empty(list);

        var prepare = SharedHeldOrderServiceTestSupport.PrepareRequest();
        var exception = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.PrepareAsync(
                request.HoldGuid,
                prepare,
                harness.Identity,
                supportedPayloadVersions: [99],
                CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Invalid, exception.Code);
        Assert.Null(await harness.Repository.GetClaimAsync(prepare.ClaimGuid, CancellationToken.None));
    }

    [Fact]
    public void Capabilities_prefer_supported_version_and_keep_legacy_payload_version_one()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness(
            preferredPayloadVersion: SharedSaleCartV2Constants.PayloadVersion);

        var capabilities = harness.Service.GetCapabilities();

        Assert.Equal(SharedSaleCartV1Constants.PayloadVersion, capabilities.PayloadVersion);
        Assert.Equal([1, 2], capabilities.SupportedPayloadVersions);
        Assert.Equal(SharedSaleCartV2Constants.PayloadVersion, capabilities.PreferredPayloadVersion);
    }

    [Fact]
    public void Capabilities_rejects_preferred_version_outside_supported_set()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness(
            preferredPayloadVersion: 3);

        Assert.Throws<InvalidOperationException>(() => harness.Service.GetCapabilities());
    }

    [Fact]
    public async Task Publish_replay_does_not_confuse_v1_and_v2_for_the_same_hold()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var holdGuid = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var v1 = new SharedHeldOrderPublishRequest(
            holdGuid,
            "S01",
            "POS-01",
            SharedHeldOrderServiceTestSupport.ValidCart(),
            "mixed-version-hold");

        await harness.Service.PublishAsync(v1, harness.Identity, CancellationToken.None);

        var v2 = new SharedHeldOrderPublishRequest(
            holdGuid,
            "S01",
            "POS-01",
            SharedHeldOrderServiceTestSupport.ValidV2Cart(),
            "mixed-version-hold");
        var mismatch = await Assert.ThrowsAsync<SharedHeldOrderException>(() =>
            harness.Service.PublishAsync(v2, harness.Identity, CancellationToken.None));
        Assert.Equal(SharedHeldOrderErrorCodes.Mismatch, mismatch.Code);
    }

    [Fact]
    public async Task Publish_roundtrips_v2_catalog_baseline()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var request = new SharedHeldOrderPublishRequest(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "S01",
            "POS-01",
            SharedHeldOrderServiceTestSupport.ValidV2Cart(),
            "publish-v2");

        var response = await harness.Service.PublishAsync(
            request,
            harness.Identity,
            CancellationToken.None);

        Assert.False(response.AlreadyExists);
        var stored = await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(SharedSaleCartV2Constants.PayloadVersion, stored!.PayloadVersion);
        var decrypted = Assert.IsType<SharedSaleCartV2>(
            harness.Protector.Unprotect(stored.PayloadCiphertext, stored.PayloadVersion));
        Assert.Equal(500, decrypted.PricingState.Lines[0].CatalogDiscountBasisPoints);
    }

    [Fact]
    public async Task V2_manual_amount_zero_keeps_explicit_zero_summary_despite_catalog_baseline()
    {
        var harness = SharedHeldOrderServiceTestSupport.CreateHarness();
        var cart = SharedHeldOrderServiceTestSupport.ValidV2Cart();
        var line = Assert.Single(cart.PricingState.Lines);
        var request = new SharedHeldOrderPublishRequest(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "S01",
            "POS-01",
            cart with
            {
                PricingState = cart.PricingState with
                {
                    Lines = [line with
                    {
                        DiscountState = new SharedLineDiscountStateV1(
                            SharedSaleCartV1Constants.DiscountModeManualAmount,
                            Cents: 0)
                    }]
                }
            },
            "v2-manual-zero");

        await harness.Service.PublishAsync(request, harness.Identity, CancellationToken.None);
        var item = Assert.Single(await harness.Service.ListPendingAsync(
            harness.Identity,
            supportedPayloadVersions: [1, 2],
            CancellationToken.None));
        var stored = await harness.Repository.GetHoldAsync(request.HoldGuid, CancellationToken.None);

        Assert.Equal(0L, stored!.DiscountCents);
        Assert.Equal(3000L, stored.ActualCents);
        Assert.Equal(3000L, item.TotalCents);
        Assert.Equal(0L, item.DiscountCents);
        Assert.Equal(3000L, item.ActualCents);
    }
}
