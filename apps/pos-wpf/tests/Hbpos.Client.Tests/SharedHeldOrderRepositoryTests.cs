using System.Text;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

/// <summary>
/// 新 repository：旧挂单评估、publication CAS（revision 递增 / Upsert 只写初态）、
/// 并发 fence 单赢家、HoldGuid/Source 持久化，以及
/// prepare/activate/bind/complete/release 幂等语义；payload 仅以密文落库。
/// </summary>
public sealed class SharedHeldOrderRepositoryTests
{
    private sealed class PrefixPayloadProtector : ISharedHeldOrderPayloadProtector
    {
        private static readonly byte[] Prefix = "enc:"u8.ToArray();

        public byte[] Protect(byte[] plaintext)
        {
            return [.. Prefix, .. plaintext];
        }

        public byte[] Unprotect(byte[] ciphertext)
        {
            Assert.StartsWith("enc:", Encoding.UTF8.GetString(ciphertext));
            return ciphertext[Prefix.Length..];
        }
    }

    private sealed class PayloadSerializer : ISharedHeldOrderPayloadSerializer
    {
        private static readonly ISharedHeldOrderCanonicalSerializer Canonical =
            new SharedHeldOrderCanonicalJsonSerializer();

        public byte[] Serialize(SharedHeldOrderCanonicalPayload payload)
        {
            return Encoding.UTF8.GetBytes(Canonical.Serialize(payload));
        }

        public SharedHeldOrderCanonicalPayload Deserialize(byte[] data)
        {
            return Canonical.Deserialize(Encoding.UTF8.GetString(data));
        }
    }

    private sealed class RepositoryScope : IAsyncDisposable
    {
        public RepositoryScope(string databasePath)
        {
            DatabasePath = databasePath;
            Store = new LocalSqliteStore(databasePath);
            Schema = new LocalSchemaService(Store);
            Repository = new SharedHeldOrderRepository(
                Store,
                new PrefixPayloadProtector(),
                new PayloadSerializer());
        }

        public string DatabasePath { get; }

        public LocalSqliteStore Store { get; }

        public LocalSchemaService Schema { get; }

        public SharedHeldOrderRepository Repository { get; }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { DatabasePath, $"{DatabasePath}-wal", $"{DatabasePath}-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private static async Task<RepositoryScope> CreateScopeAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-shared-held-{Guid.NewGuid():N}.db");
        var scope = new RepositoryScope(databasePath);
        await scope.Schema.InitializeAsync();
        return scope;
    }

