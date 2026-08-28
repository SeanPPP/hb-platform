using System.Data;
using System.Globalization;
using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using TaskStatus = BlazorApp.Shared.Models.HBweb.TaskStatus;
using TaskTrigger = BlazorApp.Shared.Models.HBweb.TaskTrigger;
using TaskType = BlazorApp.Shared.Models.HBweb.TaskType;

namespace BlazorApp.Api.Services.Background;

public interface IProductStoreDailyStatisticExecutor
{
    Task ExecuteQueuedDateAsync(
        DateTime date,
        Guid expectedJobId,
        Func<Task> validateExecutionOwnershipAsync,
        CancellationToken cancellationToken
    );
}

public interface IProductStoreDailyStatisticQueueService
{
    Task<ProductStoreDailyRecalculationSubmitResult> EnqueueAsync(
        IEnumerable<DateTime> dates,
        string? requestedBy,
        int maxConcurrency = 3,
        CancellationToken cancellationToken = default
    );

    Task<ProductStoreDailyRecalculationSubmitResult> EnqueueYearBackfillAsync(
        IEnumerable<DateTime> dates,
        string? requestedBy,
        int maxConcurrency = 3,
        CancellationToken cancellationToken = default
    );

    Task<int> DrainOnceAsync(CancellationToken cancellationToken = default);

    Task<int> RecoverExpiredRunningClaimsAsync(CancellationToken cancellationToken = default);

