using System.Text.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using static Hbpos.Client.Tests.SharedHeldOrderClientTestSupport;

namespace Hbpos.Client.Tests;

/// <summary>
/// Phase 2C 新增 repository 入口：评估结果原子落库（payload 密文 + 状态切换 CAS）、
/// Blocked 稳定原因、以及 published/待发布副本的解密读取（离线 recall 依赖）。
/// </summary>
public sealed class SharedHeldOrderRepositoryPhase2CTests
{
    [Fact]
    public async Task TryStagePendingPublishAsync_is_cas_and_encrypts_payload()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);
        Assert.True(await scope.Repository.UpsertPublicationAsync(
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderPublicationStatus.NeedsEvaluation,
            null,
            "2026-07-28T01:00:00.000Z",
            "2026-07-28T01:00:00.000Z",
            "2026-07-28T01:00:00.000Z"));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-07-28T01:00:00.000Z"));
        var payload = SampleCanonical();

        Assert.False(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid,
            expectedRevision: 99,
            payload,
            "2026-07-28T01:01:00.000Z"));
        Assert.True(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid,
            expectedRevision: 1,
            payload,
            "2026-07-28T01:01:00.000Z"));

        var publication = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.NotNull(publication);
        Assert.Equal(SharedHeldOrderPublicationStatus.PendingPublish, publication!.Status);
        Assert.Equal(2, publication.Revision);
        Assert.NotNull(publication.PayloadCiphertext);
        AssertCanonicalEqual(
            payload,
            await scope.Repository.GetPublicationPayloadAsync(holdGuid));

        // PendingPublish 已带 payload，不能再次从 NeedsEvaluation 阶段（CAS 失败返回 false）。
        Assert.False(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid,
            expectedRevision: 2,
            payload,
            "2026-07-28T01:02:00.000Z"));
    }

    [Fact]
    public async Task TryBlockPublicationAsync_records_stable_reason_and_blocks_reevaluation_guard()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);
        Assert.True(await scope.Repository.UpsertPublicationAsync(
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderPublicationStatus.NeedsEvaluation,
            null,
            "2026-07-28T01:00:00.000Z",
            "2026-07-28T01:00:00.000Z",
            "2026-07-28T01:00:00.000Z"));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-07-28T01:00:00.000Z"));

        Assert.False(await scope.Repository.TryBlockPublicationAsync(
            holdGuid,
            expectedRevision: 99,
            "ReturnLineNotSupported",
            "detail",
            "2026-07-28T01:01:00.000Z"));
        Assert.True(await scope.Repository.TryBlockPublicationAsync(
            holdGuid,
            expectedRevision: 1,
            "ReturnLineNotSupported",
            "detail",
            "2026-07-28T01:01:00.000Z"));

        var publication = await scope.Repository.GetPublicationAsync(holdGuid);
        Assert.NotNull(publication);
        Assert.Equal(SharedHeldOrderPublicationStatus.Blocked, publication!.Status);
        Assert.Equal("ReturnLineNotSupported", publication.ErrorCode);
        Assert.Equal("detail", publication.ErrorMessage);
        Assert.Equal(2, publication.Revision);

        // Blocked 只能通过显式重新评估离开；不能直接推进到 PendingPublish。
        Assert.False(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid,
            expectedRevision: 2,
            SampleCanonical(),
            "2026-07-28T01:02:00.000Z"));
        Assert.Equal(
            SharedHeldOrderPublicationStatus.Blocked,
            (await scope.Repository.GetPublicationAsync(holdGuid))!.Status);
    }

    [Fact]
    public async Task GetPublicationPayloadAsync_only_returns_payload_for_pending_or_published()
    {
        await using var scope = await CreateRepositoryScopeAsync();
        var holdGuid = Guid.NewGuid();
        await InsertLegacyOrderAsync(scope, holdGuid);
        var payload = SampleCanonical();
        Assert.True(await scope.Repository.UpsertPublicationAsync(
            holdGuid,
            "S001",
            "POS-01",
            SharedHeldOrderPublicationStatus.NeedsEvaluation,
            null,
            "2026-07-28T01:00:00.000Z",
            "2026-07-28T01:00:00.000Z",
            "2026-07-28T01:00:00.000Z"));
        Assert.Equal(SharedHeldOrderShareRequestResult.Requested, await scope.Repository.TryRequestShareAsync(
            holdGuid, "S001", "POS-01", "2026-07-28T01:00:00.000Z"));
        Assert.Null(await scope.Repository.GetPublicationPayloadAsync(holdGuid));

        Assert.True(await scope.Repository.TryStagePendingPublishAsync(
            holdGuid,
            1,
            payload,
            "2026-07-28T01:01:00.000Z"));
        AssertCanonicalEqual(payload, await scope.Repository.GetPublicationPayloadAsync(holdGuid));

        Assert.True(await scope.Repository.TryAdvancePublicationAsync(
            holdGuid,
            SharedHeldOrderPublicationStatus.PendingPublish,
            2,
            SharedHeldOrderPublicationStatus.Published,
            "2026-07-28T01:02:00.000Z",
            remoteRevision: 5L,
            remoteUpdatedAtIso: "2026-07-28T01:02:00.000Z"));
        AssertCanonicalEqual(payload, await scope.Repository.GetPublicationPayloadAsync(holdGuid));

        var missing = await scope.Repository.GetPublicationPayloadAsync(Guid.NewGuid());
        Assert.Null(missing);
    }

    private static async Task InsertLegacyOrderAsync(RepositoryScope scope, Guid holdGuid)
    {
        // 显式构造没有 publication 行的旧挂单；share 请求必须绑定真实 Pending 挂单。
        await using var connection = await scope.Store.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SuspendedOrders (
                SuspendedOrderGuid, StoreCode, DeviceCode, CashierId, CashierName, SuspendedAt,
                TotalAmount, DiscountAmount, ActualAmount, Status)
            VALUES ($HoldGuid, 'S001', 'POS-01', 'cashier-1', 'Cashier One',
                    '2026-07-28T00:00:00+00:00', '11.00', '0.00', '11.00', 0);

            INSERT INTO SuspendedOrderLines (
                SuspendedOrderLineGuid, SuspendedOrderGuid, StoreCode, ProductCode, ReferenceCode,
                DisplayName, LookupCode, ItemNumber, ProductImage, Quantity, UnitPrice, DiscountAmount,
                DiscountPercent, IsAutomaticPromotionDiscount, DiscountSource, ActualAmount, PriceSource,
                PriceSourceLabel, Kind, ReturnSourceKey, OriginalOrderGuid, OriginalOrderDetailGuid, ReturnReason)
            VALUES ($LineGuid, $HoldGuid, 'S001', 'P-1', NULL, 'Product 1', 'CODE-1', NULL, NULL,
                    '1', '11.00', '0.00', NULL, 0, 0, '11.00', 0, 'ProductBase', 0, '', NULL, NULL, NULL);
            """;
        command.Parameters.AddWithValue("$HoldGuid", holdGuid.ToString("D"));
        command.Parameters.AddWithValue("$LineGuid", Guid.NewGuid().ToString("D"));
        await command.ExecuteNonQueryAsync();
    }

    private static void AssertCanonicalEqual(
        SharedHeldOrderCanonicalPayload expected,
        SharedHeldOrderCanonicalPayload? actual)
    {
        Assert.NotNull(actual);
        // 使用独立于生产 canonical serializer 的结构序列化，既忽略集合实现类型，
        // 又能发现生产序列化/反序列化遗漏字段或改变集合顺序。
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }
}
