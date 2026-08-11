using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Wpf.Services;

/// <summary>payload 加密边界：repository 只保存密文，明文永远不进 SQLite。</summary>
public interface ISharedHeldOrderPayloadProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

public interface ISharedHeldOrderPayloadSerializer
{
    byte[] Serialize(SharedHeldOrderCanonicalPayload payload);
    SharedHeldOrderCanonicalPayload Deserialize(byte[] data);
}

/// <summary>
/// WPF 端 payload 保护：Windows DPAPI CurrentUser 作用域（Windows 专用）。
/// 数据库只保存密文；明文只存在于内存，绝不落库/写日志。
/// </summary>
public sealed class WindowsDpapiSharedHeldOrderPayloadProtector : ISharedHeldOrderPayloadProtector
{
    private static readonly byte[] Entropy = "Hbpos.SharedHeldOrders.Payload.v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext)
    {
        return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        return ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
    }
}

public sealed class SharedHeldOrderJsonPayloadSerializer : ISharedHeldOrderPayloadSerializer
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

public interface ISharedHeldOrderRepository
{
    /// <summary>
    /// 旧挂单评估：只选没有 publication row 或 publication status=NeedsEvaluation
    /// 的普通 sale；PendingPublish/Published/Blocked 均不进入评估。
    /// </summary>
    Task<IReadOnlyList<SuspendedOrder>> ListLegacyOrdersNeedingEvaluationAsync(
        string storeCode,
        string? deviceCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 评估/重新评估入口：只写初态 NeedsEvaluation（新行插入，或现有
    /// NeedsEvaluation 幂等重放 revision 不变，或 Blocked 显式重新评估
    /// Revision +1，并重置 RetryCount/尝试时间）。
    /// PendingPublish/Published 绝不能被本方法重置；状态参数非法或无更新时返回 false。
    /// 发布失败/成功的迁移必须走 <see cref="TryAdvancePublicationAsync"/> CAS。
    /// </summary>
    Task<bool> UpsertPublicationAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>后台 due：只返回 PendingPublish 且已到 NextAttemptAtIso 的行；Published/Blocked 均不重试。</summary>
    Task<IReadOnlyList<SharedHeldOrderPublication>> ListDuePublicationsAsync(
        string nowIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// CAS 推进本地 publication 状态机，只允许合法迁移：
    /// NeedsEvaluation -> PendingPublish/Blocked；PendingPublish -> Published/Blocked；
    /// PendingPublish -> PendingPublish 表示发布失败（RetryCount +1 并记录尝试时间）。
    /// PendingPublish -> Published 必须提供非负 remoteRevision 与非空 remoteUpdatedAtIso，
    /// 与状态切换原子写入；其他迁移不允许携带 remote 字段。
    /// 成功与失败重试都会使 Revision +1，调用方须重新读取新 revision；
    /// revision 与期望状态任一不符都返回 false。
    /// </summary>
    Task<bool> TryAdvancePublicationAsync(
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
        CancellationToken cancellationToken = default);

    Task<SharedHeldOrderPublication?> GetPublicationAsync(
        Guid localHoldGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除第一阶段：只允许本店、本机、Pending 且无 Prepared/Active claim 的本地挂单；
    /// 同一事务把 publication 暂存为 Blocked，确保后台不再发布。返回值指示是否还需
    /// 调用服务端 cancel；远端取消失败时可安全重试，意图不会丢失。
    /// </summary>
    Task<SharedHeldOrderDeleteStage?> TryStageDeletePendingAsync(
        Guid holdGuid,
        string storeCode,
        string deviceCode,
        string updatedAtIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除第二阶段：仅已暂存删除且无活动 claim 的本机挂单可完成；
    /// 将本地挂单标记 Canceled 并消费 publication，使其从待取列表与发布队列消失。
    /// </summary>
    Task<bool> TryCompleteDeletePendingAsync(
        Guid holdGuid,
        string storeCode,
        string deviceCode,
        string completedAtIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 评估结果原子落库：NeedsEvaluation -> PendingPublish 与 payload 密文一次性写入
    /// （CAS 校验 revision，避免两段式写入的崩溃窗口）。只接受 NeedsEvaluation；
    /// Blocked/PendingPublish/Published 均返回 false。失败原因不写入错误字段。
    /// </summary>
    Task<bool> TryStagePendingPublishAsync(
        Guid localHoldGuid,
        int expectedRevision,
        SharedHeldOrderCanonicalPayload payload,
        string updatedAtIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 评估阻断原子落库：NeedsEvaluation -> Blocked，稳定保留原因与详情；
    /// CAS 校验 revision。Blocked 不自动重试，只有显式重新评估才能离开。
    /// </summary>
    Task<bool> TryBlockPublicationAsync(
        Guid localHoldGuid,
        int expectedRevision,
        string errorCode,
        string errorMessage,
        string updatedAtIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 解密读取 PendingPublish/Published 的 canonical payload（离线 recall / 发布请求用）；
    /// NeedsEvaluation/Blocked 或无行返回 null。payload 明文只存在于内存，绝不落库/日志。
    /// </summary>
    Task<SharedHeldOrderCanonicalPayload?> GetPublicationPayloadAsync(
        Guid localHoldGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存 Prepared claim（含 HoldGuid 与 Source）；每 store+device 只允许一个
    /// Prepared/Active fence。同一 claim + 同一 prepare key 且仍为 Prepared 视为
    /// 幂等重放返回 true；重放还必须同时匹配 scope/source、ExpiresAt 与解密后的
    /// canonical payload 字节（CreatedAt 可不同），任一不同返回 false；
    /// fence/idempotency 输家同样返回 false。
    /// </summary>
    Task<bool> TrySavePreparedClaimAsync(
        SharedHeldOrderClaimDraft draft,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 激活 Prepared claim：需 prepare key 匹配且尚未激活，写入 activate key 与
    /// server revision（可空，>=0 时通过 CHECK 强制）。同 claim + 同 activate key
    /// 的 Active 重试返回 true；不同 key、终态或跨 claim 重复 key 被拒绝。
    /// </summary>
    Task<bool> TryActivateClaimAsync(
        Guid claimId,
        string prepareIdempotencyKey,
        string activateIdempotencyKey,
        long? serverRevision,
        string updatedAtIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 仅 Active claim 可绑定 orderGuid；同 activate key 且同一 orderGuid 重试
    /// 返回 true，不同 orderGuid 或非 Active 返回 false。
    /// </summary>
    Task<bool> TryBindOrderAsync(
        Guid claimId,
        string activateIdempotencyKey,
        string boundOrderGuid,
        string updatedAtIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 仅 Active 且 BoundOrderGuid 非空可完成；写入 release/complete 幂等键。
    /// 同 claim + 同 release key 的 Completed 重试返回 true。
    /// </summary>
    Task<bool> TryCompleteClaimAsync(
        Guid claimId,
        string activateIdempotencyKey,
        string releaseIdempotencyKey,
        string updatedAtIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 仅 Prepared/Active 且 BoundOrderGuid IS NULL 可释放；写入 release key。
    /// 同 claim + 同 release key 的 Released 重试返回 true；已绑定后绝不释放。
    /// </summary>
    Task<bool> TryReleaseClaimAsync(
        Guid claimId,
        string releaseIdempotencyKey,
        SharedHeldOrderClaimStatus expectedStatus,
        string updatedAtIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 本地 RemoteClaim Prepared 过期推进：仅当 Source=RemoteClaim、Status=Prepared、
    /// ExpiresAtIso 非空且不晚于 nowIso 时，CAS 推进 Released 并写入 release key
    /// （清 fence）。Active/OfflineOrigin/未到期一律不动；Active 永不自动过期。
    /// 同 claim + 同 release key 的 Released 重放返回 true（崩溃重放安全）；
    /// 不同 key/状态/来源/未到期返回 false。
    /// </summary>
    Task<bool> TryExpirePreparedRemoteClaimAsync(
        Guid claimId,
        string releaseIdempotencyKey,
        string nowIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 主管强制释放后的本地清理：仅 Prepared/Active -> Released，允许 Active 已绑定
    /// 订单时一并解除 BoundOrderGuid（服务端 force-release 成功后由协调器调用）。
    /// 同 claim + 同 release key 的 Released 重试返回 true；不同 key、终态或跨 claim 拒绝。
    /// </summary>
    Task<bool> TryForceReleaseClaimAsync(
        Guid claimId,
        string releaseIdempotencyKey,
        SharedHeldOrderClaimStatus expectedStatus,
        string updatedAtIso,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 服务端 OfflineOrigin 成交调和：仅 unbound 的 Prepared（activate 空）或
    /// Active（保留 ActivateIdempotencyKey）可进入 Superseded；已绑定订单的
    /// Active 绝不 supersede。同 claim + 同 supersede key 的 Superseded 重试
    /// 返回 true；不同 key/状态/绑定返回 false。
    /// </summary>
    Task<bool> TrySupersedeClaimAsync(
        Guid claimId,
        string supersedeIdempotencyKey,
        SharedHeldOrderClaimStatus expectedStatus,
        string updatedAtIso,
        CancellationToken cancellationToken = default);

    Task<SharedHeldOrderClaimRecord?> GetClaimAsync(
        Guid claimId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// mine recovery：本 store+device 的 Prepared/Active claim；
    /// Active 不按 ExpiresAt 自动释放，Prepared 过期策略留给 integration/server 对账。
    /// </summary>
    Task<IReadOnlyList<SharedHeldOrderClaimRecovery>> FindRecoverableClaimsAsync(
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken = default);
}

public sealed class SharedHeldOrderRepository(
    LocalSqliteStore store,
    ISharedHeldOrderPayloadProtector protector,
    ISharedHeldOrderPayloadSerializer serializer) : ISharedHeldOrderRepository
{
    private const string PublicationNeedsEvaluation = "NeedsEvaluation";
    private const string PublicationPendingPublish = "PendingPublish";
    private const string PublicationPublished = "Published";
    private const string PublicationBlocked = "Blocked";
    private const string LocalDeletePendingRemote = "LOCAL_DELETE_PENDING_REMOTE";
    private const string LocalDeletePendingLocal = "LOCAL_DELETE_PENDING_LOCAL";

    private const string ClaimPrepared = "Prepared";
    private const string ClaimActive = "Active";
    private const string ClaimCompleted = "Completed";
    private const string ClaimReleased = "Released";
    private const string ClaimSuperseded = "Superseded";

    private const string ClaimSourceRemoteClaim = "RemoteClaim";
    private const string ClaimSourceOfflineOrigin = "OfflineOrigin";

    public async Task<IReadOnlyList<SuspendedOrder>> ListLegacyOrdersNeedingEvaluationAsync(
        string storeCode,
        string? deviceCode = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedStoreCode = storeCode.Trim().ToUpperInvariant();
        var normalizedDeviceCode = (deviceCode ?? string.Empty).Trim().ToUpperInvariant();
        await using var connection = await store.OpenConnectionAsync(cancellationToken);

        var headers = new List<SuspendedOrder>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT o.SuspendedOrderGuid, o.StoreCode, o.DeviceCode, o.CashierId, o.CashierName,
                       o.SuspendedAt, o.TotalAmount, o.DiscountAmount, o.ActualAmount, o.Status,
                       o.FrozenPromotionRulesJson
                FROM SuspendedOrders o
                WHERE o.Status = $PendingStatus
                  AND UPPER(o.StoreCode) = $StoreCode
                  AND ($DeviceCode = '' OR UPPER(o.DeviceCode) = $DeviceCode)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM SharedHeldOrderPublications p
                      WHERE p.LocalHoldGuid = o.SuspendedOrderGuid
                        AND (p.Status IN ('PendingPublish', 'Published', 'Blocked')
                             OR p.ConsumedAtIso IS NOT NULL)
                  )
                ORDER BY o.SuspendedAt ASC;
                """;
            command.Parameters.AddWithValue("$PendingStatus", (int)SuspendedOrderStatus.Pending);
            command.Parameters.AddWithValue("$StoreCode", normalizedStoreCode);
            command.Parameters.AddWithValue("$DeviceCode", normalizedDeviceCode);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                headers.Add(new SuspendedOrder(
                    ReadGuid(reader, "SuspendedOrderGuid"),
                    ReadString(reader, "StoreCode"),
                    ReadString(reader, "DeviceCode"),
                    ReadString(reader, "CashierId"),
                    ReadString(reader, "CashierName"),
                    ReadDateTimeOffset(reader, "SuspendedAt"),
                    ReadDecimal(reader, "TotalAmount"),
                    ReadDecimal(reader, "DiscountAmount"),
                    ReadDecimal(reader, "ActualAmount"),
                    (SuspendedOrderStatus)reader.GetInt32(reader.GetOrdinal("Status")),
                    [])
                {
                    FrozenPromotionRules = DeserializePromotionRules(
                        ReadNullableString(reader, "FrozenPromotionRulesJson"))
                });
            }
        }

        for (var headerIndex = 0; headerIndex < headers.Count; headerIndex++)
        {
            var header = headers[headerIndex];
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT SuspendedOrderLineGuid, SuspendedOrderGuid, StoreCode, ProductCode, ReferenceCode,
                       DisplayName, LookupCode, ItemNumber, ProductImage, Quantity, UnitPrice, DiscountAmount,
                       DiscountPercent, IsAutomaticPromotionDiscount, DiscountSource, ActualAmount, PriceSource,
                       PriceSourceLabel, Kind, ReturnSourceKey, OriginalOrderGuid, OriginalOrderDetailGuid, ReturnReason,
                       IsManualPrice
                FROM SuspendedOrderLines
                WHERE SuspendedOrderGuid = $SuspendedOrderGuid
                ORDER BY rowid;
                """;
            command.Parameters.AddWithValue("$SuspendedOrderGuid", header.SuspendedOrderGuid.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var lines = new List<SuspendedOrderLine>();
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(new SuspendedOrderLine(
                    ReadGuid(reader, "SuspendedOrderLineGuid"),
                    ReadGuid(reader, "SuspendedOrderGuid"),
                    ReadString(reader, "StoreCode"),
                    ReadString(reader, "ProductCode"),
                    ReadNullableString(reader, "ReferenceCode"),
                    ReadString(reader, "DisplayName"),
                    ReadString(reader, "LookupCode"),
                    ReadNullableString(reader, "ItemNumber"),
                    ReadNullableString(reader, "ProductImage"),
                    ReadDecimal(reader, "Quantity"),
                    ReadDecimal(reader, "UnitPrice"),
                    ReadDecimal(reader, "DiscountAmount"),
                    ReadNullableDecimal(reader, "DiscountPercent"),
                    ReadDecimal(reader, "ActualAmount"),
                    (PriceSourceKind)reader.GetInt32(reader.GetOrdinal("PriceSource")),
                    ReadString(reader, "PriceSourceLabel"),
                    (PosCartLineDiscountSource)reader.GetInt32(reader.GetOrdinal("DiscountSource")))
                {
                    Kind = (CartLineKind)reader.GetInt32(reader.GetOrdinal("Kind")),
                    ReturnSourceKey = ReadNullableString(reader, "ReturnSourceKey") ?? string.Empty,
                    OriginalOrderGuid = ReadNullableGuid(reader, "OriginalOrderGuid"),
                    OriginalOrderDetailGuid = ReadNullableGuid(reader, "OriginalOrderDetailGuid"),
                    ReturnReason = ReadNullableString(reader, "ReturnReason"),
                    IsManualPrice = reader.GetInt32(reader.GetOrdinal("IsManualPrice")) != 0
                });
            }

            headers[headerIndex] = header with { Lines = lines };
        }

        return headers;
    }

    public async Task<bool> UpsertPublicationAsync(
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
        // 评估/重新评估入口只写初态；已 PendingPublish/Published 的行绝不能被重置。
        // Blocked -> NeedsEvaluation 是显式重新评估，Revision +1；
        // 纯相同 NeedsEvaluation 幂等重放保持 Revision 不变。
        if (status != SharedHeldOrderPublicationStatus.NeedsEvaluation)
        {
            return false;
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SharedHeldOrderPublications (
                LocalHoldGuid, StoreCode, DeviceCode, Status, Revision, RetryCount,
                ErrorCode, ErrorMessage, PayloadCiphertext, HeldAtIso, CreatedAtIso, UpdatedAtIso,
                LastAttemptAtIso, NextAttemptAtIso)
            VALUES (
                $LocalHoldGuid, $StoreCode, $DeviceCode, 'NeedsEvaluation', 1, 0,
                $ErrorCode, $ErrorMessage, $PayloadCiphertext, $HeldAtIso, $CreatedAtIso, $UpdatedAtIso,
                NULL, NULL)
            ON CONFLICT(LocalHoldGuid) DO UPDATE SET
                Status = 'NeedsEvaluation',
                Revision = CASE
                    WHEN SharedHeldOrderPublications.Status = 'Blocked'
                        THEN SharedHeldOrderPublications.Revision + 1
                    ELSE SharedHeldOrderPublications.Revision
                END,
                RetryCount = 0,
                ErrorCode = excluded.ErrorCode,
                ErrorMessage = excluded.ErrorMessage,
                PayloadCiphertext = COALESCE(excluded.PayloadCiphertext, SharedHeldOrderPublications.PayloadCiphertext),
                LastAttemptAtIso = NULL,
                NextAttemptAtIso = NULL,
                UpdatedAtIso = excluded.UpdatedAtIso
            WHERE SharedHeldOrderPublications.Status IN ('NeedsEvaluation', 'Blocked');
            """;
        command.Parameters.AddWithValue("$LocalHoldGuid", localHoldGuid.ToString("D"));
        command.Parameters.AddWithValue("$StoreCode", storeCode);
        command.Parameters.AddWithValue("$DeviceCode", deviceCode);
        command.Parameters.AddWithValue("$ErrorCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$ErrorMessage", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$PayloadCiphertext", (object?)payloadCiphertext ?? DBNull.Value);
        command.Parameters.AddWithValue("$HeldAtIso", heldAtIso);
        command.Parameters.AddWithValue("$CreatedAtIso", createdAtIso);
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<SharedHeldOrderPublication>> ListDuePublicationsAsync(
        string nowIso,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT LocalHoldGuid, StoreCode, DeviceCode, Status, Revision, RetryCount,
                   ErrorCode, ErrorMessage, PayloadCiphertext, HeldAtIso, CreatedAtIso, UpdatedAtIso,
                   LastAttemptAtIso, NextAttemptAtIso, RemoteRevision, RemoteUpdatedAtIso, ConsumedAtIso
            FROM SharedHeldOrderPublications
            WHERE Status = 'PendingPublish'
              AND ConsumedAtIso IS NULL
              AND (NextAttemptAtIso IS NULL OR NextAttemptAtIso <= $NowIso)
            ORDER BY COALESCE(NextAttemptAtIso, UpdatedAtIso) ASC, CreatedAtIso ASC;
            """;
        command.Parameters.AddWithValue("$NowIso", nowIso);
        var publications = new List<SharedHeldOrderPublication>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            publications.Add(ReadPublication(reader));
        }

        return publications;
    }

    public async Task<bool> TryAdvancePublicationAsync(
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
        if (!IsLegalPublicationTransition(expectedStatus, newStatus))
        {
            return false;
        }

        // Published 必须原子保存服务端 remote revision/updated-at；
        // 其他迁移不允许携带 remote 字段（本地持久层只认可 Published 持有它们）。
        if (newStatus == SharedHeldOrderPublicationStatus.Published)
        {
            if (remoteRevision is null or < 0)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(remoteUpdatedAtIso))
            {
                return false;
            }
        }
        else if (remoteRevision is not null || remoteUpdatedAtIso is not null)
        {
            return false;
        }

        if (expectedStatus == SharedHeldOrderPublicationStatus.PendingPublish
            && newStatus == SharedHeldOrderPublicationStatus.PendingPublish)
        {
            return await TryRecordPublicationFailureAsync(
                localHoldGuid,
                expectedRevision,
                updatedAtIso,
                errorCode,
                errorMessage,
                lastAttemptAtIso,
                nextAttemptAtIso,
                cancellationToken);
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // NeedsEvaluation -> PendingPublish 必须先有非空 payload（评估产物）；
        // 缺 payload 时 UPDATE 匹配 0 行，返回 false 而不是触发 CHECK 异常。
        var requirePayload = newStatus == SharedHeldOrderPublicationStatus.PendingPublish ? 1 : 0;
        command.CommandText =
            """
            UPDATE SharedHeldOrderPublications
            SET Status = $NewStatus,
                Revision = Revision + 1,
                ErrorCode = $ErrorCode,
                ErrorMessage = $ErrorMessage,
                LastAttemptAtIso = NULL,
                NextAttemptAtIso = NULL,
                RemoteRevision = $RemoteRevision,
                RemoteUpdatedAtIso = $RemoteUpdatedAtIso,
                UpdatedAtIso = $UpdatedAtIso
            WHERE LocalHoldGuid = $LocalHoldGuid
              AND Status = $ExpectedStatus
              AND Revision = $ExpectedRevision
              AND ($RequirePayload = 0
                  OR (PayloadCiphertext IS NOT NULL AND LENGTH(PayloadCiphertext) > 0));
            """;
        command.Parameters.AddWithValue("$LocalHoldGuid", localHoldGuid.ToString("D"));
        command.Parameters.AddWithValue("$NewStatus", PublicationStatusText(newStatus));
        command.Parameters.AddWithValue("$ExpectedStatus", PublicationStatusText(expectedStatus));
        command.Parameters.AddWithValue("$ExpectedRevision", expectedRevision);
        command.Parameters.AddWithValue("$ErrorCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$ErrorMessage", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$RemoteRevision", (object?)remoteRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$RemoteUpdatedAtIso", (object?)remoteUpdatedAtIso ?? DBNull.Value);
        command.Parameters.AddWithValue("$RequirePayload", requirePayload);
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        var changes = await command.ExecuteNonQueryAsync(cancellationToken);
        return changes == 1;
    }

    public async Task<SharedHeldOrderPublication?> GetPublicationAsync(
        Guid localHoldGuid,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT LocalHoldGuid, StoreCode, DeviceCode, Status, Revision, RetryCount,
                   ErrorCode, ErrorMessage, PayloadCiphertext, HeldAtIso, CreatedAtIso, UpdatedAtIso,
                   LastAttemptAtIso, NextAttemptAtIso, RemoteRevision, RemoteUpdatedAtIso, ConsumedAtIso
            FROM SharedHeldOrderPublications
            WHERE LocalHoldGuid = $LocalHoldGuid;
            """;
        command.Parameters.AddWithValue("$LocalHoldGuid", localHoldGuid.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPublication(reader) : null;
    }

    public async Task<SharedHeldOrderDeleteStage?> TryStageDeletePendingAsync(
        Guid holdGuid,
        string storeCode,
        string deviceCode,
        string updatedAtIso,
        CancellationToken cancellationToken = default)
    {
        if (holdGuid == Guid.Empty ||
            string.IsNullOrWhiteSpace(storeCode) ||
            string.IsNullOrWhiteSpace(deviceCode) ||
            string.IsNullOrWhiteSpace(updatedAtIso))
        {
            return null;
        }

        var normalizedStoreCode = storeCode.Trim().ToUpperInvariant();
        var normalizedDeviceCode = deviceCode.Trim().ToUpperInvariant();
        var holdGuidText = holdGuid.ToString("D");
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        string suspendedAtIso;
        await using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.Transaction = transaction;
            orderCommand.CommandText =
                """
                SELECT SuspendedAt
                FROM SuspendedOrders
                WHERE SuspendedOrderGuid = $HoldGuid
                  AND UPPER(StoreCode) = $StoreCode
                  AND UPPER(DeviceCode) = $DeviceCode
                  AND Status = $PendingStatus;
                """;
            orderCommand.Parameters.AddWithValue("$HoldGuid", holdGuidText);
            orderCommand.Parameters.AddWithValue("$StoreCode", normalizedStoreCode);
            orderCommand.Parameters.AddWithValue("$DeviceCode", normalizedDeviceCode);
            orderCommand.Parameters.AddWithValue("$PendingStatus", (int)SuspendedOrderStatus.Pending);
            var suspendedAt = await orderCommand.ExecuteScalarAsync(cancellationToken);
            if (suspendedAt is not string value)
            {
                return null;
            }

            suspendedAtIso = value;
        }

        await using (var claimCommand = connection.CreateCommand())
        {
            claimCommand.Transaction = transaction;
            claimCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM SharedHeldOrderClaims
                WHERE HoldGuid = $HoldGuid
                  AND Status IN ('Prepared', 'Active');
                """;
            claimCommand.Parameters.AddWithValue("$HoldGuid", holdGuidText);
            if (Convert.ToInt64(await claimCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0)
            {
                return null;
            }
        }

        var publicationExists = false;
        var remoteCancellationRequired = false;
        await using (var publicationCommand = connection.CreateCommand())
        {
            publicationCommand.Transaction = transaction;
            publicationCommand.CommandText =
                """
                SELECT StoreCode, DeviceCode, Status, ErrorCode, RemoteRevision, ConsumedAtIso
                FROM SharedHeldOrderPublications
                WHERE LocalHoldGuid = $HoldGuid;
                """;
            publicationCommand.Parameters.AddWithValue("$HoldGuid", holdGuidText);
            await using var reader = await publicationCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                publicationExists = true;
                if (!ReadString(reader, "StoreCode").Equals(normalizedStoreCode, StringComparison.OrdinalIgnoreCase) ||
                    !ReadString(reader, "DeviceCode").Equals(normalizedDeviceCode, StringComparison.OrdinalIgnoreCase) ||
                    ReadNullableString(reader, "ConsumedAtIso") is not null)
                {
                    return null;
                }

                var status = ReadString(reader, "Status");
                var errorCode = ReadNullableString(reader, "ErrorCode");
                remoteCancellationRequired =
                    errorCode == LocalDeletePendingRemote ||
                    status is PublicationPendingPublish or PublicationPublished ||
                    ReadNullableInt64(reader, "RemoteRevision") is not null;
            }
        }

        var deleteMarker = remoteCancellationRequired
            ? LocalDeletePendingRemote
            : LocalDeletePendingLocal;
        await using (var stageCommand = connection.CreateCommand())
        {
            stageCommand.Transaction = transaction;
            if (publicationExists)
            {
                stageCommand.CommandText =
                    """
                    UPDATE SharedHeldOrderPublications
                    SET Status = 'Blocked',
                        Revision = Revision + 1,
                        RetryCount = 0,
                        ErrorCode = $DeleteMarker,
                        ErrorMessage = NULL,
                        UpdatedAtIso = $UpdatedAtIso,
                        LastAttemptAtIso = NULL,
                        NextAttemptAtIso = NULL,
                        RemoteRevision = NULL,
                        RemoteUpdatedAtIso = NULL
                    WHERE LocalHoldGuid = $HoldGuid
                      AND UPPER(StoreCode) = $StoreCode
                      AND UPPER(DeviceCode) = $DeviceCode
                      AND ConsumedAtIso IS NULL;
                    """;
            }
            else
            {
                stageCommand.CommandText =
                    """
                    INSERT INTO SharedHeldOrderPublications (
                        LocalHoldGuid, StoreCode, DeviceCode, Status, Revision, RetryCount,
                        ErrorCode, ErrorMessage, PayloadCiphertext, HeldAtIso, CreatedAtIso,
                        UpdatedAtIso, LastAttemptAtIso, NextAttemptAtIso,
                        RemoteRevision, RemoteUpdatedAtIso, ConsumedAtIso)
                    VALUES (
                        $HoldGuid, $StoreCode, $DeviceCode, 'Blocked', 1, 0,
                        $DeleteMarker, NULL, NULL, $HeldAtIso, $UpdatedAtIso,
                        $UpdatedAtIso, NULL, NULL, NULL, NULL, NULL);
                    """;
                stageCommand.Parameters.AddWithValue("$HeldAtIso", suspendedAtIso);
            }

            stageCommand.Parameters.AddWithValue("$HoldGuid", holdGuidText);
            stageCommand.Parameters.AddWithValue("$StoreCode", normalizedStoreCode);
            stageCommand.Parameters.AddWithValue("$DeviceCode", normalizedDeviceCode);
            stageCommand.Parameters.AddWithValue("$DeleteMarker", deleteMarker);
            stageCommand.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
            if (await stageCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return null;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new SharedHeldOrderDeleteStage(holdGuid, remoteCancellationRequired);
    }

    public async Task<bool> TryCompleteDeletePendingAsync(
        Guid holdGuid,
        string storeCode,
        string deviceCode,
        string completedAtIso,
        CancellationToken cancellationToken = default)
    {
        if (holdGuid == Guid.Empty ||
            string.IsNullOrWhiteSpace(storeCode) ||
            string.IsNullOrWhiteSpace(deviceCode) ||
            string.IsNullOrWhiteSpace(completedAtIso))
        {
            return false;
        }

        var normalizedStoreCode = storeCode.Trim().ToUpperInvariant();
        var normalizedDeviceCode = deviceCode.Trim().ToUpperInvariant();
        var holdGuidText = holdGuid.ToString("D");
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        await using (var claimCommand = connection.CreateCommand())
        {
            claimCommand.Transaction = transaction;
            claimCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM SharedHeldOrderClaims
                WHERE HoldGuid = $HoldGuid
                  AND Status IN ('Prepared', 'Active');
                """;
            claimCommand.Parameters.AddWithValue("$HoldGuid", holdGuidText);
            if (Convert.ToInt64(await claimCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0)
            {
                return false;
            }
        }

        await using (var publicationCommand = connection.CreateCommand())
        {
            publicationCommand.Transaction = transaction;
            publicationCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM SharedHeldOrderPublications
                WHERE LocalHoldGuid = $HoldGuid
                  AND UPPER(StoreCode) = $StoreCode
                  AND UPPER(DeviceCode) = $DeviceCode
                  AND Status = 'Blocked'
                  AND ErrorCode IN ('LOCAL_DELETE_PENDING_REMOTE', 'LOCAL_DELETE_PENDING_LOCAL')
                  AND ConsumedAtIso IS NULL;
                """;
            publicationCommand.Parameters.AddWithValue("$HoldGuid", holdGuidText);
            publicationCommand.Parameters.AddWithValue("$StoreCode", normalizedStoreCode);
            publicationCommand.Parameters.AddWithValue("$DeviceCode", normalizedDeviceCode);
            if (Convert.ToInt64(await publicationCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1)
            {
                return false;
            }
        }

        await using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.Transaction = transaction;
            orderCommand.CommandText =
                """
                UPDATE SuspendedOrders
                SET Status = $CanceledStatus
                WHERE SuspendedOrderGuid = $HoldGuid
                  AND UPPER(StoreCode) = $StoreCode
                  AND UPPER(DeviceCode) = $DeviceCode
                  AND Status = $PendingStatus;
                """;
            orderCommand.Parameters.AddWithValue("$CanceledStatus", (int)SuspendedOrderStatus.Canceled);
            orderCommand.Parameters.AddWithValue("$PendingStatus", (int)SuspendedOrderStatus.Pending);
            orderCommand.Parameters.AddWithValue("$HoldGuid", holdGuidText);
            orderCommand.Parameters.AddWithValue("$StoreCode", normalizedStoreCode);
            orderCommand.Parameters.AddWithValue("$DeviceCode", normalizedDeviceCode);
            if (await orderCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }
        }

        await using (var publicationCommand = connection.CreateCommand())
        {
            publicationCommand.Transaction = transaction;
            publicationCommand.CommandText =
                """
                UPDATE SharedHeldOrderPublications
                SET Revision = Revision + 1,
                    PayloadCiphertext = NULL,
                    ConsumedAtIso = $CompletedAtIso,
                    UpdatedAtIso = $CompletedAtIso
                WHERE LocalHoldGuid = $HoldGuid
                  AND UPPER(StoreCode) = $StoreCode
                  AND UPPER(DeviceCode) = $DeviceCode
                  AND Status = 'Blocked'
                  AND ErrorCode IN ('LOCAL_DELETE_PENDING_REMOTE', 'LOCAL_DELETE_PENDING_LOCAL')
                  AND ConsumedAtIso IS NULL;
                """;
            publicationCommand.Parameters.AddWithValue("$CompletedAtIso", completedAtIso);
            publicationCommand.Parameters.AddWithValue("$HoldGuid", holdGuidText);
            publicationCommand.Parameters.AddWithValue("$StoreCode", normalizedStoreCode);
            publicationCommand.Parameters.AddWithValue("$DeviceCode", normalizedDeviceCode);
            if (await publicationCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryStagePendingPublishAsync(
        Guid localHoldGuid,
        int expectedRevision,
        SharedHeldOrderCanonicalPayload payload,
        string updatedAtIso,
        CancellationToken cancellationToken = default)
    {
        // payload 必须先序列化并加密，再与状态切换同一条 UPDATE 原子提交；
        // 明文只在内存中短暂存在，绝不写库。
        var ciphertext = protector.Protect(serializer.Serialize(payload));
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SharedHeldOrderPublications
            SET Status = 'PendingPublish',
                Revision = Revision + 1,
                PayloadCiphertext = $PayloadCiphertext,
                ErrorCode = NULL,
                ErrorMessage = NULL,
                LastAttemptAtIso = NULL,
                NextAttemptAtIso = NULL,
                UpdatedAtIso = $UpdatedAtIso
            WHERE LocalHoldGuid = $LocalHoldGuid
              AND Status = 'NeedsEvaluation'
              AND Revision = $ExpectedRevision;
            """;
        command.Parameters.AddWithValue("$LocalHoldGuid", localHoldGuid.ToString("D"));
        command.Parameters.AddWithValue("$ExpectedRevision", expectedRevision);
        command.Parameters.AddWithValue("$PayloadCiphertext", ciphertext);
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryBlockPublicationAsync(
        Guid localHoldGuid,
        int expectedRevision,
        string errorCode,
        string errorMessage,
        string updatedAtIso,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SharedHeldOrderPublications
            SET Status = 'Blocked',
                Revision = Revision + 1,
                ErrorCode = $ErrorCode,
                ErrorMessage = $ErrorMessage,
                LastAttemptAtIso = NULL,
                NextAttemptAtIso = NULL,
                UpdatedAtIso = $UpdatedAtIso
            WHERE LocalHoldGuid = $LocalHoldGuid
              AND Status = 'NeedsEvaluation'
              AND Revision = $ExpectedRevision;
            """;
        command.Parameters.AddWithValue("$LocalHoldGuid", localHoldGuid.ToString("D"));
        command.Parameters.AddWithValue("$ExpectedRevision", expectedRevision);
        command.Parameters.AddWithValue("$ErrorCode", errorCode);
        command.Parameters.AddWithValue("$ErrorMessage", errorMessage);
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<SharedHeldOrderCanonicalPayload?> GetPublicationPayloadAsync(
        Guid localHoldGuid,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Status, PayloadCiphertext, ConsumedAtIso
            FROM SharedHeldOrderPublications
            WHERE LocalHoldGuid = $LocalHoldGuid;
            """;
        command.Parameters.AddWithValue("$LocalHoldGuid", localHoldGuid.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var status = ReadString(reader, "Status");
        if (status is not (PublicationPendingPublish or PublicationPublished))
        {
            return null;
        }

        // 本地挂单已被成交订单消费：不再可离线 recall/发布，payload 视为不可恢复。
        if (ReadNullableString(reader, "ConsumedAtIso") is not null)
        {
            return null;
        }

        var ciphertext = ReadNullableBlob(reader, "PayloadCiphertext");
        if (ciphertext is null || ciphertext.Length == 0)
        {
            return null;
        }

        return serializer.Deserialize(protector.Unprotect(ciphertext));
    }

    public async Task<bool> TrySavePreparedClaimAsync(
        SharedHeldOrderClaimDraft draft,
        CancellationToken cancellationToken = default)
    {
        var ciphertext = protector.Protect(serializer.Serialize(draft.Payload));
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO SharedHeldOrderClaims (
                    ClaimId, HoldGuid, StoreCode, DeviceCode, Source, Status,
                    PrepareIdempotencyKey, PayloadCiphertext,
                    ServerRevision, ExpiresAtIso, BoundOrderGuid, CreatedAtIso, UpdatedAtIso)
                VALUES (
                    $ClaimId, $HoldGuid, $StoreCode, $DeviceCode, $Source, 'Prepared',
                    $PrepareIdempotencyKey, $PayloadCiphertext,
                    NULL, $ExpiresAtIso, NULL, $CreatedAtIso, $CreatedAtIso);
                """;
            command.Parameters.AddWithValue("$ClaimId", draft.ClaimId.ToString("D"));
            command.Parameters.AddWithValue("$HoldGuid", draft.HoldGuid.ToString("D"));
            command.Parameters.AddWithValue("$StoreCode", draft.StoreCode);
            command.Parameters.AddWithValue("$DeviceCode", draft.DeviceCode);
            command.Parameters.AddWithValue("$Source", ClaimSourceText(draft.Source));
            command.Parameters.AddWithValue("$PrepareIdempotencyKey", draft.PrepareIdempotencyKey);
            command.Parameters.AddWithValue("$PayloadCiphertext", ciphertext);
            command.Parameters.AddWithValue("$ExpiresAtIso", (object?)draft.ExpiresAtIso ?? DBNull.Value);
            command.Parameters.AddWithValue("$CreatedAtIso", draft.CreatedAtIso);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            // 唯一约束冲突：同一 claim + 同一 prepare key 且仍为 Prepared 视为幂等重放；
            // 重放必须同时匹配 HoldGuid/store(规范化)/device(规范化)/Source、
            // ExpiresAt 与解密后的 canonical payload 字节（CreatedAt 可不同），
            // 任一不同即 fence/idempotency 输家，返回 false。
            var existing = await GetClaimAsync(draft.ClaimId, cancellationToken);
            return existing is not null
                && string.Equals(existing.PrepareIdempotencyKey, draft.PrepareIdempotencyKey, StringComparison.Ordinal)
                && existing.Status == SharedHeldOrderClaimStatus.Prepared
                && existing.HoldGuid == draft.HoldGuid
                && string.Equals(
                    existing.StoreCode.Trim().ToUpperInvariant(),
                    draft.StoreCode.Trim().ToUpperInvariant(),
                    StringComparison.Ordinal)
                && string.Equals(
                    existing.DeviceCode.Trim().ToUpperInvariant(),
                    draft.DeviceCode.Trim().ToUpperInvariant(),
                    StringComparison.Ordinal)
                && existing.Source == draft.Source
                && string.Equals(existing.ExpiresAtIso, draft.ExpiresAtIso, StringComparison.Ordinal)
                && PayloadBytesMatch(existing.PayloadCiphertext, draft.Payload);
        }
    }

    public async Task<bool> TryActivateClaimAsync(
        Guid claimId,
        string prepareIdempotencyKey,
        string activateIdempotencyKey,
        long? serverRevision,
        string updatedAtIso,
        CancellationToken cancellationToken = default)
    {
        // 服务端 revision 允许空（离线激活），但一旦提供必须非负。
        if (serverRevision is < 0)
        {
            return false;
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SharedHeldOrderClaims
            SET Status = 'Active',
                ActivateIdempotencyKey = $ActivateIdempotencyKey,
                ServerRevision = $ServerRevision,
                UpdatedAtIso = $UpdatedAtIso
            WHERE ClaimId = $ClaimId
              AND Status = 'Prepared'
              AND PrepareIdempotencyKey = $PrepareIdempotencyKey
              AND ActivateIdempotencyKey IS NULL;
            """;
        command.Parameters.AddWithValue("$ClaimId", claimId.ToString("D"));
        command.Parameters.AddWithValue("$PrepareIdempotencyKey", prepareIdempotencyKey);
        command.Parameters.AddWithValue("$ActivateIdempotencyKey", activateIdempotencyKey);
        command.Parameters.AddWithValue("$ServerRevision", (object?)serverRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return true;
        }

        var existing = await GetClaimAsync(claimId, cancellationToken);
        return existing is not null
            && existing.Status == SharedHeldOrderClaimStatus.Active
            && string.Equals(existing.ActivateIdempotencyKey, activateIdempotencyKey, StringComparison.Ordinal);
    }

    public async Task<bool> TryBindOrderAsync(
        Guid claimId,
        string activateIdempotencyKey,
        string boundOrderGuid,
        string updatedAtIso,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SharedHeldOrderClaims
            SET BoundOrderGuid = $BoundOrderGuid,
                UpdatedAtIso = $UpdatedAtIso
            WHERE ClaimId = $ClaimId
              AND Status = 'Active'
              AND ActivateIdempotencyKey = $ActivateIdempotencyKey
              AND BoundOrderGuid IS NULL;
            """;
        command.Parameters.AddWithValue("$ClaimId", claimId.ToString("D"));
        command.Parameters.AddWithValue("$ActivateIdempotencyKey", activateIdempotencyKey);
        command.Parameters.AddWithValue("$BoundOrderGuid", boundOrderGuid);
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return true;
        }

        var existing = await GetClaimAsync(claimId, cancellationToken);
        return existing is not null
            && existing.Status == SharedHeldOrderClaimStatus.Active
            && string.Equals(existing.ActivateIdempotencyKey, activateIdempotencyKey, StringComparison.Ordinal)
            && string.Equals(existing.BoundOrderGuid, boundOrderGuid, StringComparison.Ordinal);
    }

    public async Task<bool> TryCompleteClaimAsync(
        Guid claimId,
        string activateIdempotencyKey,
        string releaseIdempotencyKey,
        string updatedAtIso,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SharedHeldOrderClaims
            SET Status = 'Completed',
                ReleaseIdempotencyKey = $ReleaseIdempotencyKey,
                UpdatedAtIso = $UpdatedAtIso
            WHERE ClaimId = $ClaimId
              AND Status = 'Active'
              AND ActivateIdempotencyKey = $ActivateIdempotencyKey
              AND BoundOrderGuid IS NOT NULL
              AND ReleaseIdempotencyKey IS NULL;
            """;
        command.Parameters.AddWithValue("$ClaimId", claimId.ToString("D"));
        command.Parameters.AddWithValue("$ActivateIdempotencyKey", activateIdempotencyKey);
        command.Parameters.AddWithValue("$ReleaseIdempotencyKey", releaseIdempotencyKey);
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return true;
        }

        var existing = await GetClaimAsync(claimId, cancellationToken);
        return existing is not null
            && existing.Status == SharedHeldOrderClaimStatus.Completed
            && string.Equals(existing.ReleaseIdempotencyKey, releaseIdempotencyKey, StringComparison.Ordinal);
    }

    public async Task<bool> TryReleaseClaimAsync(
        Guid claimId,
        string releaseIdempotencyKey,
        SharedHeldOrderClaimStatus expectedStatus,
        string updatedAtIso,
        CancellationToken cancellationToken = default)
    {
        if (expectedStatus is not (SharedHeldOrderClaimStatus.Prepared or SharedHeldOrderClaimStatus.Active))
        {
            return false;
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SharedHeldOrderClaims
            SET Status = 'Released',
                ReleaseIdempotencyKey = $ReleaseIdempotencyKey,
                UpdatedAtIso = $UpdatedAtIso
            WHERE ClaimId = $ClaimId
              AND Status = $ExpectedStatus
              AND ReleaseIdempotencyKey IS NULL
              AND BoundOrderGuid IS NULL;
            """;
        command.Parameters.AddWithValue("$ClaimId", claimId.ToString("D"));
        command.Parameters.AddWithValue("$ReleaseIdempotencyKey", releaseIdempotencyKey);
        command.Parameters.AddWithValue("$ExpectedStatus", ClaimStatusText(expectedStatus));
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return true;
        }

        var existing = await GetClaimAsync(claimId, cancellationToken);
        return existing is not null
            && existing.Status == SharedHeldOrderClaimStatus.Released
            && string.Equals(existing.ReleaseIdempotencyKey, releaseIdempotencyKey, StringComparison.Ordinal);
    }

    public async Task<bool> TryExpirePreparedRemoteClaimAsync(
        Guid claimId,
        string releaseIdempotencyKey,
        string nowIso,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SharedHeldOrderClaims
            SET Status = 'Released',
                ReleaseIdempotencyKey = $ReleaseIdempotencyKey,
                UpdatedAtIso = $NowIso
            WHERE ClaimId = $ClaimId
              AND Status = 'Prepared'
              AND Source = 'RemoteClaim'
              AND ReleaseIdempotencyKey IS NULL
              AND ExpiresAtIso IS NOT NULL
              AND ExpiresAtIso <= $NowIso;
            """;
        command.Parameters.AddWithValue("$ClaimId", claimId.ToString("D"));
        command.Parameters.AddWithValue("$ReleaseIdempotencyKey", releaseIdempotencyKey);
        command.Parameters.AddWithValue("$NowIso", nowIso);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return true;
        }

        var existing = await GetClaimAsync(claimId, cancellationToken);
        return existing is not null
            && existing.Status == SharedHeldOrderClaimStatus.Released
            && string.Equals(existing.ReleaseIdempotencyKey, releaseIdempotencyKey, StringComparison.Ordinal);
    }

    public async Task<bool> TryForceReleaseClaimAsync(
        Guid claimId,
        string releaseIdempotencyKey,
        SharedHeldOrderClaimStatus expectedStatus,
        string updatedAtIso,
        CancellationToken cancellationToken = default)
    {
        // 强制释放只允许从 Prepared/Active 离开；终态本地不能重开。
        if (expectedStatus is not (SharedHeldOrderClaimStatus.Prepared or SharedHeldOrderClaimStatus.Active))
        {
            return false;
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SharedHeldOrderClaims
            SET Status = 'Released',
                ReleaseIdempotencyKey = $ReleaseIdempotencyKey,
                BoundOrderGuid = NULL,
                UpdatedAtIso = $UpdatedAtIso
            WHERE ClaimId = $ClaimId
              AND Status = $ExpectedStatus
              AND ReleaseIdempotencyKey IS NULL
              AND SupersedeIdempotencyKey IS NULL;
            """;
        command.Parameters.AddWithValue("$ClaimId", claimId.ToString("D"));
        command.Parameters.AddWithValue("$ReleaseIdempotencyKey", releaseIdempotencyKey);
        command.Parameters.AddWithValue("$ExpectedStatus", ClaimStatusText(expectedStatus));
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return true;
        }

        var existing = await GetClaimAsync(claimId, cancellationToken);
        return existing is not null
            && existing.Status == SharedHeldOrderClaimStatus.Released
            && string.Equals(existing.ReleaseIdempotencyKey, releaseIdempotencyKey, StringComparison.Ordinal);
    }

    public async Task<bool> TrySupersedeClaimAsync(
        Guid claimId,
        string supersedeIdempotencyKey,
        SharedHeldOrderClaimStatus expectedStatus,
        string updatedAtIso,
        CancellationToken cancellationToken = default)
    {
        // 服务端 OfflineOrigin 成交调和：Prepared（activate 空）或 Active（保留 activate key）
        // 且未绑定订单才允许 Superseded；已绑定订单的 Active 绝不能 supersede。
        if (expectedStatus is not (
                SharedHeldOrderClaimStatus.Prepared
                or SharedHeldOrderClaimStatus.Active))
        {
            return false;
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SharedHeldOrderClaims
            SET Status = 'Superseded',
                SupersedeIdempotencyKey = $SupersedeIdempotencyKey,
                UpdatedAtIso = $UpdatedAtIso
            WHERE ClaimId = $ClaimId
              AND Status = $ExpectedStatus
              AND ReleaseIdempotencyKey IS NULL
              AND BoundOrderGuid IS NULL
              AND SupersedeIdempotencyKey IS NULL
              AND (($ExpectedStatus = 'Prepared' AND ActivateIdempotencyKey IS NULL)
                   OR ($ExpectedStatus = 'Active' AND ActivateIdempotencyKey IS NOT NULL));
            """;
        command.Parameters.AddWithValue("$ClaimId", claimId.ToString("D"));
        command.Parameters.AddWithValue("$SupersedeIdempotencyKey", supersedeIdempotencyKey);
        command.Parameters.AddWithValue("$ExpectedStatus", ClaimStatusText(expectedStatus));
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return true;
        }

        var existing = await GetClaimAsync(claimId, cancellationToken);
        return existing is not null
            && existing.Status == SharedHeldOrderClaimStatus.Superseded
            && string.Equals(
                existing.SupersedeIdempotencyKey,
                supersedeIdempotencyKey,
                StringComparison.Ordinal);
    }

    public async Task<SharedHeldOrderClaimRecord?> GetClaimAsync(
        Guid claimId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ClaimId, HoldGuid, StoreCode, DeviceCode, Source, Status,
                   PrepareIdempotencyKey, ActivateIdempotencyKey, ReleaseIdempotencyKey,
                   PayloadCiphertext, ServerRevision, ExpiresAtIso, BoundOrderGuid,
                   SupersedeIdempotencyKey, CreatedAtIso, UpdatedAtIso
            FROM SharedHeldOrderClaims
            WHERE ClaimId = $ClaimId;
            """;
        command.Parameters.AddWithValue("$ClaimId", claimId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SharedHeldOrderClaimRecord(
            ReadGuid(reader, "ClaimId"),
            ReadGuid(reader, "HoldGuid"),
            ReadString(reader, "StoreCode"),
            ReadString(reader, "DeviceCode"),
            ParseClaimSource(ReadString(reader, "Source")),
            ParseClaimStatus(ReadString(reader, "Status")),
            ReadString(reader, "PrepareIdempotencyKey"),
            ReadNullableString(reader, "ActivateIdempotencyKey"),
            ReadNullableString(reader, "ReleaseIdempotencyKey"),
            ReadBlob(reader, "PayloadCiphertext"),
            ReadNullableInt64(reader, "ServerRevision"),
            ReadNullableString(reader, "ExpiresAtIso"),
            ReadNullableString(reader, "BoundOrderGuid"),
            ReadNullableString(reader, "SupersedeIdempotencyKey"),
            ReadString(reader, "CreatedAtIso"),
            ReadString(reader, "UpdatedAtIso"));
    }

    public async Task<IReadOnlyList<SharedHeldOrderClaimRecovery>> FindRecoverableClaimsAsync(
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ClaimId, HoldGuid, StoreCode, DeviceCode, Source, Status,
                   PrepareIdempotencyKey, ActivateIdempotencyKey, ReleaseIdempotencyKey,
                   PayloadCiphertext, ServerRevision, ExpiresAtIso, BoundOrderGuid,
                   SupersedeIdempotencyKey, CreatedAtIso, UpdatedAtIso
            FROM SharedHeldOrderClaims
            WHERE UPPER(StoreCode) = $StoreCode
              AND UPPER(DeviceCode) = $DeviceCode
              AND Status IN ('Prepared', 'Active')
            ORDER BY CreatedAtIso ASC;
            """;
        command.Parameters.AddWithValue("$StoreCode", storeCode.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$DeviceCode", deviceCode.Trim().ToUpperInvariant());

        var recoveries = new List<SharedHeldOrderClaimRecovery>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var ciphertext = ReadBlob(reader, "PayloadCiphertext");
            recoveries.Add(new SharedHeldOrderClaimRecovery(
                ReadGuid(reader, "ClaimId"),
                ReadGuid(reader, "HoldGuid"),
                ReadString(reader, "StoreCode"),
                ReadString(reader, "DeviceCode"),
                ParseClaimSource(ReadString(reader, "Source")),
                ParseClaimStatus(ReadString(reader, "Status")),
                ReadString(reader, "PrepareIdempotencyKey"),
                ReadNullableString(reader, "ActivateIdempotencyKey"),
                ReadNullableString(reader, "ReleaseIdempotencyKey"),
                serializer.Deserialize(protector.Unprotect(ciphertext)),
                ReadNullableInt64(reader, "ServerRevision"),
                ReadNullableString(reader, "ExpiresAtIso"),
                ReadNullableString(reader, "BoundOrderGuid"),
                ReadNullableString(reader, "SupersedeIdempotencyKey"),
                ReadString(reader, "CreatedAtIso"),
                ReadString(reader, "UpdatedAtIso")));
        }

        return recoveries;
    }

    /// <summary>发布失败路径：保持 PendingPublish、RetryCount +1，并补齐尝试/下次尝试时间。</summary>
    private async Task<bool> TryRecordPublicationFailureAsync(
        Guid localHoldGuid,
        int expectedRevision,
        string updatedAtIso,
        string? errorCode,
        string? errorMessage,
        string? lastAttemptAtIso,
        string? nextAttemptAtIso,
        CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        int? currentRetryCount;
        await using (var readCommand = connection.CreateCommand())
        {
            readCommand.CommandText =
                """
                SELECT RetryCount
                FROM SharedHeldOrderPublications
                WHERE LocalHoldGuid = $LocalHoldGuid
                  AND Status = 'PendingPublish'
                  AND Revision = $ExpectedRevision;
                """;
            readCommand.Parameters.AddWithValue("$LocalHoldGuid", localHoldGuid.ToString("D"));
            readCommand.Parameters.AddWithValue("$ExpectedRevision", expectedRevision);
            var retryCountScalar = await readCommand.ExecuteScalarAsync(cancellationToken);
            currentRetryCount = retryCountScalar is null
                ? null
                : Convert.ToInt32(retryCountScalar, CultureInfo.InvariantCulture);
        }

        if (currentRetryCount is not int retryCount)
        {
            return false;
        }

        // 最小补齐：LastAttemptAtIso 缺失用本次失败时间；NextAttemptAtIso 缺失按新计数退避。
        var lastAttempt = lastAttemptAtIso ?? updatedAtIso;
        var nextAttempt = nextAttemptAtIso ?? AddIsoMilliseconds(lastAttempt, PublishRetryDelayMs(retryCount + 1));

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SharedHeldOrderPublications
            SET Revision = Revision + 1,
                RetryCount = RetryCount + 1,
                ErrorCode = $ErrorCode,
                ErrorMessage = $ErrorMessage,
                LastAttemptAtIso = $LastAttemptAtIso,
                NextAttemptAtIso = $NextAttemptAtIso,
                UpdatedAtIso = $UpdatedAtIso
            WHERE LocalHoldGuid = $LocalHoldGuid
              AND Status = 'PendingPublish'
              AND Revision = $ExpectedRevision
              AND RetryCount = $CurrentRetryCount;
            """;
        command.Parameters.AddWithValue("$LocalHoldGuid", localHoldGuid.ToString("D"));
        command.Parameters.AddWithValue("$ExpectedRevision", expectedRevision);
        command.Parameters.AddWithValue("$ErrorCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$ErrorMessage", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$LastAttemptAtIso", lastAttempt);
        command.Parameters.AddWithValue("$NextAttemptAtIso", nextAttempt);
        command.Parameters.AddWithValue("$UpdatedAtIso", updatedAtIso);
        command.Parameters.AddWithValue("$CurrentRetryCount", retryCount);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>本地 publication 只允许：NeedsEvaluation/PendingPublish 显式进入 Blocked，其余按主链推进。</summary>
    private static bool IsLegalPublicationTransition(
        SharedHeldOrderPublicationStatus expected,
        SharedHeldOrderPublicationStatus next)
    {
        return expected switch
        {
            SharedHeldOrderPublicationStatus.NeedsEvaluation =>
                next is SharedHeldOrderPublicationStatus.PendingPublish
                    or SharedHeldOrderPublicationStatus.Blocked,
            SharedHeldOrderPublicationStatus.PendingPublish =>
                next is SharedHeldOrderPublicationStatus.PendingPublish
                    or SharedHeldOrderPublicationStatus.Published
                    or SharedHeldOrderPublicationStatus.Blocked,
            _ => false
        };
    }

    /// <summary>发布失败退避：第 1 次失败后 30s，随后指数递增，封顶 1 小时（与 iPad M40 一致）。</summary>
    private static int PublishRetryDelayMs(int attemptCount)
    {
        if (attemptCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCount), attemptCount, "attempt count 必须为正。");
        }

        const int baseMs = 30_000;
        const int capMs = 3_600_000;
        var exponent = Math.Min(attemptCount - 1, 20);
        return (int)Math.Min((long)baseMs * (1L << exponent), capMs);
    }

    private static string AddIsoMilliseconds(string iso, int milliseconds)
    {
        var parsed = DateTimeOffset.Parse(
            iso,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        return parsed
            .AddMilliseconds(milliseconds)
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>幂等重放校验：解密存量密文后与重放 payload 的序列化字节逐位比较。</summary>
    private bool PayloadBytesMatch(byte[] ciphertext, SharedHeldOrderCanonicalPayload payload)
    {
        var expected = serializer.Serialize(payload);
        var actual = protector.Unprotect(ciphertext);
        return expected.AsSpan().SequenceEqual(actual);
    }

    private static SharedHeldOrderPublication ReadPublication(SqliteDataReader reader)
    {
        return new SharedHeldOrderPublication(
            ReadGuid(reader, "LocalHoldGuid"),
            ReadString(reader, "StoreCode"),
            ReadString(reader, "DeviceCode"),
            ParsePublicationStatus(ReadString(reader, "Status")),
            reader.GetInt32(reader.GetOrdinal("Revision")),
            reader.GetInt32(reader.GetOrdinal("RetryCount")),
            ReadNullableString(reader, "ErrorCode"),
            ReadNullableString(reader, "ErrorMessage"),
            ReadNullableBlob(reader, "PayloadCiphertext"),
            ReadString(reader, "HeldAtIso"),
            ReadString(reader, "CreatedAtIso"),
            ReadString(reader, "UpdatedAtIso"),
            ReadNullableString(reader, "LastAttemptAtIso"),
            ReadNullableString(reader, "NextAttemptAtIso"),
            ReadNullableInt64(reader, "RemoteRevision"),
            ReadNullableString(reader, "RemoteUpdatedAtIso"),
            ReadNullableString(reader, "ConsumedAtIso"));
    }

    private static string PublicationStatusText(SharedHeldOrderPublicationStatus status)
    {
        return status switch
        {
            SharedHeldOrderPublicationStatus.NeedsEvaluation => PublicationNeedsEvaluation,
            SharedHeldOrderPublicationStatus.PendingPublish => PublicationPendingPublish,
            SharedHeldOrderPublicationStatus.Published => PublicationPublished,
            SharedHeldOrderPublicationStatus.Blocked => PublicationBlocked,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "未知本地 publication 状态。")
        };
    }

    private static SharedHeldOrderPublicationStatus ParsePublicationStatus(string text)
    {
        return text switch
        {
            PublicationNeedsEvaluation => SharedHeldOrderPublicationStatus.NeedsEvaluation,
            PublicationPendingPublish => SharedHeldOrderPublicationStatus.PendingPublish,
            PublicationPublished => SharedHeldOrderPublicationStatus.Published,
            PublicationBlocked => SharedHeldOrderPublicationStatus.Blocked,
            _ => throw new InvalidDataException($"未知本地 publication 状态: {text}")
        };
    }

    private static SharedHeldOrderClaimStatus ParseClaimStatus(string text)
    {
        return text switch
        {
            ClaimPrepared => SharedHeldOrderClaimStatus.Prepared,
            ClaimActive => SharedHeldOrderClaimStatus.Active,
            ClaimCompleted => SharedHeldOrderClaimStatus.Completed,
            ClaimReleased => SharedHeldOrderClaimStatus.Released,
            ClaimSuperseded => SharedHeldOrderClaimStatus.Superseded,
            _ => throw new InvalidDataException($"未知 claim 状态: {text}")
        };
    }

    private static string ClaimStatusText(SharedHeldOrderClaimStatus status)
    {
        return status switch
        {
            SharedHeldOrderClaimStatus.Prepared => ClaimPrepared,
            SharedHeldOrderClaimStatus.Active => ClaimActive,
            SharedHeldOrderClaimStatus.Completed => ClaimCompleted,
            SharedHeldOrderClaimStatus.Released => ClaimReleased,
            SharedHeldOrderClaimStatus.Superseded => ClaimSuperseded,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "未知 claim 状态。")
        };
    }

    private static SharedHeldOrderClaimSource ParseClaimSource(string text)
    {
        return text switch
        {
            ClaimSourceRemoteClaim => SharedHeldOrderClaimSource.RemoteClaim,
            ClaimSourceOfflineOrigin => SharedHeldOrderClaimSource.OfflineOrigin,
            _ => throw new InvalidDataException($"未知 claim 来源: {text}")
        };
    }

    private static string ClaimSourceText(SharedHeldOrderClaimSource source)
    {
        return source switch
        {
            SharedHeldOrderClaimSource.RemoteClaim => ClaimSourceRemoteClaim,
            SharedHeldOrderClaimSource.OfflineOrigin => ClaimSourceOfflineOrigin,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "未知 claim 来源。")
        };
    }

    private static Guid ReadGuid(SqliteDataReader reader, string column)
    {
        return Guid.ParseExact(reader.GetString(reader.GetOrdinal(column)), "D");
    }

    private static Guid? ReadNullableGuid(SqliteDataReader reader, string column)
    {
        var value = ReadNullableString(reader, column);
        return value is null ? null : Guid.ParseExact(value, "D");
    }

    private static string ReadString(SqliteDataReader reader, string column)
    {
        return reader.GetString(reader.GetOrdinal(column));
    }

    private static string? ReadNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt32(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static long? ReadNullableInt64(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static decimal ReadDecimal(SqliteDataReader reader, string column)
    {
        return decimal.Parse(
            reader.GetString(reader.GetOrdinal(column)),
            NumberStyles.Number,
            CultureInfo.InvariantCulture);
    }

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, string column)
    {
        var value = ReadNullableString(reader, column);
        return value is null
            ? null
            : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ReadDateTimeOffset(SqliteDataReader reader, string column)
    {
        return DateTimeOffset.Parse(
            reader.GetString(reader.GetOrdinal(column)),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private static IReadOnlyList<CatalogPromotionRuleDto>? DeserializePromotionRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<CatalogPromotionRuleDto>>(json);
        }
        catch (JsonException)
        {
            // 损坏的旧冻结规则按缺失处理，由 mapper 对促销挂单写入稳定 Blocked 原因；
            // 绝不让一条坏记录中断同批其他挂单，也不记录 JSON 明文。
            return null;
        }
    }

    private static byte[] ReadBlob(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return (byte[])reader.GetValue(ordinal);
    }

    private static byte[]? ReadNullableBlob(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : (byte[])reader.GetValue(ordinal);
    }
}
