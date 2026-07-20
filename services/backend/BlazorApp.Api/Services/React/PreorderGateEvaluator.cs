using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

internal static class PreorderGateEvaluator
{
    private const string GateUnavailableMessage = "Preorder 状态暂时无法确认，请稍后重试";

    internal static string NormalizeStoreCode(string? storeCode)
    {
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            throw new PreorderBusinessException(
                "分店代码不能为空",
                "PREORDER_GATE_UNAVAILABLE",
                503
            );
        }
        return storeCode.Trim();
    }

    internal static string GetStoreLockResourceByStoreGuid(string storeGuid)
    {
        if (string.IsNullOrWhiteSpace(storeGuid))
        {
            throw new PreorderBusinessException(
                GateUnavailableMessage,
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }
        return $"PreorderStoreGate:{storeGuid.Trim()}";
    }

    internal static Task<Store> ResolveActiveStoreByGuidFailClosedAsync(
        ISqlSugarClient db,
        string storeGuid,
        ILogger logger
    ) => ExecuteFailClosedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(storeGuid))
        {
            throw new PreorderBusinessException(
                GateUnavailableMessage,
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }

        var normalizedGuid = storeGuid.Trim().ToLowerInvariant();
        var matches = await db.Queryable<Store>()
            .Where(item =>
                !item.IsDeleted
                && item.IsActive
                && item.StoreGUID.ToLower() == normalizedGuid
            )
            .Take(2)
            .ToListAsync();
        if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0].StoreGUID))
        {
            // 大小写变体必须归一到数据库中的唯一 StoreGUID；缺失或歧义都不能自行构造锁键。
            throw new PreorderBusinessException(
                GateUnavailableMessage,
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }
        return matches[0];
    }, storeGuid, logger);

    internal static Task<string> ResolveStoreLockResourceFailClosedAsync(
        ISqlSugarClient db,
        string storeCode,
        ILogger logger
    ) => ExecuteFailClosedAsync(async () =>
    {
        var normalized = NormalizeStoreCode(storeCode);
        var store = await db.Queryable<Store>()
            .FirstAsync(item =>
                !item.IsDeleted
                && item.IsActive
                && item.StoreCode == normalized
            );
        if (store == null || string.IsNullOrWhiteSpace(store.StoreGUID))
        {
            throw new PreorderBusinessException(
                GateUnavailableMessage,
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }
        // StoreCode 可改名，所有门禁写入统一使用不可变 StoreGuid 作为锁键。
        return GetStoreLockResourceByStoreGuid(store.StoreGUID);
    }, storeCode, logger);

    internal static async Task<PreorderGateEvaluation> EvaluateAsync(
        ISqlSugarClient db,
        string storeCode,
        DateTime utcNow
    )
    {
        var normalized = NormalizeStoreCode(storeCode);
        var currentStore = await db.Queryable<Store>()
            .FirstAsync(item => !item.IsDeleted && item.IsActive && item.StoreCode == normalized);
        if (currentStore == null || string.IsNullOrWhiteSpace(currentStore.StoreGUID))
        {
            // 旧设备绑定或历史草稿指向无效分店时必须 fail-closed。
            throw new PreorderBusinessException(
                "分店不存在或已停用，无法确认 Preorder 状态",
                "PREORDER_GATE_UNAVAILABLE",
                503
            );
        }

        // 关键逻辑：直接在数据库中关联并过滤当前有效批次，避免把该分店全部历史批次加载成超长 IN 条件。
        var windowActivations = await db.Queryable<PreorderActivation>()
            .InnerJoin<PreorderActivationStore>((activation, target) =>
                activation.ActivationGuid == target.ActivationGuid
            )
            .Where((activation, target) =>
                !activation.IsDeleted
                && !target.IsDeleted
                && target.StoreGuid == currentStore.StoreGUID
                && activation.StartAtUtc <= utcNow
                && utcNow < activation.EndAtUtc
            )
            .OrderBy((activation, target) => activation.EndAtUtc)
            .Select((activation, target) => activation)
            .ToListAsync();
        if (windowActivations.Any(activation =>
            activation.Status != PreorderActivationStatuses.Scheduled
            && activation.Status != PreorderActivationStatuses.Active
            && activation.Status != PreorderActivationStatuses.Closed
            && activation.Status != PreorderActivationStatuses.Cancelled))
        {
            // 历史非法状态不能被查询过滤掉，否则会误判“无待完成批次”并 fail-open。
            throw new PreorderBusinessException(
                GateUnavailableMessage,
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }
        var activations = windowActivations
            .Where(activation => activation.Status is PreorderActivationStatuses.Scheduled
                or PreorderActivationStatuses.Active)
            .ToList();
        var activeGuids = activations.Select(item => item.ActivationGuid).ToList();
        var completed = activeGuids.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (await db.Queryable<PreorderWarehouseOrder>()
                    .Where(item =>
                        !item.IsDeleted
                        && item.StoreGuid == currentStore.StoreGUID
                        && activeGuids.Contains(item.ActivationGuid)
                        // 关键逻辑：未知或未来中间状态不得解除普通订货门禁。
                        && (item.Status == PreorderWarehouseOrderStatuses.Submitted
                            || item.Status == PreorderWarehouseOrderStatuses.NoDemand
                            || item.Status == PreorderWarehouseOrderStatuses.Processing
                            || item.Status == PreorderWarehouseOrderStatuses.Completed
                            || item.Status == PreorderWarehouseOrderStatuses.Cancelled)
                    )
                    .Select(item => item.ActivationGuid)
                    .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = activations
            .Where(item => !completed.Contains(item.ActivationGuid))
            .ToList();
        return new PreorderGateEvaluation(currentStore, pending);
    }

    internal static async Task<PreorderGateEvaluation> EvaluateLockedFailClosedAsync(
        ISqlSugarClient db,
        string lockResource,
        string storeCode,
        TimeProvider timeProvider,
        ILogger logger
    )
    {
        await AcquireDatabaseLockFailClosedAsync(db, lockResource, storeCode, logger);
        return await EvaluateWithHeldStoreGateFailClosedAsync(
            db,
            lockResource,
            storeCode,
            timeProvider,
            logger
        );
    }

    internal static async Task<PreorderGateEvaluation> EvaluateWithHeldStoreGateFailClosedAsync(
        ISqlSugarClient db,
        string lockResource,
        string storeCode,
        TimeProvider timeProvider,
        ILogger logger
    )
    {
        // 必须在取得数据库 StoreGate 后再读时钟，等锁期间开始的批次不得被漏掉。
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var evaluation = await EvaluateFailClosedAsync(db, storeCode, utcNow, logger);
        var currentResource = GetStoreLockResourceByStoreGuid(evaluation.Store.StoreGUID);
        if (!string.Equals(lockResource, currentResource, StringComparison.OrdinalIgnoreCase))
        {
            // code 解析后可能被改绑；已持有 A 锁时绝不能按 B 分店门禁放行。
            throw new PreorderBusinessException(
                GateUnavailableMessage,
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }
        return evaluation;
    }

    internal static async Task AcquireDatabaseLockFailClosedAsync(
        ISqlSugarClient db,
        string lockResource,
        string storeCode,
        ILogger logger
    )
    {
        try
        {
            await PreorderMutationLock.AcquireDatabaseAsync(db, lockResource);
        }
        catch (PreorderBusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 原子门禁失败必须脱敏并返回稳定的可重试错误，不能落入普通 400 或泄露数据库异常。
            logger.LogError(ex, "Preorder 原子门禁检查失败: StoreCode={StoreCode}", storeCode);
            throw new PreorderBusinessException(
                GateUnavailableMessage,
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }
    }

    internal static async Task<PreorderGateEvaluation> EvaluateFailClosedAsync(
        ISqlSugarClient db,
        string storeCode,
        DateTime utcNow,
        ILogger logger
    )
    {
        return await ExecuteFailClosedAsync(
            () => EvaluateAsync(db, storeCode, utcNow),
            storeCode,
            logger
        );
    }

    internal static async Task<T> ExecuteFailClosedAsync<T>(
        Func<Task<T>> action,
        string storeCode,
        ILogger logger
    )
    {
        try
        {
            return await action();
        }
        catch (PreorderBusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 所有门禁读链路共用同一脱敏契约，避免活动列表和最终提交返回不同错误。
            logger.LogError(ex, "Preorder 门禁查询失败: StoreCode={StoreCode}", storeCode);
            throw new PreorderBusinessException(
                GateUnavailableMessage,
                "PREORDER_GATE_UNAVAILABLE",
                StatusCodes.Status503ServiceUnavailable
            );
        }
    }
}

internal sealed record PreorderGateEvaluation(
    Store Store,
    IReadOnlyList<PreorderActivation> PendingActivations
)
{
    internal bool IsBlocked => PendingActivations.Count > 0;
}
