using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Performance;

public sealed class PerformanceOperationalRunWriterService : BackgroundService
{
    internal const int MaxOutboxRetryCount = 20;
    internal const int MaxInMemoryFallbackRetryCount = 6;
    private const int OutboxBatchSize = 100;
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.Ordinal)
    {
        "success",
        "failure",
        "cancelled",
        "interrupted",
    };
    private static readonly HashSet<string> AuthoritativeTerminalStatuses = new(StringComparer.Ordinal)
    {
        "success",
        "failure",
        "cancelled",
    };

    private readonly PerformanceOperationalRunQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PerformanceMetricsOptions> _options;
    private readonly ILogger<PerformanceOperationalRunWriterService> _logger;
    private readonly string _ownerInstanceId;

    public PerformanceOperationalRunWriterService(
        PerformanceOperationalRunQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<PerformanceMetricsOptions> options,
        ILogger<PerformanceOperationalRunWriterService> logger
    )
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        var configuredInstance = string.IsNullOrWhiteSpace(options.Value.InstanceId)
            ? Environment.MachineName
            : options.Value.InstanceId.Trim();
        _ownerInstanceId = $"{configuredInstance}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        PerformanceOperationalRunBridge.Configure(_queue);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqlSugarContext>().Db;
            var recovered = await MarkInterruptedAsync(db, DateTime.UtcNow);
            if (recovered > 0)
            {
                _logger.LogWarning("已标记 {Count} 个上次进程未完成的性能运行记录", recovered);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "恢复未完成性能运行记录失败，将在后续运行中继续写入新记录");
        }
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        PerformanceOperationalRunBridge.Configure(null);
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            ProcessTransitionsAsync(stoppingToken),
            PollOutboxAsync(stoppingToken),
            RenewLeasesAsync(stoppingToken)
        );
    }

    private async Task ProcessTransitionsAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SqlSugarContext>().Db;
                if (item.PendingOutbox != null)
                {
                    await PersistOutboxAsync(db, item.PendingOutbox);
                }
                await ProcessOutboxAsync(
                    db,
                    item.OutboxId,
                    _options,
                    _ownerInstanceId,
                    DateTime.UtcNow
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "处理运行状态 outbox 失败: {OutboxId}",
                    item.OutboxId
                );
                if (item.PendingOutbox != null)
                {
                    if (item.InMemoryAttempt >= MaxInMemoryFallbackRetryCount)
                    {
                        _queue.ReportDroppedFallback("retry_exhausted");
                        continue;
                    }
                    var retry = item with { InMemoryAttempt = item.InMemoryAttempt + 1 };
                    await Task.Delay(
                        InMemoryRetryDelay(retry.InMemoryAttempt),
                        stoppingToken
                    );
                    _queue.Requeue(retry);
                }
            }
        }
    }

    internal static TimeSpan InMemoryRetryDelay(int retryAttempt)
    {
        var exponent = Math.Clamp(retryAttempt - 1, 0, 5);
        return TimeSpan.FromSeconds(Math.Min(30, 1 << exponent));
    }

    private async Task PollOutboxAsync(CancellationToken stoppingToken)
    {
        await DrainDueOutboxAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DrainDueOutboxAsync(stoppingToken);
        }
    }

    private async Task DrainDueOutboxAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqlSugarContext>().Db;
            var now = DateTime.UtcNow;
            var ids = await db
                .Queryable<PerformanceOperationalRunTransitionOutbox>()
                .Where(item =>
                    item.DeadLetteredAtUtc == null && item.NextAttemptAtUtc <= now
                )
                .OrderBy(item => item.CreatedAt)
                .Take(OutboxBatchSize)
                .Select(item => item.Id)
                .ToListAsync(stoppingToken);
            foreach (var id in ids)
            {
                stoppingToken.ThrowIfCancellationRequested();
                try
                {
                    await ProcessOutboxAsync(
                        db,
                        id,
                        _options,
                        _ownerInstanceId,
                        now
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "轮询处理运行状态 outbox 失败: {OutboxId}", id);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "轮询运行状态 outbox 失败，将在下一周期重试");
        }
    }

    internal static PerformanceOperationalRunTransitionOutbox CreateOutbox(
        PerformanceOperationalRunTransition transition,
        DateTime utcNow
    ) =>
        new()
        {
            ExternalRunId = Normalize(transition.ExternalRunId, 120, "unknown"),
            Category = Normalize(transition.Category, 40, "background"),
            Operation = Normalize(transition.Operation, 160, "unknown"),
            Status = Normalize(transition.Status, 30, "failure"),
            OccurredAtUtc = AsUtc(transition.OccurredAtUtc),
            Attempt = Math.Max(1, transition.Attempt),
            Backlog = transition.Backlog.HasValue ? Math.Max(0, transition.Backlog.Value) : null,
            NextAttemptAtUtc = AsUtc(utcNow),
        };

    internal static async Task<Guid> EnqueueAsync(
        ISqlSugarClient db,
        PerformanceOperationalRunTransition transition,
        DateTime utcNow
    )
    {
        var outbox = CreateOutbox(transition, utcNow);
        await PersistOutboxAsync(db, outbox);
        return outbox.Id;
    }

    internal static async Task PersistOutboxAsync(
        ISqlSugarClient db,
        PerformanceOperationalRunTransitionOutbox outbox
    )
    {
        try
        {
            await db.Insertable(outbox).ExecuteCommandAsync();
        }
        catch
        {
            // 网络中断可能发生在提交成功之后；按主键复核可避免重试时制造重复事件。
            if (await db.Queryable<PerformanceOperationalRunTransitionOutbox>().AnyAsync(item =>
                item.Id == outbox.Id
            ))
            {
                return;
            }
            throw;
        }
    }

    internal static async Task<bool> ProcessOutboxAsync(
        ISqlSugarClient db,
        Guid outboxId,
        IOptions<PerformanceMetricsOptions> options,
        string ownerInstanceId,
        DateTime utcNow,
        Func<Task>? afterApplyRollback = null
    )
    {
        db.Ado.BeginTran();
        var transactionOpen = true;
        try
        {
            var outbox = await db
                .Queryable<PerformanceOperationalRunTransitionOutbox>()
                .Where(item => item.Id == outboxId)
                .FirstAsync();
            if (!IsOutboxDue(outbox, utcNow))
            {
                db.Ado.RollbackTran();
                transactionOpen = false;
                return false;
            }

            await AcquireOutboxDatabaseLockAsync(db, outbox);
            // 获取跨实例锁后重新确认记录仍存在，避免重复通知造成二次应用。
            outbox = await db
                .Queryable<PerformanceOperationalRunTransitionOutbox>()
                .Where(item => item.Id == outboxId)
                .FirstAsync();
            if (!IsOutboxDue(outbox, utcNow))
            {
                db.Ado.RollbackTran();
                transactionOpen = false;
                return false;
            }

            // 保存点让应用写入失败时只撤销运行记录变更，外层事务及跨实例锁继续保持。
            // 退避/死信因此会先持久化，等待实例随后才能通过锁后重验。
            await CreateOutboxApplySavepointAsync(db);
            try
            {
                await ApplyAsync(
                    db,
                    new PerformanceOperationalRunTransition(
                        outbox.ExternalRunId,
                        outbox.Category,
                        outbox.Operation,
                        outbox.Status,
                        outbox.OccurredAtUtc,
                        outbox.Attempt,
                        outbox.Backlog
                    ),
                    options,
                    ownerInstanceId
                );
                await db
                    .Deleteable<PerformanceOperationalRunTransitionOutbox>()
                    .Where(item => item.Id == outboxId)
                    .ExecuteCommandAsync();
                db.Ado.CommitTran();
                transactionOpen = false;
                return true;
            }
            catch (Exception ex)
            {
                await RollbackOutboxApplySavepointAsync(db);
                if (afterApplyRollback != null)
                {
                    await afterApplyRollback();
                }
                await RecordOutboxFailureAsync(db, outboxId, utcNow, ex);
                db.Ado.CommitTran();
                transactionOpen = false;
                throw;
            }
        }
        catch
        {
            if (transactionOpen)
            {
                try
                {
                    db.Ado.RollbackTran();
                }
                catch
                {
                    // 连接或事务已失效时只能保留原 outbox，由下一轮重新尝试。
                }
            }
            throw;
        }
    }

    internal static async Task RecordOutboxFailureAsync(
        ISqlSugarClient db,
        Guid outboxId,
        DateTime utcNow,
        Exception exception
    )
    {
        var errorType = Normalize(exception.GetType().Name, 160, "Exception");
        // 先用数据库表达式原子递增，避免多实例同时失败时以陈旧实体相互覆盖计数。
        var updated = await db
            .Updateable<PerformanceOperationalRunTransitionOutbox>()
            .SetColumns(item => item.RetryCount == item.RetryCount + 1)
            .SetColumns(item => item.LastErrorType == errorType)
            .SetColumns(item => item.UpdatedAt == utcNow)
            .Where(item =>
                item.Id == outboxId
                && item.DeadLetteredAtUtc == null
                && item.RetryCount < MaxOutboxRetryCount
            )
            .ExecuteCommandAsync();
        if (updated == 0)
        {
            return;
        }

        var outbox = await db
            .Queryable<PerformanceOperationalRunTransitionOutbox>()
            .Where(item => item.Id == outboxId)
            .FirstAsync();
        if (outbox == null)
        {
            return;
        }

        if (outbox.RetryCount >= MaxOutboxRetryCount)
        {
            await db
                .Updateable<PerformanceOperationalRunTransitionOutbox>()
                .SetColumns(item => new PerformanceOperationalRunTransitionOutbox
                {
                    DeadLetteredAtUtc = utcNow,
                    NextAttemptAtUtc = utcNow,
                    UpdatedAt = utcNow,
                })
                .Where(item =>
                    item.Id == outboxId
                    && item.DeadLetteredAtUtc == null
                    && item.RetryCount >= MaxOutboxRetryCount
                )
                .ExecuteCommandAsync();
            return;
        }

        var backoffSeconds = Math.Min(300, 1 << Math.Min(outbox.RetryCount, 8));
        var nextAttemptAtUtc = utcNow.AddSeconds(backoffSeconds);
        // 只允许退避时间向后推进；较晚完成的旧并发调用不能缩短已经写入的退避。
        await db
            .Updateable<PerformanceOperationalRunTransitionOutbox>()
            .SetColumns(item => item.NextAttemptAtUtc == nextAttemptAtUtc)
            .Where(item =>
                item.Id == outboxId
                && item.DeadLetteredAtUtc == null
                && item.RetryCount < MaxOutboxRetryCount
                && item.NextAttemptAtUtc < nextAttemptAtUtc
            )
            .ExecuteCommandAsync();
    }

    internal static bool IsOutboxDue(
        PerformanceOperationalRunTransitionOutbox? outbox,
        DateTime utcNow
    ) =>
        outbox != null
        && !outbox.DeadLetteredAtUtc.HasValue
        && AsUtc(outbox.NextAttemptAtUtc) <= AsUtc(utcNow);

    private static async Task AcquireOutboxDatabaseLockAsync(
        ISqlSugarClient db,
        PerformanceOperationalRunTransitionOutbox outbox
    )
    {
        if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            var key = $"{outbox.Category}|backend|{outbox.ExternalRunId}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
            const string sql =
                """
                DECLARE @Result int;
                EXEC @Result = sys.sp_getapplock
                    @Resource = @LockResource,
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction',
                    @LockTimeout = 60000;
                IF @Result < 0
                    THROW 51002, '无法获取运行状态 outbox 锁', 1;
                """;
            await db.Ado.ExecuteCommandAsync(
                sql,
                new SugarParameter("@LockResource", $"PerformanceOperationalRun_{hash}")
            );
            return;
        }

        if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
        {
            await db.Ado.ExecuteCommandAsync(
                "UPDATE PerformanceOperationalRunTransitionOutbox SET UpdatedAt = UpdatedAt WHERE Id = @Id",
                new SugarParameter("@Id", outbox.Id)
            );
        }
    }

    private static Task CreateOutboxApplySavepointAsync(ISqlSugarClient db)
    {
        const string savepointName = "PerformanceOutboxApply";
        return db.CurrentConnectionConfig.DbType switch
        {
            DbType.SqlServer => db.Ado.ExecuteCommandAsync($"SAVE TRANSACTION {savepointName}"),
            DbType.Sqlite => db.Ado.ExecuteCommandAsync($"SAVEPOINT {savepointName}"),
            _ => Task.CompletedTask,
        };
    }

    private static Task RollbackOutboxApplySavepointAsync(ISqlSugarClient db)
    {
        const string savepointName = "PerformanceOutboxApply";
        return db.CurrentConnectionConfig.DbType switch
        {
            DbType.SqlServer => db.Ado.ExecuteCommandAsync(
                $"ROLLBACK TRANSACTION {savepointName}"
            ),
            DbType.Sqlite => db.Ado.ExecuteCommandAsync(
                $"ROLLBACK TO SAVEPOINT {savepointName}"
            ),
            _ => Task.CompletedTask,
        };
    }

    private async Task RenewLeasesAsync(CancellationToken stoppingToken)
    {
        var heartbeatSeconds = Math.Clamp(
            _options.Value.OperationalRunHeartbeatSeconds,
            5,
            300
        );
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(heartbeatSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SqlSugarContext>().Db;
                await RenewOwnedLeasesAndMarkInterruptedAsync(
                    db,
                    _ownerInstanceId,
                    DateTime.UtcNow,
                    _options.Value
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "续期性能运行租约失败");
            }
        }
    }

    internal static async Task ApplyAsync(
        ISqlSugarClient db,
        PerformanceOperationalRunTransition transition,
        IOptions<PerformanceMetricsOptions> options
    ) =>
        await ApplyAsync(
            db,
            transition,
            options,
            Normalize(options.Value.InstanceId, 120, "legacy-instance")
        );

    internal static async Task ApplyAsync(
        ISqlSugarClient db,
        PerformanceOperationalRunTransition transition,
        IOptions<PerformanceMetricsOptions> options,
        string ownerInstanceId
    )
    {
        var externalRunId = Normalize(transition.ExternalRunId, 120, "unknown");
        var category = Normalize(transition.Category, 40, "background");
        var operation = Normalize(transition.Operation, 160, "unknown");
        var status = NormalizeStatus(transition.Status);
        var occurredAt = AsUtc(transition.OccurredAtUtc);
        var source = "backend";
        var existing = await db
            .Queryable<PerformanceOperationalRun>()
            .Where(item =>
                item.ExternalRunId == externalRunId
                && item.Category == category
                && item.Source == source
            )
            .FirstAsync();

        var incomingAttempt = Math.Max(1, transition.Attempt);
        if (existing != null && ShouldIgnoreTransition(existing, status, incomingAttempt, occurredAt))
        {
            return;
        }

        var run = existing
            ?? new PerformanceOperationalRun
            {
                ExternalRunId = externalRunId,
                Category = category,
                Operation = operation,
                Environment = Normalize(options.Value.DefaultEnvironment, 60, "Production"),
                Source = source,
                QueuedAtUtc = occurredAt,
            };
        var isNewAttempt = incomingAttempt > run.Attempt;
        run.Operation = operation;
        run.Status = status;
        run.OwnerInstanceId = Normalize(ownerInstanceId, 160, "unknown-instance");
        run.Attempt = Math.Max(run.Attempt, incomingAttempt);
        run.LastTransitionAtUtc = occurredAt;
        if (isNewAttempt)
        {
            // 一个 OperationalRun 覆盖首次执行及后续重试；总耗时从首次实际开始算起。
            run.CompletedAtUtc = null;
            run.DurationMs = null;
        }
        if (transition.Backlog.HasValue)
        {
            run.Backlog = Math.Max(0, transition.Backlog.Value);
        }
        if (status == "running" && !run.StartedAtUtc.HasValue)
        {
            run.StartedAtUtc = occurredAt;
        }
        if (TerminalStatuses.Contains(status))
        {
            run.CompletedAtUtc = occurredAt;
            var durationStart = run.StartedAtUtc ?? run.QueuedAtUtc;
            run.DurationMs = Math.Max(0, (long)(occurredAt - durationStart).TotalMilliseconds);
            run.LeaseExpiresAtUtc = null;
            run.LastHeartbeatAtUtc = occurredAt;
        }
        else
        {
            var leaseSeconds = Math.Clamp(options.Value.OperationalRunLeaseSeconds, 30, 3600);
            run.LastHeartbeatAtUtc = occurredAt;
            run.LeaseExpiresAtUtc = occurredAt.AddSeconds(leaseSeconds);
        }

        if (existing == null)
        {
            await db.Insertable(run).ExecuteCommandAsync();
        }
        else
        {
            await db.Updateable(run).ExecuteCommandAsync();
        }

        // 成功率、失败率和最终耗时由持久运行记录在查询/冻结时计算。
        // 这样一次先失败后成功的重试不会被重复计为两个完成运行。
    }

    internal static async Task<int> MarkInterruptedAsync(ISqlSugarClient db, DateTime utcNow)
    {
        var active = await db
            .Queryable<PerformanceOperationalRun>()
            .Where(item =>
                (
                    item.Status == "queued"
                    || item.Status == "running"
                    || item.Status == "retry_wait"
                )
                && (
                    item.LeaseExpiresAtUtc == null
                    || item.LeaseExpiresAtUtc <= utcNow
                )
            )
            .ToListAsync();
        var affected = 0;
        foreach (var run in active)
        {
            var durationStart = run.StartedAtUtc ?? run.QueuedAtUtc;
            var durationMs = Math.Max(0, (long)(utcNow - durationStart).TotalMilliseconds);
            // 只更新恢复字段并在执行时重验活动状态/过期租约，避免陈旧快照覆盖并发终态。
            affected += await db
                .Updateable<PerformanceOperationalRun>()
                .SetColumns(item => new PerformanceOperationalRun
                {
                    Status = "interrupted",
                    CompletedAtUtc = utcNow,
                    DurationMs = durationMs,
                    LeaseExpiresAtUtc = null,
                    LastTransitionAtUtc = utcNow,
                })
                .Where(item =>
                    item.Id == run.Id
                    && item.Status == run.Status
                    && (
                        item.Status == "queued"
                        || item.Status == "running"
                        || item.Status == "retry_wait"
                    )
                    && (
                        item.LeaseExpiresAtUtc == null
                        || item.LeaseExpiresAtUtc <= utcNow
                    )
                )
                .ExecuteCommandAsync();
        }
        return affected;
    }

    internal static async Task<int> RenewOwnedLeasesAsync(
        ISqlSugarClient db,
        string ownerInstanceId,
        DateTime utcNow,
        PerformanceMetricsOptions options
    )
    {
        var leaseSeconds = Math.Clamp(options.OperationalRunLeaseSeconds, 30, 3600);
        // 字段级条件更新不会把读取时的 Status/CompletedAtUtc 等陈旧值写回。
        return await db
            .Updateable<PerformanceOperationalRun>()
            .SetColumns(item => new PerformanceOperationalRun
            {
                LastHeartbeatAtUtc = utcNow,
                LeaseExpiresAtUtc = utcNow.AddSeconds(leaseSeconds),
            })
            .Where(item =>
                item.OwnerInstanceId == ownerInstanceId
                && (
                    item.Status == "queued"
                    || item.Status == "running"
                    || item.Status == "retry_wait"
                )
            )
            .ExecuteCommandAsync();
    }

    internal static async Task<int> RenewOwnedLeasesAndMarkInterruptedAsync(
        ISqlSugarClient db,
        string ownerInstanceId,
        DateTime utcNow,
        PerformanceMetricsOptions options
    )
    {
        // 先续当前实例，避免心跳调度抖动把仍存活的运行误判为中断；随后清扫重启遗留的过期租约。
        await RenewOwnedLeasesAsync(db, ownerInstanceId, utcNow, options);
        return await MarkInterruptedAsync(db, utcNow);
    }

    private static string NormalizeStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "queued" => "queued",
        "running" => "running",
        "retry_wait" => "retry_wait",
        "success" or "succeeded" => "success",
        "failure" or "failed" or "partiallysucceeded" or "partially_succeeded" => "failure",
        "cancelled" or "canceled" => "cancelled",
        "interrupted" => "interrupted",
        _ => "failure",
    };

    private static bool ShouldIgnoreTransition(
        PerformanceOperationalRun existing,
        string incomingStatus,
        int incomingAttempt,
        DateTime occurredAt
    )
    {
        if (
            AuthoritativeTerminalStatuses.Contains(existing.Status)
            && incomingAttempt <= existing.Attempt
        )
        {
            return true;
        }

        var incomingIsAuthoritative = AuthoritativeTerminalStatuses.Contains(incomingStatus);
        if (incomingAttempt < existing.Attempt && !incomingIsAuthoritative)
        {
            return true;
        }

        // interrupted 是租约恢复的推断状态；随后到达的权威完成事件必须能够纠正它。
        if (incomingIsAuthoritative && existing.Status == "interrupted")
        {
            return false;
        }

        var lastTransitionAt = existing.LastTransitionAtUtc
            ?? existing.CompletedAtUtc
            ?? existing.StartedAtUtc
            ?? existing.QueuedAtUtc;
        if (occurredAt < lastTransitionAt)
        {
            return true;
        }

        return occurredAt == lastTransitionAt
            && incomingAttempt <= existing.Attempt
            && StatusPrecedence(incomingStatus) <= StatusPrecedence(existing.Status);
    }

    private static int StatusPrecedence(string status) => status switch
    {
        "queued" => 0,
        "running" => 1,
        "retry_wait" => 2,
        "interrupted" => 3,
        "success" or "failure" or "cancelled" => 4,
        _ => 0,
    };

    private static DateTime AsUtc(DateTime value) => PerformanceUtc.Normalize(value);

    private static string Normalize(string? value, int maxLength, string fallback)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
