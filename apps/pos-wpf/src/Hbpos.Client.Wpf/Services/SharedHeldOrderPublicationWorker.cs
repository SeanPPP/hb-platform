using System.Globalization;
using System.IO;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.HeldOrders;

namespace Hbpos.Client.Wpf.Services;

public sealed record SharedHeldOrderPublicationRunResult(
    int EvaluatedOrders,
    int StagedPendingPublish,
    int Blocked,
    int Published,
    int FailedCapability,
    int FailedPublish);

public interface ISharedHeldOrderPublicationWorker
{
    /// <summary>
    /// 一轮后台处理：先评估本店 NeedsEvaluation 的旧/新挂单（映射 -> PendingPublish/
    /// Blocked），再按退避发布所有到期的 PendingPublish。网络不可用/disabled 只记
    /// 退避，绝不删除或改变本地挂单。
    /// </summary>
    Task<SharedHeldOrderPublicationRunResult> RunOnceAsync(
        string storeCode,
        string? deviceCode = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 本地挂单后台 evaluator/publisher：NeedsEvaluation -> PendingPublish（密文原子落库）
/// -> Published（远端 revision/time 原子持久化）；失败按现有 backoff 重试；
/// return/open-item 与无法可靠冻结的促销 fail-closed Blocked 并保留原因。
/// 服务端 feature disabled/网络不可用绝不影响本机挂单。
/// </summary>
public sealed class SharedHeldOrderPublicationWorker(
    ISharedHeldOrderRepository repository,
    ISharedHeldOrderMapper mapper,
    ISharedHeldOrderApiClient apiClient,
    Func<SuspendedOrder, IReadOnlyList<CatalogPromotionRuleDto>?>? frozenPromotionRuleProvider = null,
    TimeProvider? timeProvider = null) : ISharedHeldOrderPublicationWorker
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<SharedHeldOrderPublicationRunResult> RunOnceAsync(
        string storeCode,
        string? deviceCode = null,
        CancellationToken cancellationToken = default)
    {
        var nowIso = FormatIso(_timeProvider.GetUtcNow());
        var evaluated = 0;
        var staged = 0;
        var blocked = 0;
        var published = 0;
        var failedCapability = 0;
        var failedPublish = 0;

        var legacy = await repository.ListLegacyOrdersNeedingEvaluationAsync(
            storeCode,
            deviceCode,
            cancellationToken);
        foreach (var order in legacy)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var publication = await repository.GetPublicationAsync(
                order.SuspendedOrderGuid,
                cancellationToken);
            if (publication is null)
            {
                // 旧库挂单没有 publication 行：先原子补 NeedsEvaluation 初态。
                // 回填必须使用挂单自身的 DeviceCode；缺失时才回退调用方传入的 deviceCode，
                // 绝不用空字符串覆盖真实设备来源。
                var backfillDeviceCode = string.IsNullOrWhiteSpace(order.DeviceCode)
                    ? deviceCode ?? string.Empty
                    : order.DeviceCode;
                await repository.UpsertPublicationAsync(
                    order.SuspendedOrderGuid,
                    storeCode,
                    backfillDeviceCode,
                    SharedHeldOrderPublicationStatus.NeedsEvaluation,
                    payloadCiphertext: null,
                    FormatIso(order.SuspendedAt),
                    nowIso,
                    nowIso,
                    cancellationToken: cancellationToken);
                publication = await repository.GetPublicationAsync(
                    order.SuspendedOrderGuid,
                    cancellationToken);
            }

            if (publication is null || publication.Status != SharedHeldOrderPublicationStatus.NeedsEvaluation)
            {
                // 并发已推进，跳过本轮。
                continue;
            }

            evaluated++;
            SharedHeldOrderMappingResult mapping;
            try
            {
                mapping = mapper.Map(
                    order,
                    frozenPromotionRuleProvider?.Invoke(order),
                    revision: 1);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or OverflowException
                    or SharedHeldOrderCanonicalValidationException)
            {
                // 单条旧挂单损坏不能中断整个批次；保留本地并写稳定阻断原因，
                // 错误详情不包含商品或 canonical payload。
                if (await repository.TryBlockPublicationAsync(
                        order.SuspendedOrderGuid,
                        publication.Revision,
                        SharedHeldOrderMappingReasons.InvalidSnapshot,
                        "本地挂单快照无法无损转换为共享格式。",
                        nowIso,
                        cancellationToken))
                {
                    blocked++;
                }

                continue;
            }
            if (mapping.IsBlocked)
            {
                // return/open-item/无法可靠冻结的促销：fail-closed Blocked 并保留原因，
                // 本地挂单原样保留，只有显式重新评估才能离开。
                if (await repository.TryBlockPublicationAsync(
                        order.SuspendedOrderGuid,
                        publication.Revision,
                        mapping.Block!.Reason,
                        mapping.Block.Detail ?? string.Empty,
                        nowIso,
                        cancellationToken))
                {
                    blocked++;
                }

                continue;
            }

            if (await repository.TryStagePendingPublishAsync(
                    order.SuspendedOrderGuid,
                    publication.Revision,
                    mapping.Payload!,
                    nowIso,
                    cancellationToken))
            {
                staged++;
            }
        }

        var due = (await repository.ListDuePublicationsAsync(nowIso, cancellationToken))
            .Where(publication =>
                string.Equals(
                    publication.StoreCode,
                    storeCode,
                    StringComparison.OrdinalIgnoreCase)
                && (deviceCode is null
                    || string.Equals(
                        publication.DeviceCode,
                        deviceCode,
                        StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (due.Length == 0)
        {
            return new SharedHeldOrderPublicationRunResult(
                evaluated, staged, blocked, published, failedCapability, failedPublish);
        }

        // 先读 capability：disabled/网络不可用都不发布，只记退避（不阻止本地挂单）。
        var capability = await ReadCapabilityAsync(nowIso, due, cancellationToken);
        if (capability == CapabilityGate.NotReady)
        {
            return new SharedHeldOrderPublicationRunResult(
                evaluated, staged, blocked, published, failedCapability + due.Length, failedPublish);
        }

        if (capability == CapabilityGate.Disabled)
        {
            foreach (var publication in due)
            {
                await RecordBackoffAsync(
                    publication,
                    nowIso,
                    "SHARED_HELD_ORDER_DISABLED",
                    "服务端共享挂单功能未启用。",
                    cancellationToken);
            }

            return new SharedHeldOrderPublicationRunResult(
                evaluated, staged, blocked, published, failedCapability + due.Length, failedPublish);
        }

        foreach (var publication in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var payload = await repository.GetPublicationPayloadAsync(
                    publication.LocalHoldGuid,
                    cancellationToken);
                if (payload is null)
                {
                    throw new InvalidDataException("PendingPublish 缺少可解密 payload。");
                }

                var cart = ToPublishCart(payload);
                var request = new SharedHeldOrderPublishRequest(
                    publication.LocalHoldGuid,
                    publication.StoreCode,
                    publication.DeviceCode,
                    cart,
                    IdempotencyKeyFor(publication.LocalHoldGuid));
                var response = await apiClient.PublishAsync(request, cancellationToken);
                // 发布成功：远端 revision/time 与 Published 状态原子落库（幂等重试同 key）。
                var advanced = await repository.TryAdvancePublicationAsync(
                    publication.LocalHoldGuid,
                    SharedHeldOrderPublicationStatus.PendingPublish,
                    publication.Revision,
                    SharedHeldOrderPublicationStatus.Published,
                    nowIso,
                    lastAttemptAtIso: nowIso,
                    remoteRevision: response.Revision,
                    remoteUpdatedAtIso: FormatIso(response.CreatedAtUtc),
                    cancellationToken: cancellationToken);
                if (advanced)
                {
                    published++;
                }
            }
            catch (SharedHeldOrderApiException exception)
            {
                // 发布失败：保持 PendingPublish，RetryCount +1 并退避；本地挂单不动。
                await RecordBackoffAsync(
                    publication,
                    nowIso,
                    exception.ErrorCode,
                    exception.Message,
                    cancellationToken);
                failedPublish++;
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                    or SharedHeldOrderCanonicalValidationException)
            {
                // 本地 payload 无法构造发布请求：fail-closed Blocked，保留原因。
                await repository.TryAdvancePublicationAsync(
                    publication.LocalHoldGuid,
                    SharedHeldOrderPublicationStatus.PendingPublish,
                    publication.Revision,
                    SharedHeldOrderPublicationStatus.Blocked,
                    nowIso,
                    errorCode: "SHARED_HELD_ORDER_INVALID",
                    errorMessage: "本地共享挂单 payload 无法发布，已阻断。",
                    cancellationToken: cancellationToken);
                blocked++;
            }
        }

        return new SharedHeldOrderPublicationRunResult(
            evaluated, staged, blocked, published, failedCapability, failedPublish);
    }

    private async Task<CapabilityGate> ReadCapabilityAsync(
        string nowIso,
        IReadOnlyList<SharedHeldOrderPublication> due,
        CancellationToken cancellationToken)
    {
        try
        {
            var capabilities = await apiClient.GetCapabilitiesAsync(cancellationToken);
            if (!capabilities.Enabled)
            {
                return CapabilityGate.Disabled;
            }

            if (capabilities.PayloadVersion != SharedSaleCartV1Constants.PayloadVersion)
            {
                foreach (var publication in due)
                {
                    await RecordBackoffAsync(
                        publication,
                        nowIso,
                        "SHARED_HELD_ORDER_VERSION_MISMATCH",
                        $"服务端 payload 版本 {capabilities.PayloadVersion} 与本地版本不一致。",
                        cancellationToken);
                }

                return CapabilityGate.NotReady;
            }

            return CapabilityGate.Enabled;
        }
        catch (SharedHeldOrderApiException exception)
        {
            foreach (var publication in due)
            {
                await RecordBackoffAsync(
                    publication,
                    nowIso,
                    exception.ErrorCode,
                    exception.Message,
                    cancellationToken);
            }

            return CapabilityGate.NotReady;
        }
    }

    /// <summary>失败退避：状态机保持 PendingPublish，仓库内部 RetryCount +1 并计算 NextAttemptAtIso。</summary>
    private async Task RecordBackoffAsync(
        SharedHeldOrderPublication publication,
        string nowIso,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await repository.TryAdvancePublicationAsync(
            publication.LocalHoldGuid,
            SharedHeldOrderPublicationStatus.PendingPublish,
            publication.Revision,
            SharedHeldOrderPublicationStatus.PendingPublish,
            nowIso,
            errorCode: errorCode,
            errorMessage: errorMessage,
            lastAttemptAtIso: nowIso,
            cancellationToken: cancellationToken);
    }

    /// <summary>本地 canonical -> 服务端 SharedSaleCartV1 契约（显式字段映射）。</summary>
    private static SharedSaleCartV1 ToPublishCart(SharedHeldOrderCanonicalPayload payload)
    {
        return SharedHeldOrderContractMapper.ToContract(payload);
    }

    /// <summary>发布幂等键：同一本地挂单恒用同一 key，服务端重复发布按幂等返回。</summary>
    private static string IdempotencyKeyFor(Guid localHoldGuid)
    {
        return localHoldGuid.ToString("D");
    }

    private static string FormatIso(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private enum CapabilityGate
    {
        Enabled,
        Disabled,
        NotReady
    }
}
