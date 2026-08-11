using System.Net;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.HeldOrders;
using static Hbpos.Client.Tests.SharedHeldOrderClientTestSupport;

namespace Hbpos.Client.Tests;

/// <summary>
/// 后台 publication worker：NeedsEvaluation -> PendingPublish（密文）-> Published（远端
/// revision/time 原子落库）；API 不可用/disabled 走现有 backoff 且绝不影响本地挂单；
/// return/open-item/无法冻结促销规则 fail-closed Blocked；发布幂等（同 holdGuid 同 key 重试）。
/// </summary>
public sealed class SharedHeldOrderPublicationWorkerTests
{
    [Fact]
    public async Task RunOnceAsync_evaluates_new_hold_to_pending_publish_and_offline_api_keeps_hold()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var store = new SuspendedOrderRepository(scope.Store);
        var order = await SaveSampleOrderAsync(store);
        var api = new StubSharedHeldOrderApiClient
        {
            Capabilities = _ => throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Retryable,
                "network down",
                null,
                HttpStatusCode.ServiceUnavailable),
            Publish = (_, _) => throw new InvalidOperationException("publish must not be called when capability check fails")
        };
        var worker = CreateWorker(scope, api);

        var result = await worker.RunOnceAsync("S001", "POS-01");

        Assert.Equal(1, result.StagedPendingPublish);
        Assert.Equal(0, result.Published);
        Assert.Equal(1, result.FailedCapability);
        var publication = await scope.Repository.GetPublicationAsync(order.SuspendedOrderGuid);
        Assert.NotNull(publication);
        Assert.Equal(SharedHeldOrderPublicationStatus.PendingPublish, publication!.Status);
        Assert.Equal(1, publication.RetryCount);
        Assert.NotNull(publication.NextAttemptAtIso);
        Assert.NotNull(publication.PayloadCiphertext);
        // 本地挂单仍 Pending，API 不可用不影响本机数据。
        var pending = await store.GetPendingAsync("S001");
        Assert.Contains(pending, summary => summary.SuspendedOrderGuid == order.SuspendedOrderGuid);
    }

    [Fact]
    public async Task RunOnceAsync_publishes_when_enabled_and_persists_remote_revision_and_time()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var store = new SuspendedOrderRepository(scope.Store);
        var order = await SaveSampleOrderAsync(store);
        var publishedRequest = new List<SharedHeldOrderPublishRequest>();
        var api = new StubSharedHeldOrderApiClient
        {
            Capabilities = _ => Task.FromResult(new SharedHeldOrderCapabilitiesResponse(
                Enabled: true,
                PayloadVersion: 1,
                PreparedTtlSeconds: 120,
                ForceReleaseSupported: true)),
            Publish = (request, _) =>
            {
                publishedRequest.Add(request);
                return Task.FromResult(new SharedHeldOrderPublishResponse(
                    request.HoldGuid,
                    SharedHeldOrderStatus.Pending,
                    Revision: 7L,
                    new DateTimeOffset(2026, 7, 28, 1, 2, 3, TimeSpan.Zero)));
            }
        };
        var worker = CreateWorker(scope, api);

        var first = await worker.RunOnceAsync("S001", "POS-01");
        var second = await worker.RunOnceAsync("S001", "POS-01");

        Assert.Equal(1, first.Published);
        Assert.Equal(0, second.Published);
        var request = Assert.Single(publishedRequest);

        var publication = await scope.Repository.GetPublicationAsync(order.SuspendedOrderGuid);
        Assert.NotNull(publication);
        Assert.Equal(SharedHeldOrderPublicationStatus.Published, publication!.Status);
        Assert.Equal(7L, publication.RemoteRevision);
        Assert.Equal("2026-07-28T01:02:03.000Z", publication.RemoteUpdatedAtIso);
        Assert.Equal(0, publication.RetryCount);
        Assert.Equal(order.SuspendedOrderGuid, request.HoldGuid);
        Assert.Equal(order.SuspendedOrderGuid.ToString("D"), request.IdempotencyKey);
    }

    [Fact]
    public async Task RunOnceAsync_records_backoff_when_publish_fails_and_retries_with_same_key()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var store = new SuspendedOrderRepository(scope.Store);
        var order = await SaveSampleOrderAsync(store);
        var attempts = 0;
        var api = new StubSharedHeldOrderApiClient
        {
            Capabilities = _ => Task.FromResult(EnabledCapabilities()),
            Publish = (request, _) =>
            {
                attempts++;
                return attempts == 1
                    ? throw new SharedHeldOrderApiException(
                        SharedHeldOrderApiErrorKind.Retryable,
                        "SHARED_HELD_ORDER_BUSY",
                        "SHARED_HELD_ORDER_BUSY",
                        HttpStatusCode.Conflict)
                    : Task.FromResult(new SharedHeldOrderPublishResponse(
                        request.HoldGuid,
                        SharedHeldOrderStatus.Pending,
                        Revision: 3L,
                        new DateTimeOffset(2026, 7, 28, 2, 0, 0, TimeSpan.Zero)));
            }
        };
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero));
        var worker = CreateWorker(scope, api, timeProvider: time);

        var failed = await worker.RunOnceAsync("S001", "POS-01");

        Assert.Equal(1, failed.FailedPublish);
        var publication = await scope.Repository.GetPublicationAsync(order.SuspendedOrderGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.PendingPublish, publication!.Status);
        Assert.Equal(1, publication.RetryCount);
        Assert.Equal("SHARED_HELD_ORDER_BUSY", publication.ErrorCode);
        Assert.NotNull(publication.NextAttemptAtIso);
        Assert.True(string.CompareOrdinal(publication.NextAttemptAtIso, publication.LastAttemptAtIso) > 0);

        time.Now = new DateTimeOffset(2026, 7, 28, 3, 2, 0, TimeSpan.Zero);
        var retried = await worker.RunOnceAsync("S001", "POS-01");

        Assert.Equal(1, retried.Published);
        Assert.Equal(2, attempts);
        var published = await scope.Repository.GetPublicationAsync(order.SuspendedOrderGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.Published, published!.Status);
        Assert.Equal(3L, published.RemoteRevision);
        Assert.Equal(1, published.RetryCount);
        Assert.Null(published.ErrorCode);
    }

    [Fact]
    public async Task RunOnceAsync_disabled_capability_records_backoff_and_keeps_hold()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var store = new SuspendedOrderRepository(scope.Store);
        var order = await SaveSampleOrderAsync(store);
        var api = new StubSharedHeldOrderApiClient
        {
            Capabilities = _ => Task.FromResult(new SharedHeldOrderCapabilitiesResponse(
                Enabled: false,
                PayloadVersion: 1,
                PreparedTtlSeconds: 120,
                ForceReleaseSupported: true)),
            Publish = (_, _) => throw new InvalidOperationException("publish must not run while disabled")
        };
        var worker = CreateWorker(scope, api);

        var result = await worker.RunOnceAsync("S001", "POS-01");

        Assert.Equal(1, result.FailedCapability);
        Assert.Equal(0, result.Published);
        var publication = await scope.Repository.GetPublicationAsync(order.SuspendedOrderGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.PendingPublish, publication!.Status);
        Assert.Equal("SHARED_HELD_ORDER_DISABLED", publication.ErrorCode);
        Assert.Equal(1, publication.RetryCount);
        var pending = await store.GetPendingAsync("S001");
        Assert.Contains(pending, summary => summary.SuspendedOrderGuid == order.SuspendedOrderGuid);
    }

    [Fact]
    public async Task RunOnceAsync_promotion_hold_without_frozen_rules_fails_closed_blocked()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var store = new SuspendedOrderRepository(scope.Store);
        var order = await SavePromotionOrderAsync(store);
        var api = new StubSharedHeldOrderApiClient
        {
            Capabilities = _ => Task.FromResult(EnabledCapabilities()),
            Publish = (_, _) => throw new InvalidOperationException("blocked orders must not publish")
        };
        var worker = CreateWorker(scope, api);

        var result = await worker.RunOnceAsync("S001", "POS-01");

        Assert.Equal(1, result.Blocked);
        var publication = await scope.Repository.GetPublicationAsync(order.SuspendedOrderGuid);
        Assert.NotNull(publication);
        Assert.Equal(SharedHeldOrderPublicationStatus.Blocked, publication!.Status);
        Assert.Equal(SharedHeldOrderMappingReasons.PromotionRulesMissing, publication.ErrorCode);
        Assert.DoesNotContain("Product", publication.ErrorMessage ?? string.Empty);
        var pending = await store.GetPendingAsync("S001");
        Assert.Contains(pending, summary => summary.SuspendedOrderGuid == order.SuspendedOrderGuid);
    }

    [Fact]
    public async Task RunOnceAsync_return_and_open_item_holds_are_blocked_with_reasons()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var store = new SuspendedOrderRepository(scope.Store);
        var returnOrder = await SaveReturnOrderAsync(store);
        var openItemOrder = await SaveOpenItemOrderAsync(store);
        var api = new StubSharedHeldOrderApiClient
        {
            Capabilities = _ => Task.FromResult(EnabledCapabilities()),
            Publish = (_, _) => throw new InvalidOperationException("blocked orders must not publish")
        };
        var worker = CreateWorker(scope, api);

        var result = await worker.RunOnceAsync("S001", "POS-01");

        Assert.Equal(2, result.Blocked);
        var returnPublication = await scope.Repository.GetPublicationAsync(returnOrder.SuspendedOrderGuid);
        Assert.Equal(SharedHeldOrderMappingReasons.ReturnLine, returnPublication!.ErrorCode);
        var openItemPublication = await scope.Repository.GetPublicationAsync(openItemOrder.SuspendedOrderGuid);
        Assert.Equal(SharedHeldOrderMappingReasons.OpenItemLine, openItemPublication!.ErrorCode);
    }

    [Fact]
    public async Task RunOnceAsync_frozen_rule_mismatch_blocks_with_mismatch_reason()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var store = new SuspendedOrderRepository(scope.Store);
        var order = await SavePromotionOrderAsync(store);
        var api = new StubSharedHeldOrderApiClient
        {
            Capabilities = _ => Task.FromResult(EnabledCapabilities()),
            Publish = (_, _) => throw new InvalidOperationException("blocked orders must not publish")
        };
        // 提供一套与挂单折扣不一致的规则：映射器重演后应 fail-closed Blocked。
        var worker = CreateWorker(
            scope,
            api,
            frozenPromotionRuleProvider: _ => new[]
            {
                new CatalogPromotionRuleDto(
                    "PROMO-OTHER",
                    "Other Promotion",
                    IsExclusive: false,
                    Priority: 10,
                    ApplyQuantity: 2,
                    FixedPrice: 8m,
                    MaxApplicationsPerOrder: 1,
                    EffectiveStart: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                    EffectiveEnd: new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
                    UpdatedAt: null,
                    Products: [new CatalogPromotionProductDto("P-1", 1)])
            });

        var result = await worker.RunOnceAsync("S001", "POS-01");

        Assert.Equal(1, result.Blocked);
        var publication = await scope.Repository.GetPublicationAsync(order.SuspendedOrderGuid);
        Assert.Equal(SharedHeldOrderMappingReasons.PromotionRulesMismatch, publication!.ErrorCode);
    }

    [Fact]
    public async Task RunOnceAsync_backfills_legacy_publication_with_order_device_code()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var holdGuid = Guid.NewGuid();
        // 旧库挂单（无 publication 行）直接经 SQL 播种，设备来源是 POS-09。
        await using (var connection = await scope.Store.OpenConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO SuspendedOrders (
                    SuspendedOrderGuid, StoreCode, DeviceCode, CashierId, CashierName, SuspendedAt,
                    TotalAmount, DiscountAmount, ActualAmount, Status)
                VALUES ($HoldGuid, 'S001', 'POS-09', 'cashier-1', 'Cashier One',
                        '2026-07-28T00:00:00+00:00', '11.00', '0.00', '11.00', 0);
                """;
            command.Parameters.AddWithValue("$HoldGuid", holdGuid.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        var api = new StubSharedHeldOrderApiClient
        {
            Capabilities = _ => throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Retryable,
                "network down",
                null,
                HttpStatusCode.ServiceUnavailable)
        };
        var worker = CreateWorker(scope, api);

        var result = await worker.RunOnceAsync("S001", "POS-01");

        Assert.Equal(1, result.StagedPendingPublish);
        var publication = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.NotNull(publication);
        Assert.Equal("POS-09", publication!.DeviceCode);
        Assert.NotEqual("POS-01", publication.DeviceCode);
        Assert.NotEqual(string.Empty, publication.DeviceCode);
    }

    [Fact]
    public async Task RunOnceAsync_never_publishes_due_rows_from_another_store_or_device_scope()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var suspendedOrders = new SuspendedOrderRepository(scope.Store);
        var foreign = SampleOrder(
            CartLineKind.Sale,
            PosCartLineDiscountSource.None,
            discountAmount: 0m,
            storeCode: "S002",
            deviceCode: "POS-02");
        await suspendedOrders.SaveAsync(foreign);
        var publication = await scope.Repository.GetPublicationAsync(foreign.SuspendedOrderGuid);
        Assert.NotNull(publication);
        Assert.True(await scope.Repository.TryStagePendingPublishAsync(
            foreign.SuspendedOrderGuid,
            publication!.Revision,
            SampleCanonical(),
            "2026-07-28T03:00:00.000Z"));

        var published = new List<SharedHeldOrderPublishRequest>();
        var api = new StubSharedHeldOrderApiClient
        {
            Capabilities = _ => Task.FromResult(EnabledCapabilities()),
            Publish = (request, _) =>
            {
                published.Add(request);
                return Task.FromResult(new SharedHeldOrderPublishResponse(
                    request.HoldGuid,
                    SharedHeldOrderStatus.Pending,
                    1,
                    new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero)));
            }
        };

        var result = await CreateWorker(scope, api).RunOnceAsync("S001", "POS-01");

        Assert.Equal(0, result.Published);
        Assert.Empty(published);
        Assert.Equal(
            SharedHeldOrderPublicationStatus.PendingPublish,
            (await scope.Repository.GetPublicationAsync(foreign.SuspendedOrderGuid))!.Status);
    }

    [Fact]
    public async Task RunOnceAsync_invalid_legacy_snapshot_is_blocked_without_stopping_the_batch()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var suspendedOrders = new SuspendedOrderRepository(scope.Store);
        var invalid = SampleOrder(
            CartLineKind.Sale,
            PosCartLineDiscountSource.None,
            discountAmount: 0m);
        invalid = invalid with
        {
            // 正小数是称重商品的合法 frozen quantity；零才是稳定的无效快照。
            Lines = [invalid.Lines[0] with { Quantity = 0m }]
        };
        await suspendedOrders.SaveAsync(invalid);

        var api = new StubSharedHeldOrderApiClient
        {
            Capabilities = _ => Task.FromResult(EnabledCapabilities()),
            Publish = (_, _) => throw new InvalidOperationException("invalid hold must not publish")
        };

        var result = await CreateWorker(scope, api).RunOnceAsync("S001", "POS-01");

        Assert.Equal(1, result.Blocked);
        var publication = await scope.Repository.GetPublicationAsync(invalid.SuspendedOrderGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.Blocked, publication!.Status);
        Assert.Equal(SharedHeldOrderMappingReasons.InvalidSnapshot, publication.ErrorCode);
    }

    private static SharedHeldOrderPublicationWorker CreateWorker(
        RepositoryScope scope,
        StubSharedHeldOrderApiClient api,
        Func<SuspendedOrder, IReadOnlyList<CatalogPromotionRuleDto>?>? frozenPromotionRuleProvider = null,
        FixedTimeProvider? timeProvider = null)
    {
        return new SharedHeldOrderPublicationWorker(
            scope.Repository,
            new SharedHeldOrderMapper(),
            api,
            frozenPromotionRuleProvider,
            timeProvider ?? new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero)));
    }

    private static SharedHeldOrderCapabilitiesResponse EnabledCapabilities()
    {
        return new SharedHeldOrderCapabilitiesResponse(
            Enabled: true,
            PayloadVersion: 1,
            PreparedTtlSeconds: 120,
            ForceReleaseSupported: true);
    }

    private static async Task<SuspendedOrder> SaveSampleOrderAsync(SuspendedOrderRepository repository)
    {
        var order = SampleOrder(CartLineKind.Sale, PosCartLineDiscountSource.None, discountAmount: 0m);
        await repository.SaveAsync(order);
        return order;
    }

    private static async Task<SuspendedOrder> SavePromotionOrderAsync(SuspendedOrderRepository repository)
    {
        var order = SampleOrder(
            CartLineKind.Sale,
            PosCartLineDiscountSource.Promotion,
            discountAmount: 4m);
        await repository.SaveAsync(order);
        return order;
    }

    private static async Task<SuspendedOrder> SaveReturnOrderAsync(SuspendedOrderRepository repository)
    {
        var order = SampleOrder(CartLineKind.Return, PosCartLineDiscountSource.None, discountAmount: 0m);
        await repository.SaveAsync(order);
        return order;
    }

    private static async Task<SuspendedOrder> SaveOpenItemOrderAsync(SuspendedOrderRepository repository)
    {
        var order = SampleOrder(CartLineKind.OpenItem, PosCartLineDiscountSource.None, discountAmount: 0m);
        await repository.SaveAsync(order);
        return order;
    }

    private static SuspendedOrder SampleOrder(
        CartLineKind kind,
        PosCartLineDiscountSource discountSource,
        decimal discountAmount,
        string storeCode = "S001",
        string deviceCode = "POS-01")
    {
        var orderGuid = Guid.NewGuid();
        return new SuspendedOrder(
            orderGuid,
            storeCode,
            deviceCode,
            "cashier-1",
            "Cashier One",
            new DateTimeOffset(2026, 7, 28, 1, 0, 0, TimeSpan.Zero),
            22m,
            4m,
            18m,
            SuspendedOrderStatus.Pending,
            [
                new SuspendedOrderLine(
                    Guid.NewGuid(),
                    orderGuid,
                    storeCode,
                    "P-1",
                    "REF-1",
                    "Product 1",
                    "CODE-1",
                    "ITEM-1",
                    null,
                    2m,
                    11m,
                    discountAmount,
                    null,
                    22m - discountAmount,
                    PriceSourceKind.ProductBase,
                    "Product Base",
                    discountSource)
                {
                    Kind = kind,
                    ReturnSourceKey = kind == CartLineKind.Return ? "RETURN-1" : string.Empty
                }
            ]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}