    private static SharedHeldOrderCanonicalPayload SamplePayload(int revision = 1)
    {
        return new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                revision,
                "sale",
                "2026-07-28T00:00:00.000Z",
                [],
                [
                    new SharedHeldOrderPricingLine(
                        "line-1",
                        "P-1",
                        null,
                        "CODE-1",
                        "Product 1",
                        1,
                        1100,
                        "catalog",
                        null,
                        "sale",
                        null,
                        null,
                        null,
                        new SharedHeldOrderDiscountState("none"))
                ]));
    }

    private static SharedHeldOrderClaimDraft Draft(
        Guid claimId,
        string prepareKey = "idem-prepare",
        Guid? holdGuid = null,
        string storeCode = "S001",
        string deviceCode = "POS-01",
        SharedHeldOrderClaimSource source = SharedHeldOrderClaimSource.OfflineOrigin,
        string? expiresAtIso = null)
    {
        return new SharedHeldOrderClaimDraft(
            claimId,
            holdGuid ?? Guid.NewGuid(),
            storeCode,
            deviceCode,
            source,
            prepareKey,
            SamplePayload(),
            "2026-07-28T00:00:00.000Z",
            expiresAtIso);
    }

    private static async Task InsertLegacyOrderAsync(
        RepositoryScope scope,
        Guid orderGuid,
        string storeCode = "S001",
        string deviceCode = "POS-01")
    {
        await using var connection = await scope.Store.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SuspendedOrders (
                SuspendedOrderGuid, StoreCode, DeviceCode, CashierId, CashierName, SuspendedAt,
                TotalAmount, DiscountAmount, ActualAmount, Status)
            VALUES ($OrderGuid, $StoreCode, $DeviceCode, 'cashier-1', 'Cashier One',
                    '2026-07-28T00:00:00+00:00', '11.00', '0.00', '11.00', 0);

            INSERT INTO SuspendedOrderLines (
                SuspendedOrderLineGuid, SuspendedOrderGuid, StoreCode, ProductCode, ReferenceCode,
                DisplayName, LookupCode, ItemNumber, ProductImage, Quantity, UnitPrice, DiscountAmount,
                DiscountPercent, IsAutomaticPromotionDiscount, DiscountSource, ActualAmount, PriceSource,
                PriceSourceLabel, Kind, ReturnSourceKey, OriginalOrderGuid, OriginalOrderDetailGuid, ReturnReason)
            VALUES (
                $LineGuid, $OrderGuid, $StoreCode, 'P-1', NULL, 'Product 1', 'CODE-1', NULL, NULL,
                '1', '11.00', '0.00', NULL, 0, 0, '11.00', 0, 'ProductBase', 0, '', NULL, NULL, NULL);
            """;
        command.Parameters.AddWithValue("$OrderGuid", orderGuid.ToString("D"));
        command.Parameters.AddWithValue("$LineGuid", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$StoreCode", storeCode);
        command.Parameters.AddWithValue("$DeviceCode", deviceCode);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> UpsertNeedsEvaluationAsync(
        RepositoryScope scope,
        Guid holdGuid,
        byte[]? payload = null,
        string? errorCode = null,
        string? errorMessage = null)
    {
        return await scope.Repository.UpsertPublicationAsync(
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderPublicationStatus.NeedsEvaluation,
            payload,
            "2026-07-28T00:00:00.000Z",
            "2026-07-28T00:00:00.000Z",
            "2026-07-28T00:00:00.000Z",
            errorCode,
            errorMessage);
    }

    [Fact]
    public async Task List_legacy_orders_needing_evaluation_requires_share_request_and_excludes_active_states()
    {
        await using var scope = await CreateScopeAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var untouched = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, first);
        await InsertLegacyOrderAsync(scope, second);
        await InsertLegacyOrderAsync(scope, third);
        await InsertLegacyOrderAsync(scope, untouched);

        // 默认不评估发布：无 publication 行或未请求的 NeedsEvaluation 都不进入候选。
        Assert.Empty(await scope.Repository.ListLegacyOrdersNeedingEvaluationAsync("S001"));
        Assert.True(await UpsertNeedsEvaluationAsync(scope, first, [1, 2, 3]));
        Assert.Empty(await scope.Repository.ListLegacyOrdersNeedingEvaluationAsync("S001"));

        // 显式请求共享后才进入评估候选（旧缺 publication 行由 request 入口补 NeedsEvaluation+requested）。
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            first, "S001", "POS-01", "2026-07-28T00:00:00.000Z"));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            second, "S001", "POS-01", "2026-07-28T00:00:00.000Z"));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            third, "S001", "POS-01", "2026-07-28T00:00:00.000Z"));
        Assert.Equal(3, (await scope.Repository.ListLegacyOrdersNeedingEvaluationAsync("S001")).Count);

        // PendingPublish 与 Published 不再进入评估候选。
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            first, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.PendingPublish, "2026-07-28T00:00:01.000Z"));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            second, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.PendingPublish, "2026-07-28T00:00:01.000Z"));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            second, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T00:00:02.000Z", remoteRevision: 5L, remoteUpdatedAtIso: "2026-07-28T00:00:02.000Z"));
        var remaining = await scope.Repository.ListLegacyOrdersNeedingEvaluationAsync("S001");
        var remainingOrder = Assert.Single(remaining);
        Assert.Equal(third, remainingOrder.SuspendedOrderGuid);

        // Blocked 同样不进入 re-evaluation。
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            third, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.Blocked,
            "2026-07-28T00:00:01.000Z", "PromotionRulesMissing", "缺少冻结促销规则"));
        Assert.Empty(await scope.Repository.ListLegacyOrdersNeedingEvaluationAsync("S001"));
    }

    [Fact]
    public async Task TryRequestShareAsync_requires_pending_order_and_exact_store_device()
    {
        await using var scope = await CreateScopeAsync();
        var missing = Guid.NewGuid();
        Assert.Equal(SharedHeldOrderShareRequestResult.NotFound, await scope.Repository.TryRequestShareAsync(
            missing, "S001", "POS-01", "2026-07-28T00:00:00.000Z"));

        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);

        // store/device 必须精确匹配（大小写不敏感规范化）。
        Assert.Equal(SharedHeldOrderShareRequestResult.Ineligible, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S999", "POS-01", "2026-07-28T00:00:00.000Z"));
        Assert.Equal(SharedHeldOrderShareRequestResult.Ineligible, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-99", "2026-07-28T00:00:00.000Z"));

        // 非 Pending 状态不可共享。
        await new SuspendedOrderRepository(scope.Store).MarkStatusAsync(holdGuid, SuspendedOrderStatus.Recalled);
        Assert.Equal(SharedHeldOrderShareRequestResult.Ineligible, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-07-28T00:00:01.000Z"));
    }

    [Fact]
    public async Task TryRequestShareAsync_creates_requested_needs_evaluation_for_legacy_order_without_publication()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);

        var result = await scope.Repository.TryRequestShareAsync(
            holdGuid, "s001", "pos-01", "2026-07-28T00:00:00.000Z");

        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, result);
        var publication = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.NotNull(publication);
        Assert.Equal(SharedHeldOrderPublicationStatus.NeedsEvaluation, publication!.Status);
        Assert.Equal("2026-07-28T00:00:00.000Z", publication.ShareRequestedAtIso);
        Assert.Equal(1, publication.Revision);
        Assert.Equal(0, publication.RetryCount);
        Assert.Null(publication.PayloadCiphertext);
    }

    [Fact]
    public async Task TryRequestShareAsync_is_idempotent_and_only_writes_request_time_for_existing_needs_evaluation()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid, [1, 2, 3]));

        var first = await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-07-28T00:00:01.000Z");
        var second = await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-07-28T00:00:02.000Z");

        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, first);
        Assert.Equal(SharedHeldOrderShareRequestResult.AlreadyRequested, second);
        var publication = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.NotNull(publication);
        Assert.Equal(SharedHeldOrderPublicationStatus.NeedsEvaluation, publication!.Status);
        Assert.Equal(1, publication.Revision);
        Assert.Equal("2026-07-28T00:00:01.000Z", publication.ShareRequestedAtIso);
        Assert.NotNull(publication.PayloadCiphertext);
    }

    [Fact]
    public async Task List_due_publications_and_stage_and_block_require_share_request()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));

        // 未请求：不能暂存 PendingPublish、不能普通 Blocked，也不进 due。
        Assert.False(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid, 1, SamplePayload(), "2026-07-28T00:00:01.000Z"));
        Assert.False(await scope.Repository.TryBlockPublicationAsync(
            holdGuid, 1, "ReturnLineNotSupported", "detail", "2026-07-28T00:00:01.000Z"));
        Assert.Empty(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:00:02.000Z"));

        // 请求后：可暂存、可进入 due。
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-07-28T00:00:03.000Z"));
        var publication = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.NotNull(publication);
        Assert.True(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid, publication!.Revision, SamplePayload(), "2026-07-28T00:00:04.000Z"));
        Assert.Equal(
            holdGuid,
            Assert.Single(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:00:05.000Z")).LocalHoldGuid);

        // 请求后：可普通 Blocked（评估阻断）。
        var second = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, second);
        Assert.True(await UpsertNeedsEvaluationAsync(scope, second));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            second, "S001", "POS-01", "2026-07-28T00:00:03.000Z"));
        var secondPublication = await scope.Repository.GetPublicationAsync(second);
        Assert.NotNull(secondPublication);
        Assert.True(await scope.Repository.TryBlockPublicationAsync(
            second, secondPublication!.Revision, "ReturnLineNotSupported", "detail", "2026-07-28T00:00:04.000Z"));
        Assert.Equal(
            SharedHeldOrderPublicationStatus.Blocked,
            (await scope.Repository.GetPublicationAsync(second))!.Status);
    }

    [Fact]
    public async Task Try_advance_publication_cas_allows_only_legal_transitions_and_published_is_terminal()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid, [1, 2, 3]));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-07-28T00:00:00.000Z"));

        // 越级迁移或错误 revision 都不得推进。
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.Published, "2026-07-28T00:00:01.000Z"));
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 1, SharedHeldOrderPublicationStatus.Published, "2026-07-28T00:00:01.000Z"));
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 9, SharedHeldOrderPublicationStatus.PendingPublish, "2026-07-28T00:00:01.000Z"));

        // NeedsEvaluation -> PendingPublish 后 revision 必须 +1，并进入 due 队列。
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.PendingPublish, "2026-07-28T00:00:02.000Z"));
        var pending = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.PendingPublish, pending!.Status);
        Assert.Equal(2, pending.Revision);
        Assert.Equal(0, pending.RetryCount);
        Assert.Null(pending.NextAttemptAtIso);
        Assert.Equal(holdGuid, Assert.Single(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:00:03.000Z")).LocalHoldGuid);

        // PendingPublish -> Published 为终态；revision 继续 +1；
        // 缺少服务端 remote 字段的 Published 一律拒绝；之后任何迁移都失败。
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T00:00:03.000Z"));
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T00:00:03.000Z", remoteRevision: -1L, remoteUpdatedAtIso: "2026-07-28T00:00:03.000Z"));
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T00:00:03.000Z", remoteRevision: 1L, remoteUpdatedAtIso: "   "));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T00:00:03.000Z", remoteRevision: 9L, remoteUpdatedAtIso: "2026-07-28T00:00:03.000Z"));
        var published = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.Published, published!.Status);
        Assert.Equal(3, published.Revision);
        Assert.Equal(9L, published.RemoteRevision);
        Assert.Equal("2026-07-28T00:00:03.000Z", published.RemoteUpdatedAtIso);
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.Published, 3, SharedHeldOrderPublicationStatus.Blocked, "2026-07-28T00:00:04.000Z"));
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.Published, 3, SharedHeldOrderPublicationStatus.NeedsEvaluation, "2026-07-28T00:00:04.000Z"));
        Assert.Empty(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:10:00.000Z"));

        // Published 绝不能被 Upsert 重开。
        Assert.False(await UpsertNeedsEvaluationAsync(scope, holdGuid));
        Assert.Equal(SharedHeldOrderPublicationStatus.Published, (await scope.Repository.GetPublicationAsync(holdGuid))!.Status);
        Assert.Equal(3, (await scope.Repository.GetPublicationAsync(holdGuid))!.Revision);
    }

    [Fact]
    public async Task Publication_failure_stays_pending_publish_revision_increments_and_backs_off()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid, [1, 2, 3]));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-07-28T00:00:00.000Z"));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.PendingPublish, "2026-07-28T00:00:01.000Z"));

        // 第 1 次失败：保持 PendingPublish、revision 与 RetryCount 都 +1；
        // 未提供尝试时间时按失败时间最小补齐（30s 退避）。
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.PendingPublish,
            "2026-07-28T00:00:02.000Z", "HttpTimeout", "上游超时"));
        var afterFirst = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.PendingPublish, afterFirst!.Status);
        Assert.Equal(3, afterFirst.Revision);
        Assert.Equal(1, afterFirst.RetryCount);
        Assert.Equal("2026-07-28T00:00:02.000Z", afterFirst.LastAttemptAtIso);
        Assert.Equal("2026-07-28T00:00:32.000Z", afterFirst.NextAttemptAtIso);

        // 退避期未到不出 due；到期后重新出现。
        Assert.Empty(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:00:31.000Z"));
        Assert.Equal(holdGuid, Assert.Single(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:00:32.000Z")).LocalHoldGuid);

        // 第 2 次失败按显式提供的下次时间记录，revision 与 RetryCount 继续 +1。
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 3, SharedHeldOrderPublicationStatus.PendingPublish,
            "2026-07-28T00:01:00.000Z", "HttpTimeout", "上游超时",
            "2026-07-28T00:01:00.000Z", "2026-07-28T00:02:00.000Z"));
        var afterSecond = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.Equal(4, afterSecond!.Revision);
        Assert.Equal(2, afterSecond.RetryCount);
        Assert.Equal("2026-07-28T00:01:00.000Z", afterSecond.LastAttemptAtIso);
        Assert.Equal("2026-07-28T00:02:00.000Z", afterSecond.NextAttemptAtIso);
        Assert.Empty(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:01:59.000Z"));
        Assert.Single(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:02:00.000Z"));
    }

    [Fact]
    public async Task Due_publications_exclude_holds_with_open_offline_origin_claim()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        var payload = SamplePayload();
        await InsertLegacyOrderAsync(scope, holdGuid);
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));
        Assert.Equal(
            SharedHeldOrderShareRequestResult.Requested,
            await scope.Repository.TryRequestShareAsync(
                holdGuid,
                "S001",
                "POS-01",
                "2026-07-28T00:00:01.000Z"));
        var publication = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.NotNull(publication);
        Assert.True(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid,
            publication!.Revision,
            payload,
            "2026-07-28T00:00:02.000Z"));
        Assert.Single(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:00:03.000Z"));

        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(
            new SharedHeldOrderClaimDraft(
                Guid.NewGuid(),
                holdGuid,
                "S001",
                "POS-01",
                SharedHeldOrderClaimSource.OfflineOrigin,
                $"wpf-offline:{holdGuid:D}",
                payload,
                "2026-07-28T00:00:04.000Z",
                ExpiresAtIso: null)));

        Assert.Empty(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:00:05.000Z"));
    }

    [Fact]
    public async Task Blocked_publications_keep_stable_error_and_only_explicit_re_evaluation_leaves()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-07-28T00:00:00.000Z"));

        // NeedsEvaluation 显式进入 Blocked，保留稳定 ErrorCode/ErrorMessage，revision +1。
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.Blocked,
            "2026-07-28T00:00:01.000Z", "PromotionRulesMissing", "缺少冻结促销规则"));
        var blocked = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.Blocked, blocked!.Status);
        Assert.Equal(2, blocked.Revision);
        Assert.Equal("PromotionRulesMissing", blocked.ErrorCode);
        Assert.Equal("缺少冻结促销规则", blocked.ErrorMessage);
        Assert.Null(blocked.NextAttemptAtIso);

        // Blocked 不进 due，也不进入旧挂单 re-evaluation；自动路径无法离开。
        Assert.Empty(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:10:00.000Z"));
        Assert.Empty(await scope.Repository.ListLegacyOrdersNeedingEvaluationAsync("S001"));
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.Blocked, 2, SharedHeldOrderPublicationStatus.PendingPublish, "2026-07-28T00:00:02.000Z"));

        // 显式重新评估（Upsert -> NeedsEvaluation）后回到评估候选；
        // Blocked -> NeedsEvaluation 是显式重评，Revision +1。
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));
        Assert.Equal(3, (await scope.Repository.GetPublicationAsync(holdGuid))!.Revision);
        var reEvaluated = Assert.Single(await scope.Repository.ListLegacyOrdersNeedingEvaluationAsync("S001"));
        Assert.Equal(holdGuid, reEvaluated.SuspendedOrderGuid);

        // PendingPublish 也可显式进入 Blocked，同样保留稳定错误。
        var second = Guid.NewGuid();
        Assert.True(await UpsertNeedsEvaluationAsync(scope, second, [1, 2, 3]));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            second, "S001", "POS-01", "2026-07-28T00:00:02.000Z"));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            second, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.PendingPublish, "2026-07-28T00:00:03.000Z"));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            second, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Blocked,
            "2026-07-28T00:00:04.000Z", "PublishRejected", "服务端拒绝"));
        var blockedFromPublish = await scope.Repository.GetPublicationAsync(second);
        Assert.Equal(SharedHeldOrderPublicationStatus.Blocked, blockedFromPublish!.Status);
        Assert.Equal("PublishRejected", blockedFromPublish.ErrorCode);
        Assert.Equal("服务端拒绝", blockedFromPublish.ErrorMessage);

        // PendingPublish 绝不能被 Upsert 重置回 NeedsEvaluation。
        var third = Guid.NewGuid();
        Assert.True(await UpsertNeedsEvaluationAsync(scope, third, [1, 2, 3]));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            third, "S001", "POS-01", "2026-07-28T00:00:04.000Z"));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            third, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.PendingPublish, "2026-07-28T00:00:05.000Z"));
        Assert.False(await UpsertNeedsEvaluationAsync(scope, third));
        var stillPending = await scope.Repository.GetPublicationAsync(third);
        Assert.Equal(SharedHeldOrderPublicationStatus.PendingPublish, stillPending!.Status);
        Assert.Equal(2, stillPending.Revision);
        Assert.Equal(0, stillPending.RetryCount);
    }

    [Fact]
    public async Task Upsert_publication_only_inserts_initial_state()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();

        // 非初态 status 一律拒绝且不建行。
        Assert.False(await scope.Repository.UpsertPublicationAsync(
            holdGuid, "S001", "POS-01", SharedHeldOrderPublicationStatus.PendingPublish, null,
            "2026-07-28T00:00:00.000Z", "2026-07-28T00:00:00.000Z", "2026-07-28T00:00:00.000Z"));
        Assert.False(await scope.Repository.UpsertPublicationAsync(
            holdGuid, "S001", "POS-01", SharedHeldOrderPublicationStatus.Published, null,
            "2026-07-28T00:00:00.000Z", "2026-07-28T00:00:00.000Z", "2026-07-28T00:00:00.000Z"));
        Assert.False(await scope.Repository.UpsertPublicationAsync(
            holdGuid, "S001", "POS-01", SharedHeldOrderPublicationStatus.Blocked, null,
            "2026-07-28T00:00:00.000Z", "2026-07-28T00:00:00.000Z", "2026-07-28T00:00:00.000Z"));
        Assert.Null(await scope.Repository.GetPublicationAsync(holdGuid));

        // NeedsEvaluation 可插入并可重复评估（幂等）。
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));
        Assert.Equal(1, (await scope.Repository.GetPublicationAsync(holdGuid))!.Revision);
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));
        Assert.Equal(1, (await scope.Repository.GetPublicationAsync(holdGuid))!.Revision);
    }

    [Fact]
    public async Task Stage_delete_of_published_order_blocks_republish_and_keeps_remote_cancel_intent()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-08-11T00:59:00.000Z"));
        Assert.True(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid,
            1,
            SamplePayload(),
            "2026-08-11T01:00:00.000Z"));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid,
            SharedHeldOrderPublicationStatus.PendingPublish,
            2,
            SharedHeldOrderPublicationStatus.Published,
            "2026-08-11T01:00:01.000Z",
            remoteRevision: 7,
            remoteUpdatedAtIso: "2026-08-11T01:00:01.000Z"));

        var staged = await scope.Repository.TryStageDeletePendingAsync(
            holdGuid,
            "s001",
            "pos-01",
            "2026-08-11T01:00:02.000Z");

        Assert.NotNull(staged);
        Assert.True(staged!.RemoteCancellationRequired);
        var publication = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.NotNull(publication);
        Assert.Equal(SharedHeldOrderPublicationStatus.Blocked, publication!.Status);
        Assert.Equal("LOCAL_DELETE_PENDING_REMOTE", publication.ErrorCode);
        Assert.Null(publication.RemoteRevision);
        Assert.Null(publication.RemoteUpdatedAtIso);
        Assert.NotNull(await scope.Repository.GetPublicationPayloadAsync(holdGuid));
        Assert.Empty(await scope.Repository.ListDuePublicationsAsync("2026-08-11T01:10:00.000Z"));

        // 网络取消失败后重试仍必须记得服务端已发布，不能退化成本地直接删除。
        var retry = await scope.Repository.TryStageDeletePendingAsync(
            holdGuid,
            "S001",
            "POS-01",
            "2026-08-11T01:00:03.000Z");
        Assert.NotNull(retry);
        Assert.True(retry!.RemoteCancellationRequired);
    }

    [Fact]
    public async Task Local_only_delete_completes_as_canceled_and_disappears_from_pending_orders()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);

        var staged = await scope.Repository.TryStageDeletePendingAsync(
            holdGuid,
            "S001",
            "POS-01",
            "2026-08-11T01:00:00.000Z");

        Assert.NotNull(staged);
        Assert.False(staged!.RemoteCancellationRequired);
        Assert.True(await scope.Repository.TryCompleteDeletePendingAsync(
            holdGuid,
            "S001",
            "POS-01",
            "2026-08-11T01:00:01.000Z"));

        var suspended = await new SuspendedOrderRepository(scope.Store).GetAsync(holdGuid);
        Assert.NotNull(suspended);
        Assert.Equal(SuspendedOrderStatus.Canceled, suspended!.Status);
        Assert.Empty(await new SuspendedOrderRepository(scope.Store).GetPendingAsync("S001", "POS-01"));
        var publication = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.NotNull(publication);
        Assert.Equal(SharedHeldOrderPublicationStatus.Blocked, publication!.Status);
        Assert.Equal("LOCAL_DELETE_PENDING_LOCAL", publication.ErrorCode);
        Assert.Equal("2026-08-11T01:00:01.000Z", publication.ConsumedAtIso);
    }

    [Fact]
    public async Task Delete_staging_rejects_other_device_open_claim_and_unstaged_completion()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));

        Assert.Null(await scope.Repository.TryStageDeletePendingAsync(
            holdGuid,
            "S001",
            "POS-02",
            "2026-08-11T01:00:00.000Z"));
        Assert.False(await scope.Repository.TryCompleteDeletePendingAsync(
            holdGuid,
            "S001",
            "POS-01",
            "2026-08-11T01:00:00.000Z"));

        var claimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            claimId,
            holdGuid: holdGuid,
            source: SharedHeldOrderClaimSource.RemoteClaim)));
        Assert.Null(await scope.Repository.TryStageDeletePendingAsync(
            holdGuid,
            "S001",
            "POS-01",
            "2026-08-11T01:00:01.000Z"));

        Assert.True(await scope.Repository.TryReleaseClaimAsync(
            claimId,
            "release-for-delete",
            SharedHeldOrderClaimStatus.Prepared,
            "2026-08-11T01:00:02.000Z"));
        Assert.NotNull(await scope.Repository.TryStageDeletePendingAsync(
            holdGuid,
            "S001",
            "POS-01",
            "2026-08-11T01:00:03.000Z"));
    }

    [Fact]
    public async Task Prepared_claim_fence_has_single_winner_per_store_device()
    {
        await using var scope = await CreateScopeAsync();
        var first = Draft(Guid.NewGuid(), prepareKey: "idem-1");
        var second = Draft(Guid.NewGuid(), prepareKey: "idem-2");

        var results = await Task.WhenAll(
            scope.Repository.TrySavePreparedClaimAsync(first),
            scope.Repository.TrySavePreparedClaimAsync(second));

        Assert.Equal(1, results.Count(winner => winner));
        Assert.Equal(
            1,
            await CountRowsAsync(scope, "SELECT COUNT(*) FROM SharedHeldOrderClaims WHERE StoreCode = 'S001' AND DeviceCode = 'POS-01'"));
    }

    [Fact]
    public async Task Claim_lifecycle_prepare_activate_bind_complete_is_durable_and_idempotent()
    {
        await using var scope = await CreateScopeAsync();
        var claimId = Guid.NewGuid();
        var holdGuid = Guid.NewGuid();
        var draft = new SharedHeldOrderClaimDraft(
            claimId,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.OfflineOrigin,
            "idem-prepare",
            SamplePayload(3),
            "2026-07-28T00:00:00.000Z",
            "2026-07-28T00:05:00.000Z");

        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft));
        // 同一 claim + 同一 prepare key 重放视为幂等成功，不产生第二行。
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(draft with { CreatedAtIso = "2026-07-28T00:00:01.000Z" }));
        Assert.Equal(1, await CountRowsAsync(scope, "SELECT COUNT(*) FROM SharedHeldOrderClaims WHERE ClaimId = $ClaimId", claimId));

        var stored = await scope.Repository.GetClaimAsync(claimId);
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, stored!.Status);
        Assert.Equal(holdGuid, stored.HoldGuid);
        Assert.Equal(SharedHeldOrderClaimSource.OfflineOrigin, stored.Source);
        Assert.Equal("idem-prepare", stored.PrepareIdempotencyKey);
        Assert.Null(stored.ActivateIdempotencyKey);
        Assert.Null(stored.ReleaseIdempotencyKey);
        Assert.Equal("2026-07-28T00:05:00.000Z", stored.ExpiresAtIso);
        // payload 只存密文：protector 加前缀后不等于明文 JSON。
        Assert.True(stored.PayloadCiphertext.Length > 4);
        Assert.Equal("enc:", Encoding.UTF8.GetString(stored.PayloadCiphertext[..4]));

        // 未激活前 prepare key 不匹配不能激活。
        Assert.False(await scope.Repository.TryActivateClaimAsync(claimId, "wrong-prepare", "idem-activate", 42, "2026-07-28T00:00:01.500Z"));
        // 激活需 prepare key 匹配；同 activate key 的 Active 重试幂等，不同 key 拒绝。
        Assert.True(await scope.Repository.TryActivateClaimAsync(claimId, "idem-prepare", "idem-activate", 42, "2026-07-28T00:00:02.000Z"));
        Assert.True(await scope.Repository.TryActivateClaimAsync(claimId, "idem-prepare", "idem-activate", 42, "2026-07-28T00:00:03.000Z"));
        // iPad parity：Active 重试以 activate key 为准，即使 prepare key 记错也视为同 key 重试。
        Assert.True(await scope.Repository.TryActivateClaimAsync(claimId, "wrong-prepare", "idem-activate", 42, "2026-07-28T00:00:03.000Z"));
        Assert.False(await scope.Repository.TryActivateClaimAsync(claimId, "idem-prepare", "idem-other", 42, "2026-07-28T00:00:03.000Z"));

        var active = await scope.Repository.GetClaimAsync(claimId);
        Assert.Equal(SharedHeldOrderClaimStatus.Active, active!.Status);
        Assert.Equal("idem-activate", active.ActivateIdempotencyKey);
        Assert.Equal(42L, active.ServerRevision);
        Assert.Null(active.BoundOrderGuid);

        // 同一 orderGuid 绑定重试幂等，不同 orderGuid 拒绝。
        var boundOrder = Guid.NewGuid().ToString("D");
        Assert.True(await scope.Repository.TryBindOrderAsync(claimId, "idem-activate", boundOrder, "2026-07-28T00:00:04.000Z"));
        Assert.True(await scope.Repository.TryBindOrderAsync(claimId, "idem-activate", boundOrder, "2026-07-28T00:00:05.000Z"));
        Assert.False(await scope.Repository.TryBindOrderAsync(claimId, "idem-activate", Guid.NewGuid().ToString("D"), "2026-07-28T00:00:05.000Z"));
        Assert.False(await scope.Repository.TryBindOrderAsync(claimId, "idem-other", boundOrder, "2026-07-28T00:00:05.000Z"));

        var bound = await scope.Repository.GetClaimAsync(claimId);
        Assert.Equal(SharedHeldOrderClaimStatus.Active, bound!.Status);
        Assert.Equal(boundOrder, bound.BoundOrderGuid);

        // 同 release key 的 Completed 重试幂等，不同 key 拒绝。
        Assert.True(await scope.Repository.TryCompleteClaimAsync(claimId, "idem-activate", "idem-release", "2026-07-28T00:00:06.000Z"));
        Assert.True(await scope.Repository.TryCompleteClaimAsync(claimId, "idem-activate", "idem-release", "2026-07-28T00:00:07.000Z"));
        Assert.False(await scope.Repository.TryCompleteClaimAsync(claimId, "idem-activate", "idem-other-release", "2026-07-28T00:00:07.000Z"));
        var completed = await scope.Repository.GetClaimAsync(claimId);
        Assert.Equal(SharedHeldOrderClaimStatus.Completed, completed!.Status);
        Assert.Equal("idem-release", completed.ReleaseIdempotencyKey);
        Assert.Equal(boundOrder, completed.BoundOrderGuid);
    }

    [Fact]
    public async Task Complete_requires_binding_and_release_after_bind_fails()
    {
        await using var scope = await CreateScopeAsync();
        var claimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(claimId, prepareKey: "idem-p")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(claimId, "idem-p", "idem-a", 1, "2026-07-28T00:00:01.000Z"));

        // 未绑定不能完成。
        Assert.False(await scope.Repository.TryCompleteClaimAsync(claimId, "idem-a", "idem-r", "2026-07-28T00:00:02.000Z"));
        Assert.Equal(SharedHeldOrderClaimStatus.Active, (await scope.Repository.GetClaimAsync(claimId))!.Status);

        // 绑定后绝不能 release。
        var boundOrder = Guid.NewGuid().ToString("D");
        Assert.True(await scope.Repository.TryBindOrderAsync(claimId, "idem-a", boundOrder, "2026-07-28T00:00:03.000Z"));
        Assert.False(await scope.Repository.TryReleaseClaimAsync(claimId, "rel-x", SharedHeldOrderClaimStatus.Active, "2026-07-28T00:00:04.000Z"));
        var stillBound = await scope.Repository.GetClaimAsync(claimId);
        Assert.Equal(SharedHeldOrderClaimStatus.Active, stillBound!.Status);
        Assert.Equal(boundOrder, stillBound.BoundOrderGuid);
        Assert.Null(stillBound.ReleaseIdempotencyKey);

        // 绑定后完成成功，终态不再可释放。
        Assert.True(await scope.Repository.TryCompleteClaimAsync(claimId, "idem-a", "idem-r", "2026-07-28T00:00:05.000Z"));
        Assert.False(await scope.Repository.TryReleaseClaimAsync(claimId, "rel-y", SharedHeldOrderClaimStatus.Completed, "2026-07-28T00:00:06.000Z"));
        Assert.Equal(SharedHeldOrderClaimStatus.Completed, (await scope.Repository.GetClaimAsync(claimId))!.Status);
    }

    [Fact]
    public async Task Release_from_prepared_or_active_is_idempotent_and_rejects_wrong_key()
    {
        await using var scope = await CreateScopeAsync();

        // Prepared -> Released：同 release key 重试幂等，不同 key 拒绝。
        var fromPrepared = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(fromPrepared, prepareKey: "idem-p1")));
        Assert.True(await scope.Repository.TryReleaseClaimAsync(fromPrepared, "rel-1", SharedHeldOrderClaimStatus.Prepared, "2026-07-28T00:00:01.000Z"));
        Assert.True(await scope.Repository.TryReleaseClaimAsync(fromPrepared, "rel-1", SharedHeldOrderClaimStatus.Prepared, "2026-07-28T00:00:02.000Z"));
        Assert.False(await scope.Repository.TryReleaseClaimAsync(fromPrepared, "rel-other", SharedHeldOrderClaimStatus.Prepared, "2026-07-28T00:00:02.000Z"));
        var releasedPrepared = await scope.Repository.GetClaimAsync(fromPrepared);
        Assert.Equal(SharedHeldOrderClaimStatus.Released, releasedPrepared!.Status);
        Assert.Equal("rel-1", releasedPrepared.ReleaseIdempotencyKey);
        Assert.Null(releasedPrepared.BoundOrderGuid);
        Assert.Null(releasedPrepared.ActivateIdempotencyKey);

        // Active（未绑定）-> Released：同样幂等。
        var fromActive = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(fromActive, prepareKey: "idem-p2")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(fromActive, "idem-p2", "idem-a2", null, "2026-07-28T00:00:01.000Z"));
        Assert.True(await scope.Repository.TryReleaseClaimAsync(fromActive, "rel-2", SharedHeldOrderClaimStatus.Active, "2026-07-28T00:00:02.000Z"));
        Assert.True(await scope.Repository.TryReleaseClaimAsync(fromActive, "rel-2", SharedHeldOrderClaimStatus.Active, "2026-07-28T00:00:03.000Z"));
        var releasedActive = await scope.Repository.GetClaimAsync(fromActive);
        Assert.Equal(SharedHeldOrderClaimStatus.Released, releasedActive!.Status);
        Assert.Equal("rel-2", releasedActive.ReleaseIdempotencyKey);
        Assert.Null(releasedActive.BoundOrderGuid);

        // 非 Prepared/Active 期望状态直接拒绝。
        Assert.False(await scope.Repository.TryReleaseClaimAsync(fromPrepared, "rel-3", SharedHeldOrderClaimStatus.Completed, "2026-07-28T00:00:04.000Z"));
    }

    [Fact]
    public async Task Force_release_transitions_prepared_or_bound_active_and_is_idempotent()
    {
        await using var scope = await CreateScopeAsync();

        // Prepared -> Released：同 release key 重试幂等，不同 key 拒绝。
        var fromPrepared = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(fromPrepared, prepareKey: "fr-p")));
        Assert.True(await scope.Repository.TryForceReleaseClaimAsync(
            fromPrepared,
            "fr-rel-1",
            SharedHeldOrderClaimStatus.Prepared,
            "2026-07-28T00:00:01.000Z"));
        Assert.True(await scope.Repository.TryForceReleaseClaimAsync(
            fromPrepared,
            "fr-rel-1",
            SharedHeldOrderClaimStatus.Prepared,
            "2026-07-28T00:00:02.000Z"));
        Assert.False(await scope.Repository.TryForceReleaseClaimAsync(
            fromPrepared,
            "fr-rel-other",
            SharedHeldOrderClaimStatus.Prepared,
            "2026-07-28T00:00:02.000Z"));
        var releasedPrepared = await scope.Repository.GetClaimAsync(fromPrepared);
        Assert.Equal(SharedHeldOrderClaimStatus.Released, releasedPrepared!.Status);
        Assert.Equal("fr-rel-1", releasedPrepared.ReleaseIdempotencyKey);
        Assert.Null(releasedPrepared.BoundOrderGuid);

        // 已绑定订单的 Active：普通 release 拒绝，force release 必须能解除绑定并终态化。
        var boundActive = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(boundActive, prepareKey: "fr-p2")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            boundActive,
            "fr-p2",
            "fr-a2",
            serverRevision: 7L,
            "2026-07-28T00:00:01.000Z"));
        var boundOrder = Guid.NewGuid().ToString("D");
        Assert.True(await scope.Repository.TryBindOrderAsync(
            boundActive,
            "fr-a2",
            boundOrder,
            "2026-07-28T00:00:02.000Z"));
        Assert.False(await scope.Repository.TryReleaseClaimAsync(
            boundActive,
            "rel-x",
            SharedHeldOrderClaimStatus.Active,
            "2026-07-28T00:00:03.000Z"));

        Assert.True(await scope.Repository.TryForceReleaseClaimAsync(
            boundActive,
            "fr-rel-2",
            SharedHeldOrderClaimStatus.Active,
            "2026-07-28T00:00:04.000Z"));
        Assert.True(await scope.Repository.TryForceReleaseClaimAsync(
            boundActive,
            "fr-rel-2",
            SharedHeldOrderClaimStatus.Active,
            "2026-07-28T00:00:05.000Z"));
        var releasedActive = await scope.Repository.GetClaimAsync(boundActive);
        Assert.Equal(SharedHeldOrderClaimStatus.Released, releasedActive!.Status);
        Assert.Equal("fr-rel-2", releasedActive.ReleaseIdempotencyKey);
        Assert.Null(releasedActive.BoundOrderGuid);

        // 终态/非 Prepared/Active 期望状态直接拒绝。
        Assert.False(await scope.Repository.TryForceReleaseClaimAsync(
            boundActive,
            "fr-rel-3",
            SharedHeldOrderClaimStatus.Released,
            "2026-07-28T00:00:06.000Z"));
    }

    [Fact]
    public async Task Active_claims_are_not_auto_released_and_recovery_is_mine_scoped()
    {
        await using var scope = await CreateScopeAsync();
        var mine = Guid.NewGuid();
        var mineHoldGuid = Guid.NewGuid();
        var otherDevice = Guid.NewGuid();

        // 已过期（ExpiresAtIso 早于当前）的 Active claim 仍必须可恢复，绝不自动释放。
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            mine,
            mineHoldGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.RemoteClaim,
            "idem-mine",
            SamplePayload(2),
            "2026-07-28T00:00:00.000Z",
            "2026-07-27T00:00:00.000Z")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(mine, "idem-mine", "idem-act-mine", 7, "2026-07-28T00:00:01.000Z"));
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(new SharedHeldOrderClaimDraft(
            otherDevice,
            Guid.NewGuid(),
            "S001",
            "POS-02",
            SharedHeldOrderClaimSource.OfflineOrigin,
            "idem-other",
            SamplePayload(2),
            "2026-07-28T00:00:00.000Z",
            "2026-07-27T00:00:00.000Z")));

        // mine recovery 返回本设备 Active claim 并携带 HoldGuid/Source/keys/server revision。
        var recoverable = await scope.Repository.FindRecoverableClaimsAsync("S001", "POS-01");
        var recovered = Assert.Single(recoverable);
        Assert.Equal(SharedHeldOrderClaimStatus.Active, recovered.Status);
        Assert.Equal(mineHoldGuid, recovered.HoldGuid);
        Assert.Equal(SharedHeldOrderClaimSource.RemoteClaim, recovered.Source);
        Assert.Equal("idem-act-mine", recovered.ActivateIdempotencyKey);
        Assert.Equal(7L, recovered.ServerRevision);
        Assert.Equal(2, recovered.Payload.PricingState.Revision);

        // 本设备范围内：另一终端的 claim 不属于 mine；其自己的 Prepared 仍可恢复。
        var other = await scope.Repository.FindRecoverableClaimsAsync("S001", "POS-02");
        var otherRecovered = Assert.Single(other);
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, otherRecovered.Status);
        Assert.Equal(SharedHeldOrderClaimSource.OfflineOrigin, otherRecovered.Source);

        // Released 不参与恢复。
        Assert.True(await scope.Repository.TryReleaseClaimAsync(mine, "rel-mine", SharedHeldOrderClaimStatus.Active, "2026-07-28T00:00:02.000Z"));
        Assert.Empty(await scope.Repository.FindRecoverableClaimsAsync("S001", "POS-01"));
        Assert.Equal(SharedHeldOrderClaimStatus.Released, (await scope.Repository.GetClaimAsync(mine))!.Status);
    }

    [Fact]
    public async Task Published_requires_remote_fields_and_stores_revision_beyond_int32()
    {
        await using var scope = await CreateScopeAsync();

        // 非 Published 迁移携带 remote 字段一律拒绝。
        var blocked = Guid.NewGuid();
        Assert.True(await UpsertNeedsEvaluationAsync(scope, blocked));
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            blocked, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.PendingPublish,
            "2026-07-28T00:00:00.500Z", remoteRevision: 1L, remoteUpdatedAtIso: "2026-07-28T00:00:00.500Z"));
        Assert.Equal(SharedHeldOrderPublicationStatus.NeedsEvaluation,
            (await scope.Repository.GetPublicationAsync(blocked))!.Status);

        var holdGuid = Guid.NewGuid();
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid, [1, 2, 3]));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.PendingPublish,
            "2026-07-28T00:00:01.000Z"));

        // 缺 remote revision / updated-at / 负 revision 都不能进入 Published。
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T00:00:02.000Z"));
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T00:00:02.000Z", remoteRevision: null, remoteUpdatedAtIso: "2026-07-28T00:00:02.000Z"));
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T00:00:02.000Z", remoteRevision: -1L, remoteUpdatedAtIso: "2026-07-28T00:00:02.000Z"));
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T00:00:02.000Z", remoteRevision: 1L, remoteUpdatedAtIso: null));
        var stillPending = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.PendingPublish, stillPending!.Status);
        Assert.Equal(2, stillPending.Revision);
        Assert.Null(stillPending.RemoteRevision);
        Assert.Null(stillPending.RemoteUpdatedAtIso);

        // 远程 revision 超过 int.MaxValue 也必须原样保存并读回。
        const long largeRemoteRevision = 3_000_000_000L;
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.PendingPublish, 2, SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T00:00:03.000Z",
            remoteRevision: largeRemoteRevision, remoteUpdatedAtIso: "2026-07-28T00:00:03.000Z"));
        var published = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.Published, published!.Status);
        Assert.Equal(3, published.Revision);
        Assert.Equal(largeRemoteRevision, published.RemoteRevision);
        Assert.Equal("2026-07-28T00:00:03.000Z", published.RemoteUpdatedAtIso);
        Assert.Empty(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:10:00.000Z"));
    }

    [Fact]
    public async Task Needs_evaluation_without_payload_cannot_enter_pending_publish()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));

        // 评估产物缺失：NeedsEvaluation -> PendingPublish 必须返回 false 且不推进。
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.PendingPublish,
            "2026-07-28T00:00:01.000Z"));
        var unchanged = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.NeedsEvaluation, unchanged!.Status);
        Assert.Equal(1, unchanged.Revision);
        Assert.Null(unchanged.PayloadCiphertext);
        Assert.Empty(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:10:00.000Z"));

        // 补上 payload 后可以进入 PendingPublish。
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid, [1, 2, 3]));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.PendingPublish,
            "2026-07-28T00:00:02.000Z"));
        Assert.Single(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:10:00.000Z"));
    }

    [Fact]
    public async Task Blocked_re_evaluation_bumps_revision_and_stale_cas_cannot_cross()
    {
        await using var scope = await CreateScopeAsync();
        var holdGuid = Guid.NewGuid();
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 1, SharedHeldOrderPublicationStatus.Blocked,
            "2026-07-28T00:00:01.000Z", "PromotionRulesMissing", "缺少冻结促销规则"));
        Assert.Equal(2, (await scope.Repository.GetPublicationAsync(holdGuid))!.Revision);

        // Blocked -> NeedsEvaluation 显式重评 Revision +1（2 -> 3），并携带评估产物。
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid, [1, 2, 3]));
        var reEvaluated = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.Equal(SharedHeldOrderPublicationStatus.NeedsEvaluation, reEvaluated!.Status);
        Assert.Equal(3, reEvaluated.Revision);
        Assert.Equal(0, reEvaluated.RetryCount);

        // 纯相同 NeedsEvaluation 重放不增 revision。
        Assert.True(await UpsertNeedsEvaluationAsync(scope, holdGuid));
        Assert.Equal(3, (await scope.Repository.GetPublicationAsync(holdGuid))!.Revision);

        // stale CAS（旧 revision）不能穿过重评；当前 revision 的 CAS 正常推进。
        Assert.False(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 2, SharedHeldOrderPublicationStatus.PendingPublish,
            "2026-07-28T00:00:02.000Z"));
        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid, SharedHeldOrderPublicationStatus.NeedsEvaluation, 3, SharedHeldOrderPublicationStatus.PendingPublish,
            "2026-07-28T00:00:03.000Z"));
        Assert.Equal(4, (await scope.Repository.GetPublicationAsync(holdGuid))!.Revision);
    }

    [Fact]
    public async Task Prepared_replay_requires_matching_hold_guid_store_device_and_source()
    {
        await using var scope = await CreateScopeAsync();
        var claimId = Guid.NewGuid();
        var holdGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            claimId, prepareKey: "idem-prepare", holdGuid: holdGuid)));

        // store/device 大小写差异按规范化比较仍视为同一重放。
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            claimId, prepareKey: "idem-prepare", holdGuid: holdGuid,
            storeCode: "s001", deviceCode: "pos-01")));

        // HoldGuid 不一致、Source 不一致都不是幂等重放。
        Assert.False(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            claimId, prepareKey: "idem-prepare", holdGuid: Guid.NewGuid())));
        Assert.False(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            claimId, prepareKey: "idem-prepare", holdGuid: holdGuid,
            source: SharedHeldOrderClaimSource.RemoteClaim)));
        Assert.False(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            claimId, prepareKey: "idem-other", holdGuid: holdGuid)));

        var stored = await scope.Repository.GetClaimAsync(claimId);
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, stored!.Status);
        Assert.Equal(holdGuid, stored.HoldGuid);
        Assert.Equal("S001", stored.StoreCode);
        Assert.Equal("POS-01", stored.DeviceCode);
    }

    [Fact]
    public async Task Cross_claim_duplicate_activate_and_release_keys_are_rejected()
    {
        await using var scope = await CreateScopeAsync();
        var claimA = Guid.NewGuid();
        var claimB = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            claimA, prepareKey: "idem-prep-a", deviceCode: "POS-01")));
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            claimB, prepareKey: "idem-prep-b", deviceCode: "POS-02")));

        // 同一 activate key 只允许一个赢家；第二个 claim 被 SQLite 唯一索引拒绝。
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimA, "idem-prep-a", "idem-act-shared", 1L, "2026-07-28T00:00:01.000Z"));
        var activateLoser = await Assert.ThrowsAsync<SqliteException>(
            () => scope.Repository.TryActivateClaimAsync(
                claimB, "idem-prep-b", "idem-act-shared", 1L, "2026-07-28T00:00:01.000Z"));
        Assert.Equal(19, activateLoser.SqliteErrorCode);
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared,
            (await scope.Repository.GetClaimAsync(claimB))!.Status);

        // 同一 release key 同样只允许一个赢家。
        Assert.True(await scope.Repository.TryReleaseClaimAsync(
            claimA, "rel-shared", SharedHeldOrderClaimStatus.Active, "2026-07-28T00:00:02.000Z"));
        var releaseLoser = await Assert.ThrowsAsync<SqliteException>(
            () => scope.Repository.TryReleaseClaimAsync(
                claimB, "rel-shared", SharedHeldOrderClaimStatus.Prepared, "2026-07-28T00:00:02.000Z"));
        Assert.Equal(19, releaseLoser.SqliteErrorCode);
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared,
            (await scope.Repository.GetClaimAsync(claimB))!.Status);
        Assert.Equal(SharedHeldOrderClaimStatus.Released,
            (await scope.Repository.GetClaimAsync(claimA))!.Status);
    }

    [Fact]
    public async Task Claim_server_revision_beyond_int32_is_durable_and_recoverable()
    {
        await using var scope = await CreateScopeAsync();
        var claimId = Guid.NewGuid();
        const long largeServerRevision = 4_000_000_000L;
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(claimId, prepareKey: "idem-p")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            claimId, "idem-p", "idem-a", largeServerRevision, "2026-07-28T00:00:01.000Z"));

        var stored = await scope.Repository.GetClaimAsync(claimId);
        Assert.Equal(SharedHeldOrderClaimStatus.Active, stored!.Status);
        Assert.Equal(largeServerRevision, stored.ServerRevision);

        var recovered = Assert.Single(await scope.Repository.FindRecoverableClaimsAsync("S001", "POS-01"));
        Assert.Equal(largeServerRevision, recovered.ServerRevision);

        // 负的 server revision 在进入 DB 前即被拒绝。
        var invalid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(invalid, prepareKey: "idem-p2")));
        Assert.False(await scope.Repository.TryActivateClaimAsync(
            invalid, "idem-p2", "idem-a2", -1L, "2026-07-28T00:00:02.000Z"));
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared,
            (await scope.Repository.GetClaimAsync(invalid))!.Status);
    }

    [Fact]
    public async Task Prepared_replay_rejects_payload_and_expiry_mismatch()
    {
        await using var scope = await CreateScopeAsync();
        var claimId = Guid.NewGuid();
        var holdGuid = Guid.NewGuid();
        var original = new SharedHeldOrderClaimDraft(
            claimId,
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderClaimSource.OfflineOrigin,
            "idem-prepare",
            SamplePayload(1),
            "2026-07-28T00:00:00.000Z",
            "2026-07-28T00:05:00.000Z");
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(original));

        // 相同 claim + prepare key + scope/source/expiry + payload，仅 CreatedAt 不同：幂等重放成功。
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(
            original with { CreatedAtIso = "2026-07-28T00:00:01.000Z" }));

        // payload 不同：即使同一 claim/key 也视为输家。
        Assert.False(await scope.Repository.TrySavePreparedClaimAsync(
            original with { Payload = SamplePayload(2) }));
        // ExpiresAt 不同：同样拒绝。
        Assert.False(await scope.Repository.TrySavePreparedClaimAsync(
            original with { ExpiresAtIso = "2026-07-28T00:10:00.000Z" }));
        Assert.False(await scope.Repository.TrySavePreparedClaimAsync(
            original with { ExpiresAtIso = null }));

        var stored = await scope.Repository.GetClaimAsync(claimId);
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, stored!.Status);
        Assert.Equal("2026-07-28T00:05:00.000Z", stored.ExpiresAtIso);
        Assert.Equal(1, await CountRowsAsync(
            scope,
            "SELECT COUNT(*) FROM SharedHeldOrderClaims WHERE ClaimId = $ClaimId",
            claimId));
    }

    [Fact]
    public async Task Supersede_requires_unbound_claim_and_preserves_activate_key()
    {
        await using var scope = await CreateScopeAsync();

        // Prepared（activate 空）-> Superseded。
        var preparedClaim = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            preparedClaim, prepareKey: "idem-p1", deviceCode: "POS-01")));
        Assert.True(await scope.Repository.TrySupersedeClaimAsync(
            preparedClaim,
            "supersede-1",
            SharedHeldOrderClaimStatus.Prepared,
            "2026-07-28T00:01:00.000Z"));
        var superseded = await scope.Repository.GetClaimAsync(preparedClaim);
        Assert.Equal(SharedHeldOrderClaimStatus.Superseded, superseded!.Status);
        Assert.Null(superseded.ActivateIdempotencyKey);
        Assert.Null(superseded.ReleaseIdempotencyKey);
        Assert.Equal("supersede-1", superseded.SupersedeIdempotencyKey);

        // 同 claim + 同 supersede key 幂等重放；不同 key 拒绝。
        Assert.True(await scope.Repository.TrySupersedeClaimAsync(
            preparedClaim,
            "supersede-1",
            SharedHeldOrderClaimStatus.Prepared,
            "2026-07-28T00:01:01.000Z"));
        Assert.False(await scope.Repository.TrySupersedeClaimAsync(
            preparedClaim,
            "supersede-other",
            SharedHeldOrderClaimStatus.Prepared,
            "2026-07-28T00:01:02.000Z"));

        // Active（保留 activate key）-> Superseded。
        var activeClaim = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            activeClaim, prepareKey: "idem-p2", deviceCode: "POS-02")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            activeClaim, "idem-p2", "idem-act-2", 7L, "2026-07-28T00:00:30.000Z"));
        Assert.True(await scope.Repository.TrySupersedeClaimAsync(
            activeClaim,
            "supersede-2",
            SharedHeldOrderClaimStatus.Active,
            "2026-07-28T00:01:00.000Z"));
        var activeSuperseded = await scope.Repository.GetClaimAsync(activeClaim);
        Assert.Equal(SharedHeldOrderClaimStatus.Superseded, activeSuperseded!.Status);
        Assert.Equal("idem-act-2", activeSuperseded.ActivateIdempotencyKey);

        // 已绑定订单的 Active 绝不 supersede；终态/未知状态直接拒绝。
        var boundClaim = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            boundClaim, prepareKey: "idem-p3", deviceCode: "POS-03")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            boundClaim, "idem-p3", "idem-act-3", null, "2026-07-28T00:00:30.000Z"));
        Assert.True(await scope.Repository.TryBindOrderAsync(
            boundClaim, "idem-act-3", Guid.NewGuid().ToString("D"), "2026-07-28T00:00:40.000Z"));
        Assert.False(await scope.Repository.TrySupersedeClaimAsync(
            boundClaim, "supersede-3", SharedHeldOrderClaimStatus.Active, "2026-07-28T00:01:00.000Z"));
        Assert.Equal(SharedHeldOrderClaimStatus.Active,
            (await scope.Repository.GetClaimAsync(boundClaim))!.Status);
        Assert.False(await scope.Repository.TrySupersedeClaimAsync(
            boundClaim, "supersede-4", SharedHeldOrderClaimStatus.Completed, "2026-07-28T00:01:00.000Z"));

        // 跨 claim 复用 supersede key 被唯一索引拒绝。
        var second = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            second, prepareKey: "idem-p4", deviceCode: "POS-04")));
        var loser = await Assert.ThrowsAsync<SqliteException>(
            () => scope.Repository.TrySupersedeClaimAsync(
                second, "supersede-1", SharedHeldOrderClaimStatus.Prepared, "2026-07-28T00:01:00.000Z"));
        Assert.Equal(19, loser.SqliteErrorCode);
    }

    [Fact]
    public async Task Consumed_publications_are_excluded_from_due_payload_and_legacy_evaluation()
    {
        await using var scope = await CreateScopeAsync();
        var consumedHold = Guid.NewGuid();
        var untouchedHold = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, consumedHold);
        await InsertLegacyOrderAsync(scope, untouchedHold);

        Assert.True(await scope.Repository.UpsertPublicationAsync(
            consumedHold,
            "S001",
            "POS-01",
            SharedHeldOrderPublicationStatus.NeedsEvaluation,
            null,
            "2026-07-28T00:00:00.000Z",
            "2026-07-28T00:00:00.000Z",
            "2026-07-28T00:00:00.000Z"));
        Assert.True(await scope.Repository.TryStagePendingPublishAsync(
            consumedHold, 1, SamplePayload(), "2026-07-28T00:00:01.000Z"));

        // 直接模拟成交消费：publication ConsumedAtIso 置位（付款事务内由 LocalOrderRepository 写入）。
        await using (var connection = await scope.Store.OpenConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE SharedHeldOrderPublications
                SET ConsumedAtIso = '2026-07-28T00:05:00.000Z',
                    UpdatedAtIso = '2026-07-28T00:05:00.000Z'
                WHERE LocalHoldGuid = $HoldGuid;
                """;
            command.Parameters.AddWithValue("$HoldGuid", consumedHold.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        // 消费后：不再出现在 due 发布队列，payload 不可恢复（离线 recall 会被 NOT_FOUND 拦截），
        // 也不再进入 legacy 评估候选；未消费的挂单不受影响。
        Assert.Empty(await scope.Repository.ListDuePublicationsAsync("2026-07-28T00:10:00.000Z"));
        Assert.Null(await scope.Repository.GetPublicationPayloadAsync(consumedHold));
        var publication = await scope.Repository.GetPublicationAsync(consumedHold);
        Assert.NotNull(publication);
        Assert.Equal("2026-07-28T00:05:00.000Z", publication!.ConsumedAtIso);
        var legacy = await scope.Repository.ListLegacyOrdersNeedingEvaluationAsync("S001");
        Assert.Single(legacy);
        Assert.Equal(untouchedHold, legacy[0].SuspendedOrderGuid);
    }

    [Fact]
    public async Task Expired_remote_prepared_claim_transitions_to_released_and_clears_fence()
    {
        await using var scope = await CreateScopeAsync();
        var claimId = Guid.NewGuid();
        var holdGuid = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            claimId,
            prepareKey: "prepare-expired",
            holdGuid: holdGuid,
            source: SharedHeldOrderClaimSource.RemoteClaim,
            expiresAtIso: "2026-07-28T01:04:00.000Z")));

        // 本地 RemoteClaim Prepared 且可信 ExpiresAt 已过时：幂等推进 Released 并清 fence。
        Assert.True(await scope.Repository.TryExpirePreparedRemoteClaimAsync(
            claimId,
            "wpf-expired-prepare:test",
            "2026-07-28T01:06:00.000Z"));

        var claim = await scope.Repository.GetClaimAsync(claimId);
        Assert.NotNull(claim);
        Assert.Equal(SharedHeldOrderClaimStatus.Released, claim!.Status);
        Assert.Equal("wpf-expired-prepare:test", claim.ReleaseIdempotencyKey);
        Assert.Empty(await scope.Repository.FindRecoverableClaimsAsync("S001", "POS-01"));

        // 崩溃重放：同 claim + 同 release key 幂等返回 true，不再报错。
        Assert.True(await scope.Repository.TryExpirePreparedRemoteClaimAsync(
            claimId,
            "wpf-expired-prepare:test",
            "2026-07-28T01:07:00.000Z"));
        Assert.Equal(SharedHeldOrderClaimStatus.Released, (await scope.Repository.GetClaimAsync(claimId))!.Status);
    }

    [Fact]
    public async Task Expired_remote_prepared_rejects_wrong_release_key_replay()
    {
        await using var scope = await CreateScopeAsync();
        var claimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            claimId,
            prepareKey: "prepare-expired-wrong-key",
            source: SharedHeldOrderClaimSource.RemoteClaim,
            expiresAtIso: "2026-07-28T01:04:00.000Z")));

        Assert.True(await scope.Repository.TryExpirePreparedRemoteClaimAsync(
            claimId,
            "wpf-expired-prepare:first",
            "2026-07-28T01:06:00.000Z"));
        Assert.False(await scope.Repository.TryExpirePreparedRemoteClaimAsync(
            claimId,
            "wpf-expired-prepare:second",
            "2026-07-28T01:07:00.000Z"));

        var claim = await scope.Repository.GetClaimAsync(claimId);
        Assert.NotNull(claim);
        Assert.Equal("wpf-expired-prepare:first", claim!.ReleaseIdempotencyKey);
    }

    [Fact]
    public async Task Expire_prepared_remote_never_touches_active_unexpired_or_offline_claims()
    {
        await using var scope = await CreateScopeAsync();

        // Active 永不自动过期：即使残留 ExpiresAtIso 早于当前也不推进。
        var activeClaimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            activeClaimId,
            prepareKey: "prepare-active",
            source: SharedHeldOrderClaimSource.RemoteClaim,
            expiresAtIso: "2026-07-28T01:04:00.000Z")));
        Assert.True(await scope.Repository.TryActivateClaimAsync(
            activeClaimId,
            "prepare-active",
            "activate-active",
            serverRevision: 1L,
            "2026-07-28T01:03:00.000Z"));
        Assert.False(await scope.Repository.TryExpirePreparedRemoteClaimAsync(
            activeClaimId,
            "wpf-expired-prepare:active",
            "2026-07-28T01:06:00.000Z"));
        Assert.Equal(SharedHeldOrderClaimStatus.Active, (await scope.Repository.GetClaimAsync(activeClaimId))!.Status);

        // 未到期的 Prepared 不动。
        var futureClaimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            futureClaimId,
            prepareKey: "prepare-future",
            source: SharedHeldOrderClaimSource.RemoteClaim,
            expiresAtIso: "2026-07-28T01:10:00.000Z")));
        Assert.False(await scope.Repository.TryExpirePreparedRemoteClaimAsync(
            futureClaimId,
            "wpf-expired-prepare:future",
            "2026-07-28T01:06:00.000Z"));
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, (await scope.Repository.GetClaimAsync(futureClaimId))!.Status);

        // OfflineOrigin 即使带 ExpiresAt 也绝不自动过期（来源守卫）。
        var offlineClaimId = Guid.NewGuid();
        Assert.True(await scope.Repository.TrySavePreparedClaimAsync(Draft(
            offlineClaimId,
            prepareKey: "prepare-offline",
            source: SharedHeldOrderClaimSource.OfflineOrigin,
            expiresAtIso: "2026-07-28T01:04:00.000Z")));
        Assert.False(await scope.Repository.TryExpirePreparedRemoteClaimAsync(
            offlineClaimId,
            "wpf-expired-prepare:offline",
            "2026-07-28T01:06:00.000Z"));
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, (await scope.Repository.GetClaimAsync(offlineClaimId))!.Status);
    }

    private static async Task<int> CountRowsAsync(
        RepositoryScope scope,
        string sql,
        Guid? claimId = null)
    {
        await using var connection = await scope.Store.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (claimId is not null)
        {
            command.Parameters.AddWithValue("$ClaimId", claimId.Value.ToString("D"));
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