    Task<int> FinalizeJobsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 商品分店每日统计的持久任务队列。日期清单、任务日志和状态共享同一个主库事务，
/// 进程重启后由 hosted drainer 从数据库继续执行，不依赖请求线程或内存任务。
/// </summary>
public sealed class ProductStoreDailyStatisticQueueService
    : IProductStoreDailyStatisticQueueService
{
    private const int MaxRegularDays = 31;
    private const int MaxYearBackfillDays = 365;
    internal const string ProductStoreDaily2025SerialLeaseTaskType =
        "RecalculateProductStoreDaily2025Serial";
    internal const string ProductStoreDaily2025SerialLeaseScope =
        "product-store-daily-2025-global";
    private static readonly TimeSpan ExecutionLeaseDuration = TimeSpan.FromHours(2);
    private static readonly TimeSpan RunningClaimTimeout = TimeSpan.FromHours(2);
    private static readonly TimeSpan OrphanManifestGracePeriod = TimeSpan.FromHours(2);

    private readonly SqlSugarContext _context;
    private readonly ScheduledTaskLogService _taskLogService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductStoreDailyStatisticQueueService> _logger;

    public ProductStoreDailyStatisticQueueService(
        SqlSugarContext context,
        ScheduledTaskLogService taskLogService,
        IServiceScopeFactory scopeFactory,
        ILogger<ProductStoreDailyStatisticQueueService> logger
    )
    {
        _context = context;
        _taskLogService = taskLogService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task<ProductStoreDailyRecalculationSubmitResult> EnqueueAsync(
        IEnumerable<DateTime> dates,
        string? requestedBy,
        int maxConcurrency = 3,
        CancellationToken cancellationToken = default
    )
    {
        return EnqueueCoreAsync(
            dates,
            requestedBy,
            maxConcurrency,
            MaxRegularDays,
            cancellationToken
        );
    }

    public Task<ProductStoreDailyRecalculationSubmitResult> EnqueueYearBackfillAsync(
        IEnumerable<DateTime> dates,
        string? requestedBy,
        int maxConcurrency = 3,
        CancellationToken cancellationToken = default
    )
    {
        return EnqueueCoreAsync(
            dates,
            requestedBy,
            maxConcurrency,
            MaxYearBackfillDays,
            cancellationToken
        );
    }

    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var queuedStates = await _context.Db.Queryable<SalesStatisticRefreshState>()
            .Where(state =>
                state.StatisticType == SalesStatisticType.ProductStoreDaily
                && state.Status == SalesStatisticRefreshStatus.Queued
                && state.JobId != null
            )
            .OrderBy(state => state.RequestedAtUtc)
            .OrderBy(state => state.Date)
            .ToListAsync();
        if (queuedStates.Count == 0)
        {
            return 0;
        }

        var runningLogs = await _context.Db.Queryable<ScheduledTaskLog>()
            .Where(task =>
                task.TaskType == TaskType.RecalculateProductStoreDaily
                && task.Status == TaskStatus.Running
            )
            .ToListAsync();
        var runningLogsById = runningLogs.ToDictionary(task => task.Id);
        var selectedGroup = queuedStates
            .Where(state => state.JobId.HasValue && runningLogsById.ContainsKey(state.JobId.Value))
            .GroupBy(state => state.JobId!.Value)
            .OrderBy(group => group.Min(state => state.RequestedAtUtc ?? DateTime.MinValue))
            .ThenBy(group => group.Min(state => state.Date))
            .FirstOrDefault();
        if (selectedGroup == null)
        {
            return 0;
        }

        var taskLog = runningLogsById[selectedGroup.Key];
        if (!TryReadManifest(taskLog, out var parameters, out var manifestDates, out var manifestError))
        {
            _logger.LogError(
                "商品统计任务不可变日期清单无效，等待 finalizer 失败终结: {JobId}, {Error}",
                taskLog.Id,
                manifestError
            );
            return 0;
        }

        var manifestSet = manifestDates.ToHashSet();
        var maxConcurrency = ResolveMaxConcurrency(manifestDates, parameters.MaxConcurrency ?? 3);
        var candidates = selectedGroup
            .Where(state => manifestSet.Contains(state.Date.Date))
            .OrderBy(state => state.RequestedAtUtc)
            .ThenBy(state => state.Date)
            .ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        // 2025 的历史回填还会触碰成对年度事实表，必须先取得数据库级全局租约再 claim。
        if (candidates[0].Date.Year == 2025)
        {
            return await TryClaimAndExecute2025Async(
                taskLog.Id,
                candidates[0].Date.Date,
                cancellationToken
            ) ? 1 : 0;
        }

        var claimedDates = new List<DateTime>(maxConcurrency);
        try
        {
            foreach (var state in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (claimedDates.Count >= maxConcurrency)
                {
                    break;
                }

                if (await TryClaimDateAsync(_context, selectedGroup.Key, state.Date.Date))
                {
                    claimedDates.Add(state.Date.Date);
                }
            }
        }
        catch
        {
            // claim 循环尚未进入执行器时发生取消/异常，必须逐个以 JobId fencing 退回本批已 claim 日期。
            await ReturnClaimsToQueueBestEffortAsync(selectedGroup.Key, claimedDates);
            throw;
        }

        if (claimedDates.Count == 0)
        {
            return 0;
        }

        var executions = claimedDates.Select(date =>
            ExecuteClaimAsync(taskLog.Id, date, cancellationToken)
        );
        var executionResults = await Task.WhenAll(executions);
        return executionResults.Count(executed => executed);
    }

    public async Task<int> RecoverExpiredRunningClaimsAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nowUtc = DateTime.UtcNow;
        var progress = await EnsureOrphanTaskLogsAsync(nowUtc, cancellationToken);
        var runningStates = await _context.Db.Queryable<SalesStatisticRefreshState>()
            .Where(state =>
                state.StatisticType == SalesStatisticType.ProductStoreDaily
                && state.Status == SalesStatisticRefreshStatus.Running
            )
            .ToListAsync();

        foreach (var state in runningStates.OrderBy(state => state.Date))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var referenceTime = state.StartedAtUtc ?? state.LastCheckedAtUtc ?? state.RequestedAtUtc;
            if (!referenceTime.HasValue)
            {
                // 缺失水位时先写安全宽限起点；本轮不能凭空判断 worker 已死亡。
                var touched = await _context.Db.Updateable<SalesStatisticRefreshState>()
                    .SetColumns(row => row.LastCheckedAtUtc == nowUtc)
                    .Where(row =>
                        row.StatisticType == SalesStatisticType.ProductStoreDaily
                        && row.Date == state.Date
                        && row.JobId == state.JobId
                        && row.Status == SalesStatisticRefreshStatus.Running
                        && row.StartedAtUtc == null
                        && row.LastCheckedAtUtc == null
                        && row.RequestedAtUtc == null
                    )
                    .ExecuteCommandAsync();
                progress += touched;
                continue;
            }
            if (nowUtc - referenceTime.Value < RunningClaimTimeout)
            {
                continue;
            }
            if (await HasActiveDateLeaseAsync(state.Date, nowUtc))
            {
                continue;
            }

            var recovered = await _context.Db.Updateable<SalesStatisticRefreshState>()
                .SetColumns(row => row.Status == SalesStatisticRefreshStatus.Queued)
                .SetColumns(row => row.StartedAtUtc == null)
                .SetColumns(row => row.CompletedAtUtc == null)
                .SetColumns(row => row.ErrorMessage == null)
                .SetColumns(row => row.LastCheckedAtUtc == nowUtc)
                .Where(row =>
                    row.StatisticType == SalesStatisticType.ProductStoreDaily
                    && row.Date == state.Date
                    && row.JobId == state.JobId
                    && row.Status == SalesStatisticRefreshStatus.Running
                )
                .ExecuteCommandAsync();
            progress += recovered;
        }

