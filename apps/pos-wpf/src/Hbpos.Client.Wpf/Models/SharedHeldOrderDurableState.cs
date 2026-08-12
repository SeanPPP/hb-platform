namespace Hbpos.Client.Wpf.Models;

/// <summary>
/// claim 来源，与 iPad SharedHeldOrderClaimSource 完全一致：
/// RemoteClaim = 服务端已存在 claim，本地绑定其 HoldGuid；
/// OfflineOrigin = 本地离线生成的 claim。
/// </summary>
public enum SharedHeldOrderClaimSource
{
    RemoteClaim = 0,
    OfflineOrigin = 1
}

/// <summary>
/// 本地 publication 状态机（与 iPad M40 share_state 对齐）：
/// NeedsEvaluation -> PendingPublish -> Published；NeedsEvaluation/PendingPublish
/// 可显式进入 Blocked；Blocked 不自动重试，只有显式重新评估才能离开。
/// Published 必须同时保存服务端 RemoteRevision/RemoteUpdatedAtIso，其他状态两者为空。
/// 服务端 held 状态（Pending/Claimed/Completed）与本地 publication 状态完全分开。
/// </summary>
public enum SharedHeldOrderPublicationStatus
{
    NeedsEvaluation = 0,
    PendingPublish = 1,
    Published = 2,
    Blocked = 3
}

/// <summary>
/// claim 耐久状态机：Prepared -> Active -> Completed；Prepared/Active 可显式 Released；
/// 服务端 OfflineOrigin 成交调和时 unbound Prepared/Active -> Superseded（保留 activate key）。
/// </summary>
public enum SharedHeldOrderClaimStatus
{
    Prepared = 0,
    Active = 1,
    Completed = 2,
    Released = 3,
    Superseded = 4
}

public sealed record SharedHeldOrderPublication(
    Guid LocalHoldGuid,
    string StoreCode,
    string DeviceCode,
    SharedHeldOrderPublicationStatus Status,
    int Revision,
    int RetryCount,
    string? ErrorCode,
    string? ErrorMessage,
    byte[]? PayloadCiphertext,
    string HeldAtIso,
    string CreatedAtIso,
    string UpdatedAtIso,
    string? LastAttemptAtIso = null,
    string? NextAttemptAtIso = null,
    long? RemoteRevision = null,
    string? RemoteUpdatedAtIso = null,
    string? ShareRequestedAtIso = null,
    string? ConsumedAtIso = null);

/// <summary>
/// 显式一次性共享请求的结果：Requested = 本次写入请求时间；
/// AlreadyRequested = 已请求过（幂等）；Ineligible = 非 Pending/
/// store-device 不匹配/已被消费；NotFound = 挂单不存在。
/// </summary>
public enum SharedHeldOrderShareRequestResult
{
    Requested,
    AlreadyRequested,
    Ineligible,
    NotFound
}

/// <summary>
/// 本地删除暂存结果：先阻断后台发布，再由调用方按需取消服务端挂单；
/// 只有远端取消成功后才可完成本地删除。
/// </summary>
public sealed record SharedHeldOrderDeleteStage(
    Guid HoldGuid,
    bool RemoteCancellationRequired);

public sealed record SharedHeldOrderClaimDraft(
    Guid ClaimId,
    Guid HoldGuid,
    string StoreCode,
    string DeviceCode,
    SharedHeldOrderClaimSource Source,
    string PrepareIdempotencyKey,
    SharedHeldOrderCanonicalPayload Payload,
    string CreatedAtIso,
    string? ExpiresAtIso = null);

/// <summary>claim 存储视图：payload 永远只以密文出现。</summary>
public sealed record SharedHeldOrderClaimRecord(
    Guid ClaimId,
    Guid HoldGuid,
    string StoreCode,
    string DeviceCode,
    SharedHeldOrderClaimSource Source,
    SharedHeldOrderClaimStatus Status,
    string PrepareIdempotencyKey,
    string? ActivateIdempotencyKey,
    string? ReleaseIdempotencyKey,
    byte[] PayloadCiphertext,
    long? ServerRevision,
    string? ExpiresAtIso,
    string? BoundOrderGuid,
    string? SupersedeIdempotencyKey,
    string CreatedAtIso,
    string UpdatedAtIso);

/// <summary>mine recovery：解密密文后恢复完整 canonical payload。</summary>
public sealed record SharedHeldOrderClaimRecovery(
    Guid ClaimId,
    Guid HoldGuid,
    string StoreCode,
    string DeviceCode,
    SharedHeldOrderClaimSource Source,
    SharedHeldOrderClaimStatus Status,
    string PrepareIdempotencyKey,
    string? ActivateIdempotencyKey,
    string? ReleaseIdempotencyKey,
    SharedHeldOrderCanonicalPayload Payload,
    long? ServerRevision,
    string? ExpiresAtIso,
    string? BoundOrderGuid,
    string? SupersedeIdempotencyKey,
    string CreatedAtIso,
    string UpdatedAtIso);

/// <summary>
/// 取单完成事务上下文：由付款完成路径从本地 durable claim 解析后传给
/// LocalOrderRepository，保证 held-order source、claim 完成与本地挂单消费
/// 与 LocalOrder+SyncQueue 同一事务提交。
/// </summary>
public sealed record LocalHeldOrderCompletionContext(
    Guid HoldGuid,
    Guid ClaimId,
    SharedHeldOrderClaimSource Source,
    string PrepareIdempotencyKey,
    string? ActivateIdempotencyKey,
    string? BoundOrderGuid,
    string CompletedAtIso);