        return progress;
    }

    public async Task<int> FinalizeJobsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runningLogs = await _context.Db.Queryable<ScheduledTaskLog>()
            .Where(task =>
                task.TaskType == TaskType.RecalculateProductStoreDaily
                && task.Status == TaskStatus.Running
            )
            .OrderBy(task => task.StartedAt)
            .ToListAsync();
        var finalized = 0;

        foreach (var taskLog in runningLogs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadManifest(taskLog, out _, out var manifestDates, out var manifestError))
            {
                if (await FailMalformedManifestAsync(taskLog.Id, manifestError))
                {
                    finalized++;
                }
                continue;
            }

            // Fresh 状态可能先于计算租约 completion 落库；任一 manifest 日期仍有有效租约时不得抢先终结日志。
            if (await HasAnyActiveDateLeaseAsync(manifestDates, DateTime.UtcNow))
            {
                continue;
            }

            var minDate = manifestDates.Min();
            var maxDate = manifestDates.Max();
            var manifestSet = manifestDates.ToHashSet();
            var states = await _context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(state =>
                    state.StatisticType == SalesStatisticType.ProductStoreDaily
                    && state.Date >= minDate
                    && state.Date <= maxDate
                    && state.JobId == taskLog.Id
                )
                .ToListAsync();
            states = states.Where(state => manifestSet.Contains(state.Date.Date)).ToList();
            if (states.Count != manifestDates.Count)
            {
                continue;
            }

            if (states.All(state => state.Status == SalesStatisticRefreshStatus.Fresh))
            {
                if (await _taskLogService.TryCompleteProductStoreDailyTaskAsync(taskLog.Id, true))
                {
                    finalized++;
                }
                continue;
            }

            var hasPending = states.Any(state =>
                state.Status == SalesStatisticRefreshStatus.Queued
                || state.Status == SalesStatisticRefreshStatus.Running
            );
            var failures = states
                .Where(state => state.Status == SalesStatisticRefreshStatus.Failed)
                .OrderBy(state => state.Date)
                .ToList();
            if (!hasPending && failures.Count > 0)
            {
                var failureMessage = string.Join(
                    "；",
                    failures.Select(state =>
                        $"{state.Date:yyyy-MM-dd}: {NormalizeError(state.ErrorMessage)}"
                    )
                );
                if (
                    await _taskLogService.TryCompleteProductStoreDailyTaskAsync(
                        taskLog.Id,
                        false,
                        failureMessage
                    )
                )
                {
                    finalized++;
                }
            }
        }

        return finalized;
    }

    private async Task<ProductStoreDailyRecalculationSubmitResult> EnqueueCoreAsync(
        IEnumerable<DateTime> dates,
        string? requestedBy,
        int maxConcurrency,
        int maxDays,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(dates);
        cancellationToken.ThrowIfCancellationRequested();
        var targetDates = dates
            .Select(date => date.Date)
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        if (targetDates.Count == 0)
        {
            throw new ArgumentException("商品分店每日统计至少需要 1 个日期", nameof(dates));
        }
        if (targetDates.Count > maxDays)
        {
            throw new ArgumentException(
                $"商品分店每日统计一次最多重算 {maxDays} 天，请分段执行",
                nameof(dates)
            );
        }

        var normalizedRequestedBy = string.IsNullOrWhiteSpace(requestedBy)
            ? null
            : requestedBy.Trim();
        var normalizedConcurrency = ResolveMaxConcurrency(targetDates, maxConcurrency);
        var jobId = Guid.Empty;
        var submittedDates = new List<DateTime>();
        var skippedDates = new List<DateTime>();
        var activeJobIds = new List<Guid>();
        ScheduledTaskLog? createdTaskLog = null;

        await _context.Db.Ado.BeginTranAsync(IsolationLevel.Serializable);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var minDate = targetDates[0];
            var maxDate = targetDates[^1];
            var targetSet = targetDates.ToHashSet();
            var existingStates = await _context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(state =>
                    state.StatisticType == SalesStatisticType.ProductStoreDaily
                    && state.Date >= minDate
                    && state.Date <= maxDate
                )
                .ToListAsync();
            existingStates = existingStates
                .Where(state => targetSet.Contains(state.Date.Date))
                .ToList();
            var existingByDate = existingStates.ToDictionary(state => state.Date.Date);
            var linkedJobIds = existingStates
                .Where(state => state.JobId.HasValue)
                .Select(state => state.JobId!.Value)
                .Distinct()
                .ToList();
            var runningLogs = linkedJobIds.Count == 0
                ? new List<ScheduledTaskLog>()
                : await _context.Db.Queryable<ScheduledTaskLog>()
                    .Where(task =>
                        linkedJobIds.Contains(task.Id)
                        && task.TaskType == TaskType.RecalculateProductStoreDaily
                        && task.Status == TaskStatus.Running
                    )
                    .ToListAsync();
            var runningJobIds = runningLogs.Select(task => task.Id).ToHashSet();

            foreach (var date in targetDates)
            {
                if (
                    existingByDate.TryGetValue(date, out var state)
                    && (
                        state.Status == SalesStatisticRefreshStatus.Queued
                        || state.Status == SalesStatisticRefreshStatus.Running
                        || (state.JobId.HasValue && runningJobIds.Contains(state.JobId.Value))
                    )
                )
                {
                    skippedDates.Add(date);
                }
                else
                {
                    submittedDates.Add(date);
                }
            }

            activeJobIds = skippedDates
                .Select(date => existingByDate[date].JobId)
                .Where(id => id.HasValue && runningJobIds.Contains(id.Value))
                .Select(id => id!.Value)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            if (submittedDates.Count == 0)
            {
                if (activeJobIds.Count == 0)
                {
                    throw new InvalidOperationException(
                        "活动商品统计状态缺少真实 Running 任务日志，请等待恢复服务补齐后重试"
                    );
                }
                jobId = activeJobIds.Count == 1 ? activeJobIds[0] : Guid.Empty;
                await _context.Db.Ado.CommitTranAsync();
            }
            else
            {
                jobId = Guid.NewGuid();
                var nowUtc = DateTime.UtcNow;
                var parameters = CreateTaskParameters(submittedDates, normalizedConcurrency);
                createdTaskLog = await _taskLogService.LogTaskStartStrictAsync(
                    jobId,
                    TaskType.RecalculateProductStoreDaily,
                    parameters,
                    TaskTrigger.Manual,
                    canRetry: false,
                    startedAtUtc: nowUtc
                );

                foreach (var date in submittedDates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (existingByDate.TryGetValue(date, out var existing))
                    {
                        existing.Status = SalesStatisticRefreshStatus.Queued;
                        existing.JobId = jobId;
                        existing.RequestedBy = normalizedRequestedBy;
                        existing.RequestedAtUtc = nowUtc;
                        existing.StartedAtUtc = null;
                        existing.CompletedAtUtc = null;
                        existing.LastCheckedAtUtc = nowUtc;
                        existing.ErrorMessage = null;
                        var updated = await _context.Db.Updateable(existing).ExecuteCommandAsync();
                        if (updated != 1)
                        {
                            throw new InvalidOperationException(
                                $"商品统计排队状态未严格更新: {date:yyyy-MM-dd}"
                            );
                        }
                        continue;
                    }

                    var inserted = await _context.Db.Insertable(new SalesStatisticRefreshState
                    {
                        StatisticType = SalesStatisticType.ProductStoreDaily,
                        Date = date,
                        Status = SalesStatisticRefreshStatus.Queued,
                        SourceTimeZone = "POSM_LOCAL",
                        JobId = jobId,
                        RequestedBy = normalizedRequestedBy,
                        RequestedAtUtc = nowUtc,
                        LastCheckedAtUtc = nowUtc,
                    }).ExecuteCommandAsync();
                    if (inserted != 1)
                    {
                        throw new InvalidOperationException(
                            $"商品统计排队状态未严格插入: {date:yyyy-MM-dd}"
                        );
                    }
                }

                await _context.Db.Ado.CommitTranAsync();
            }
        }
        catch
        {
            try
            {
                await _context.Db.Ado.RollbackTranAsync();
            }
            catch (Exception rollbackException)
            {
                _logger.LogError(rollbackException, "商品统计排队事务回滚失败");
            }
            throw;
        }

        if (createdTaskLog != null)
        {
            // 关键顺序：只有日志、manifest 和全部日期状态都提交后才允许发布 started。
            _taskLogService.PublishTaskStartedAfterCommit(createdTaskLog);
        }

        return new ProductStoreDailyRecalculationSubmitResult
        {
            JobId = jobId,
            ActiveJobIds = activeJobIds,
            SubmittedDates = submittedDates,
            SkippedDates = skippedDates,
            Status = submittedDates.Count > 0
                ? SalesStatisticRefreshStatus.Queued
                : SalesStatisticRefreshStatus.Running,
            Message = BuildSubmitMessage(
                submittedDates.Count,
                skippedDates.Count,
                activeJobIds.Count
            ),
        };
    }

    private async Task<bool> TryClaimAndExecute2025Async(
        Guid jobId,
        DateTime date,
        CancellationToken cancellationToken
    )
    {
        using var globalScope = _scopeFactory.CreateScope();
        var globalLeaseService = globalScope.ServiceProvider
            .GetRequiredService<ScheduledTaskLeaseService>();
        string? globalLeaseToken = null;
        var claimed = false;
        Exception? failure = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var globalLease = await globalLeaseService.TryAcquireAsync(
                ProductStoreDaily2025SerialLeaseTaskType,
                ProductStoreDaily2025SerialLeaseScope,
                ExecutionLeaseDuration
            );
            if (!globalLease.Acquired)
            {
                return false;
            }

            globalLeaseToken = globalLease.Lease?.LeaseToken;
            if (string.IsNullOrWhiteSpace(globalLeaseToken))
            {
                throw new InvalidOperationException("2025 商品统计全局租约缺少 fencing token");
            }

            // 全局串行租约必须先于状态 claim，确保不同实例、不同日期也不能并发进入 2025 写链。
            cancellationToken.ThrowIfCancellationRequested();
            claimed = await TryClaimDateAsync(_context, jobId, date);
            if (!claimed)
            {
                return false;
            }

            return await ExecuteClaimAsync(
                jobId,
                date,
                cancellationToken,
                new GlobalLeaseOwnership(globalLeaseService, globalLeaseToken)
            );
        }
        catch (Exception ex)
        {
            failure = ex;
            if (claimed)
            {
                await ReturnClaimToQueueAsync(_context, jobId, date);
            }
            throw;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(globalLeaseToken))
            {
                try
                {
                    if (
                        !await globalLeaseService.CompleteAsync(
                            ProductStoreDaily2025SerialLeaseTaskType,
                            ProductStoreDaily2025SerialLeaseScope,
                            globalLeaseToken,
                            failure == null,
                            failure?.Message
                        )
                    )
                    {
                        _logger.LogWarning(
                            "2025 商品统计全局租约 completion 未取得所有权: JobId={JobId}, Date={Date}",
                            jobId,
                            date.ToString("yyyy-MM-dd")
                        );
                    }
                }
                catch (Exception leaseException)
                {
                    _logger.LogError(
                        leaseException,
                        "2025 商品统计全局租约 completion 异常: JobId={JobId}, Date={Date}",
                        jobId,
                        date.ToString("yyyy-MM-dd")
                    );
                }
            }
        }
    }

    private static async Task<bool> TryClaimDateAsync(
        SqlSugarContext context,
        Guid jobId,
        DateTime date
    )
    {
        var claimedAtUtc = DateTime.UtcNow;
        var targetDate = date.Date;
        var updated = await context.Db.Updateable<SalesStatisticRefreshState>()
            .SetColumns(state => state.Status == SalesStatisticRefreshStatus.Running)
            .SetColumns(state => state.StartedAtUtc == claimedAtUtc)
            .SetColumns(state => state.LastCheckedAtUtc == claimedAtUtc)
            .SetColumns(state => state.CompletedAtUtc == null)
            .SetColumns(state => state.ErrorMessage == null)
            .Where(state =>
                state.StatisticType == SalesStatisticType.ProductStoreDaily
                && state.Date == targetDate
                && state.JobId == jobId
                && state.Status == SalesStatisticRefreshStatus.Queued
            )
            .ExecuteCommandAsync();
        if (updated > 1)
        {
            throw new InvalidOperationException($"日期 claim 影响了异常行数: {targetDate:yyyy-MM-dd}");
        }
        return updated == 1;
    }

    private async Task ReturnClaimsToQueueBestEffortAsync(
        Guid jobId,
        IReadOnlyCollection<DateTime> dates
    )
    {
        foreach (var date in dates)
        {
            try
            {
                await ReturnClaimToQueueAsync(_context, jobId, date);
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(
                    cleanupException,
                    "商品统计 claim 批次回退失败: JobId={JobId}, Date={Date}",
                    jobId,
                    date.ToString("yyyy-MM-dd")
                );
            }
        }
    }

    private async Task<bool> ExecuteClaimAsync(
        Guid jobId,
        DateTime date,
        CancellationToken cancellationToken,
        GlobalLeaseOwnership? globalLease = null
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
        var leaseService = scope.ServiceProvider.GetRequiredService<ScheduledTaskLeaseService>();
        var executor = scope.ServiceProvider.GetRequiredService<IProductStoreDailyStatisticExecutor>();
        var cacheWarmer = scope.ServiceProvider.GetRequiredService<ISalesDashboardCacheWarmer>();
        var dateKey = date.ToString("yyyy-MM-dd");
        string? leaseToken = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease = await leaseService.TryAcquireAsync(
                SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
                dateKey,
                ExecutionLeaseDuration
            );
            if (!lease.Acquired)
            {
                await ReturnClaimToQueueAsync(context, jobId, date);
                return false;
            }

            leaseToken = lease.Lease?.LeaseToken;
            if (string.IsNullOrWhiteSpace(leaseToken))
            {
                await ReturnClaimToQueueAsync(context, jobId, date);
                throw new InvalidOperationException($"统计租约缺少 fencing token: {dateKey}");
            }

            async Task ValidateExecutionOwnershipAsync()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (globalLease != null)
                {
                    await globalLease.LeaseService.EnsureActiveAsync(
                        ProductStoreDaily2025SerialLeaseTaskType,
                        ProductStoreDaily2025SerialLeaseScope,
                        globalLease.LeaseToken,
                        ExecutionLeaseDuration,
                        "2025 商品每日统计全局串行"
                    );
                }
                await leaseService.EnsureActiveAsync(
                    SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
                    dateKey,
                    leaseToken,
                    ExecutionLeaseDuration,
                    "商品分店每日持久队列"
                );
                var currentState = await context.Db.Queryable<SalesStatisticRefreshState>()
                    .Where(state =>
                        state.StatisticType == SalesStatisticType.ProductStoreDaily
                        && state.Date == date
                    )
                    .FirstAsync();
                if (
                    currentState == null
                    || currentState.JobId != jobId
                    || currentState.Status != SalesStatisticRefreshStatus.Running
                )
                {
                    throw new InvalidOperationException(
                        $"商品统计执行权已变化，拒绝旧 worker 提交: {dateKey} {jobId}"
                    );
                }
            }

            await executor.ExecuteQueuedDateAsync(
                date,
                jobId,
                ValidateExecutionOwnershipAsync,
                cancellationToken
            );
            var completedState = await context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(state =>
                    state.StatisticType == SalesStatisticType.ProductStoreDaily
                    && state.Date == date
                )
                .FirstAsync();
            if (
                completedState == null
                || completedState.JobId != jobId
                || completedState.Status != SalesStatisticRefreshStatus.Fresh
            )
            {
                throw new InvalidOperationException(
                    $"商品统计执行完成但未形成同 owner Fresh 状态: {dateKey} {jobId}"
                );
            }

            try
            {
                await cacheWarmer.ClearCacheAsync();
            }
            catch (Exception cacheException)
            {
                // 缓存只影响后续读取时效，不能把已经原子落库的统计计算降级为失败。
                _logger.LogWarning(
                    cacheException,
                    "商品统计完成后的看板缓存清理失败（best-effort）: JobId={JobId}, Date={Date}",
                    jobId,
                    dateKey
                );
            }

            try
            {
                if (
                    !await leaseService.CompleteAsync(
                        SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
                        dateKey,
                        leaseToken,
                        true
                    )
                )
                {
                    // 状态已经是同 owner Fresh；租约 fencing 失败时等待其失效，finalizer 会被有效租约挡住。
                    _logger.LogWarning(
                        "商品统计成功但租约 completion 未取得所有权: JobId={JobId}, Date={Date}",
                        jobId,
                        dateKey
                    );
                }
            }
            catch (Exception leaseCompletionException)
            {
                _logger.LogError(
                    leaseCompletionException,
                    "商品统计成功但租约 completion 异常: JobId={JobId}, Date={Date}",
                    jobId,
                    dateKey
                );
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(leaseToken))
            {
                try
                {
                    await leaseService.CompleteAsync(
                        SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
                        dateKey,
                        leaseToken,
                        false,
                        ex.Message
                    );
                }
                catch (Exception leaseException)
                {
                    _logger.LogError(
                        leaseException,
                        "商品统计失败租约完成异常: JobId={JobId}, Date={Date}",
                        jobId,
                        dateKey
                    );
                }
            }

            // executor 的 guarded failure path 负责 Failed；这里不能无条件覆盖可能已换 owner 的状态。
            _logger.LogError(
                ex,
                "商品统计持久队列执行失败: JobId={JobId}, Date={Date}",
                jobId,
                dateKey
            );
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                await ReturnClaimToQueueAsync(context, jobId, date);
                throw;
            }
            return !string.IsNullOrWhiteSpace(leaseToken);
        }
    }

    private sealed record GlobalLeaseOwnership(
        ScheduledTaskLeaseService LeaseService,
        string LeaseToken
    );

    private async Task<int> EnsureOrphanTaskLogsAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken
    )
    {
        var activeStates = await _context.Db.Queryable<SalesStatisticRefreshState>()
            .Where(state =>
                state.StatisticType == SalesStatisticType.ProductStoreDaily
                && state.JobId != null
                && (
                    state.Status == SalesStatisticRefreshStatus.Queued
                    || state.Status == SalesStatisticRefreshStatus.Running
                )
            )
            .ToListAsync();
        if (activeStates.Count == 0)
        {
            return 0;
        }

        var jobIds = activeStates.Select(state => state.JobId!.Value).Distinct().ToList();
        var existingIds = (await _context.Db.Queryable<ScheduledTaskLog>()
                .Where(task => jobIds.Contains(task.Id))
                .Select(task => task.Id)
                .ToListAsync())
            .ToHashSet();
        var progress = 0;

        foreach (var group in activeStates.Where(state => !existingIds.Contains(state.JobId!.Value))
                     .GroupBy(state => state.JobId!.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = group.OrderBy(state => state.Date).ToList();
            if (rows.Any(state =>
                    !state.RequestedAtUtc.HasValue
                    && !state.StartedAtUtc.HasValue
                    && !state.LastCheckedAtUtc.HasValue
                ))
            {
                foreach (var row in rows.Where(state =>
                             !state.RequestedAtUtc.HasValue
                             && !state.StartedAtUtc.HasValue
                             && !state.LastCheckedAtUtc.HasValue
                         ))
                {
                    progress += await _context.Db.Updateable<SalesStatisticRefreshState>()
                        .SetColumns(state => state.LastCheckedAtUtc == nowUtc)
                        .Where(state =>
                            state.StatisticType == SalesStatisticType.ProductStoreDaily
                            && state.Date == row.Date
                            && state.JobId == group.Key
                            && state.LastCheckedAtUtc == null
                        )
                        .ExecuteCommandAsync();
                }
                continue;
            }

            var newestWatermark = rows
                .Select(state => state.StartedAtUtc ?? state.RequestedAtUtc ?? state.LastCheckedAtUtc)
                .Where(value => value.HasValue)
                .Max(value => value!.Value);
            if (nowUtc - newestWatermark < OrphanManifestGracePeriod)
            {
                continue;
            }

            // orphan 是否成立由 active 状态和安全宽限确认；manifest 则必须按 JobId 收齐全部历史状态，
            // 包括已经 Failed/Fresh 的日期，否则日志会遗失原始任务边界。
            var manifestStates = await _context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(state =>
                    state.StatisticType == SalesStatisticType.ProductStoreDaily
                    && state.JobId == group.Key
                )
                .ToListAsync();
            var dates = manifestStates
                .Select(state => state.Date.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();
            var taskLog = await _taskLogService.LogTaskStartStrictAsync(
                group.Key,
                TaskType.RecalculateProductStoreDaily,
                CreateTaskParameters(dates, ResolveMaxConcurrency(dates, 1)),
                TaskTrigger.Manual,
                canRetry: false,
                startedAtUtc: rows
                    .Select(state => state.RequestedAtUtc ?? state.StartedAtUtc ?? state.LastCheckedAtUtc)
                    .Where(value => value.HasValue)
                    .Min(value => value!.Value)
            );
            _taskLogService.PublishTaskStartedAfterCommit(taskLog);
            progress++;
            _logger.LogWarning(
                "已为旧商品统计 orphan 状态补建任务日志: JobId={JobId}, Dates={DateCount}",
                group.Key,
                dates.Count
            );
        }

        return progress;
    }

    private Task<bool> HasActiveDateLeaseAsync(DateTime date, DateTime nowUtc)
    {
        var scopeKey = date.Date.ToString("yyyy-MM-dd");
        return _context.Db.Queryable<ScheduledTaskLease>()
            .Where(lease =>
                lease.TaskType == SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType
                && lease.ScopeKey == scopeKey
                && lease.Status == ScheduledTaskLeaseStatus.Running
                && lease.LeaseUntilUtc != null
                && lease.LeaseUntilUtc > nowUtc
            )
            .AnyAsync();
    }

    private async Task<bool> HasAnyActiveDateLeaseAsync(
        IReadOnlyCollection<DateTime> dates,
        DateTime nowUtc
    )
    {
        var manifestScopes = dates
            .Select(date => date.Date.ToString("yyyy-MM-dd"))
            .ToHashSet(StringComparer.Ordinal);
        var activeScopes = await _context.Db.Queryable<ScheduledTaskLease>()
            .Where(lease =>
                lease.TaskType == SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType
                && lease.Status == ScheduledTaskLeaseStatus.Running
                && lease.LeaseUntilUtc != null
                && lease.LeaseUntilUtc > nowUtc
            )
            .Select(lease => lease.ScopeKey)
            .ToListAsync();
        return activeScopes.Any(manifestScopes.Contains);
    }

    private async Task<bool> FailMalformedManifestAsync(Guid jobId, string manifestError)
    {
        var nowUtc = DateTime.UtcNow;
        var diagnostic = $"商品统计不可变日期清单损坏: {manifestError}";
        await _context.Db.Updateable<SalesStatisticRefreshState>()
            .SetColumns(state => state.Status == SalesStatisticRefreshStatus.Failed)
            .SetColumns(state => state.CompletedAtUtc == nowUtc)
            .SetColumns(state => state.LastCheckedAtUtc == nowUtc)
            .SetColumns(state => state.ErrorMessage == diagnostic)
            .Where(state =>
                state.StatisticType == SalesStatisticType.ProductStoreDaily
                && state.JobId == jobId
                && (
                    state.Status == SalesStatisticRefreshStatus.Queued
                    || state.Status == SalesStatisticRefreshStatus.Running
                )
            )
            .ExecuteCommandAsync();

        var completed = await _taskLogService.TryCompleteProductStoreDailyTaskAsync(
            jobId,
            false,
            diagnostic
        );
        if (completed)
        {
            _logger.LogError(
                "商品统计任务因 manifest 损坏已失败终结: JobId={JobId}, Error={Error}",
                jobId,
                manifestError
            );
        }
        return completed;
    }

    private static Task<int> ReturnClaimToQueueAsync(
        SqlSugarContext context,
        Guid jobId,
        DateTime date
    )
    {
        var targetDate = date.Date;
        return context.Db.Updateable<SalesStatisticRefreshState>()
            .SetColumns(state => state.Status == SalesStatisticRefreshStatus.Queued)
            .SetColumns(state => state.StartedAtUtc == null)
            .SetColumns(state => state.CompletedAtUtc == null)
            .SetColumns(state => state.LastCheckedAtUtc == DateTime.UtcNow)
            .Where(state =>
                state.StatisticType == SalesStatisticType.ProductStoreDaily
                && state.Date == targetDate
                && state.JobId == jobId
                && state.Status == SalesStatisticRefreshStatus.Running
            )
            .ExecuteCommandAsync();
    }

    private static TaskParameters CreateTaskParameters(
        IReadOnlyList<DateTime> dates,
        int maxConcurrency
    )
    {
        return new TaskParameters
        {
            StartDate = dates[0].ToString("yyyy-MM-dd"),
            EndDate = dates[^1].ToString("yyyy-MM-dd"),
            MaxConcurrency = maxConcurrency,
            CustomParameters = new Dictionary<string, object>
            {
                ["dates"] = dates.Select(date => date.ToString("yyyy-MM-dd")).ToList(),
            },
        };
    }

    private static bool TryReadManifest(
        ScheduledTaskLog taskLog,
        out TaskParameters parameters,
        out List<DateTime> dates,
        out string error
    )
    {
        parameters = new TaskParameters();
        dates = new List<DateTime>();
        if (string.IsNullOrWhiteSpace(taskLog.TaskParameters))
        {
            error = "缺少 TaskParameters JSON";
            return false;
        }

        try
        {
            parameters = JsonSerializer.Deserialize<TaskParameters>(taskLog.TaskParameters)
                ?? new TaskParameters();
        }
        catch (JsonException ex)
        {
            error = $"TaskParameters JSON 无效: {ex.Message}";
            return false;
        }

        if (
            parameters.CustomParameters == null
            || !parameters.CustomParameters.TryGetValue("dates", out var rawDates)
            || rawDates == null
        )
        {
            error = "缺少 CustomParameters.dates";
            return false;
        }

        if (!TryReadManifestDateValues(rawDates, out var dateValues))
        {
            error = "dates 必须是完整的字符串日期数组";
            return false;
        }
        if (dateValues.Count == 0)
        {
            error = "dates 日期清单为空";
            return false;
        }

        foreach (var value in dateValues)
        {
            if (
                string.IsNullOrWhiteSpace(value)
                || !DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed
                )
            )
            {
                error = $"dates 包含非法日期值: {value}";
                return false;
            }
            dates.Add(parsed.Date);
        }

        dates = dates
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        error = string.Empty;
        return true;
    }

    private static bool TryReadManifestDateValues(object rawDates, out List<string> values)
    {
        values = new List<string>();
        switch (rawDates)
        {
            case JsonElement element when element.ValueKind == JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }
                    values.Add(item.GetString() ?? string.Empty);
                }
                return true;
            case IEnumerable<string> stringValues:
                values.AddRange(stringValues);
                return true;
            case IEnumerable<object> objectValues:
                foreach (var item in objectValues)
                {
                    if (item is string text)
                    {
                        values.Add(text);
                    }
                    else if (item is JsonElement json && json.ValueKind == JsonValueKind.String)
                    {
                        values.Add(json.GetString() ?? string.Empty);
                    }
                    else
                    {
                        return false;
                    }
                }
                return true;
            default:
                return false;
        }
    }

    private static int ResolveMaxConcurrency(
        IReadOnlyCollection<DateTime> dates,
        int requestedMaxConcurrency
    )
    {
        if (dates.Any(date => date.Year == 2025))
        {
            return 1;
        }
        var defaulted = requestedMaxConcurrency < 1 ? 3 : requestedMaxConcurrency;
        return Math.Clamp(defaulted, 1, 10);
    }

    private static string BuildSubmitMessage(
        int submittedCount,
        int skippedCount,
        int activeJobCount
    )
    {
        if (submittedCount > 0)
        {
            return $"已提交 {submittedCount} 天商品统计持久任务，跳过 {skippedCount} 天活动任务";
        }
        return activeJobCount > 1
            ? $"所选 {skippedCount} 天分属 {activeJobCount} 个活动商品统计任务，本次未重复提交"
            : $"所选 {skippedCount} 天已有活动商品统计任务，本次未重复提交";
    }

    private static string NormalizeError(string? errorMessage)
    {
        return string.IsNullOrWhiteSpace(errorMessage) ? "未知错误" : errorMessage.Trim();
    }
}
