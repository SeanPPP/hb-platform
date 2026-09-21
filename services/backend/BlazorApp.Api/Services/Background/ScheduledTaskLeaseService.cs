using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Background
{
    /// <summary>
    /// 基于数据库的任务租约服务，避免多实例或手动重复触发同一范围统计。
    /// </summary>
    public class ScheduledTaskLeaseService
    {
        private static readonly DateTime SessionGuardLeaseUntilUtc = new(
            9999,
            12,
            31,
            23,
            59,
            59,
            DateTimeKind.Utc
        );
        private readonly SqlSugarContext _context;
        private readonly ScheduledTaskOptions _options;
        private readonly ILogger<ScheduledTaskLeaseService> _logger;

        public ScheduledTaskLeaseService(
            SqlSugarContext context,
            IOptions<ScheduledTaskOptions> options,
            ILogger<ScheduledTaskLeaseService> logger
        )
        {
            _context = context;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<ScheduledTaskLeaseAcquireResult> TryAcquireAsync(
            string taskType,
            string scopeKey,
            TimeSpan leaseDuration
        )
        {
            var normalizedTaskType = NormalizeKey(taskType);
            var normalizedScopeKey = NormalizeKey(scopeKey);
            var ownerInstanceId = ResolveInstanceId();
            var now = DateTime.UtcNow;
            var leaseUntil = now.Add(leaseDuration);
            var leaseToken = Guid.NewGuid().ToString("N");

            var existing = await QueryLeaseAsync(normalizedTaskType, normalizedScopeKey);
            if (existing == null)
            {
                var newLease = new ScheduledTaskLease
                {
                    TaskType = normalizedTaskType,
                    ScopeKey = normalizedScopeKey,
                    Status = ScheduledTaskLeaseStatus.Running,
                    OwnerInstanceId = ownerInstanceId,
                    LeaseToken = leaseToken,
                    LeaseUntilUtc = leaseUntil,
                    StartedAtUtc = now,
                    CompletedAtUtc = null,
                    LastError = null,
                    UpdatedAtUtc = now,
                };

                try
                {
                    await _context.Db.Insertable(newLease).ExecuteCommandAsync();
                    return ScheduledTaskLeaseAcquireResult.CreateAcquired(newLease);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "统计任务租约插入发生并发冲突，改为尝试抢占: {TaskType} {ScopeKey}",
                        normalizedTaskType,
                        normalizedScopeKey
                    );
                }
            }

            // 只允许抢占非运行中或已过期的租约；未过期 Running 直接跳过。
            var updatedRows = await _context.Db.Updateable<ScheduledTaskLease>()
                .SetColumns(x => x.Status == ScheduledTaskLeaseStatus.Running)
                .SetColumns(x => x.OwnerInstanceId == ownerInstanceId)
                .SetColumns(x => x.LeaseToken == leaseToken)
                .SetColumns(x => x.LeaseUntilUtc == leaseUntil)
                .SetColumns(x => x.StartedAtUtc == now)
                .SetColumns(x => x.CompletedAtUtc == null)
                .SetColumns(x => x.LastError == null)
                .SetColumns(x => x.UpdatedAtUtc == now)
                .Where(x =>
                    x.TaskType == normalizedTaskType
                    && x.ScopeKey == normalizedScopeKey
                    && (
                        x.Status != ScheduledTaskLeaseStatus.Running
                        || ((x.LeaseToken == null
                                || !x.LeaseToken.StartsWith(
                                    SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix
                                ))
                            && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
                    )
                )
                .ExecuteCommandAsync();

            if (updatedRows > 0)
            {
                var lease = await QueryLeaseAsync(normalizedTaskType, normalizedScopeKey);
                return ScheduledTaskLeaseAcquireResult.CreateAcquired(lease!);
            }

            var runningLease = await QueryLeaseAsync(normalizedTaskType, normalizedScopeKey);
            await IncrementDuplicateSkipAsync(normalizedTaskType, normalizedScopeKey);
            return ScheduledTaskLeaseAcquireResult.CreateRunning(runningLease);
        }

        /// <summary>
        /// 仅当调用方已持有同一 HBweb 日期的 SQL Server Session applock 时，才可取得
        /// sqlsess1 标记租约。旧 TTL 租约绝不提前回收，避免首次上线时覆盖旧二进制执行。
        /// </summary>
        internal async Task<ScheduledTaskLeaseAcquireResult> TryAcquireSessionGuardedAsync(
            string taskType,
            string scopeKey,
            SalesStatisticsDateExecutionGuard guard
        )
        {
            if (!guard.IsSqlServerSessionGuarded)
                throw new InvalidOperationException("SQL 会话租约必须持有全日统计 Session guard");
            if (!guard.IsBoundTo(_context.Db, scopeKey))
                throw new InvalidOperationException("SQL 会话租约 guard 与当前数据库或日期范围不匹配");
            await guard.EnsureActiveAsync("取得日期租约");

            var normalizedTaskType = NormalizeKey(taskType);
            var normalizedScopeKey = NormalizeKey(scopeKey);
            var ownerInstanceId = ResolveInstanceId();
            var now = DateTime.UtcNow;
            var leaseToken = SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix
                + Guid.NewGuid().ToString("N");
            // SqlSugar 不能将私有静态字段翻译为 UPDATE 常量，保留为本次 SQL 的局部参数。
            var sessionLeaseUntil = SessionGuardLeaseUntilUtc;

            var existing = await QueryLeaseAsync(normalizedTaskType, normalizedScopeKey);
            if (existing == null)
            {
                var newLease = new ScheduledTaskLease
                {
                    TaskType = normalizedTaskType,
                    ScopeKey = normalizedScopeKey,
                    Status = ScheduledTaskLeaseStatus.Running,
                    OwnerInstanceId = ownerInstanceId,
                    LeaseToken = leaseToken,
                    LeaseUntilUtc = sessionLeaseUntil,
                    StartedAtUtc = now,
                    CompletedAtUtc = null,
                    LastError = null,
                    UpdatedAtUtc = now,
                };
                try
                {
                    await _context.Db.Insertable(newLease).ExecuteCommandAsync();
                    return ScheduledTaskLeaseAcquireResult.CreateAcquired(newLease);
                }
                catch (Exception ex)
                {
                    guard.RecordException(ex);
                    _logger.LogWarning(ex, "Session guard 日期租约插入发生并发冲突: {TaskType} {ScopeKey}", normalizedTaskType, normalizedScopeKey);
                }
            }

            // 当前 guard 已证明原 sqlsess1 owner 的 SQL Session 已退出，才允许 CAS 接管标记行。
            // 没有前缀的旧 Running 租约仍完全遵循旧 TTL，不得在这里接管。
            var updatedRows = await _context.Db.Updateable<ScheduledTaskLease>()
                .SetColumns(x => x.Status == ScheduledTaskLeaseStatus.Running)
                .SetColumns(x => x.OwnerInstanceId == ownerInstanceId)
                .SetColumns(x => x.LeaseToken == leaseToken)
                .SetColumns(x => x.LeaseUntilUtc == sessionLeaseUntil)
                .SetColumns(x => x.StartedAtUtc == now)
                .SetColumns(x => x.CompletedAtUtc == null)
                .SetColumns(x => x.LastError == null)
                .SetColumns(x => x.UpdatedAtUtc == now)
                .Where(x =>
                    x.TaskType == normalizedTaskType
                    && x.ScopeKey == normalizedScopeKey
                    && (
                        x.Status != ScheduledTaskLeaseStatus.Running
                        || (x.LeaseToken != null
                            && x.LeaseToken.StartsWith(
                                SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix
                            ))
                        || ((x.LeaseToken == null
                                || !x.LeaseToken.StartsWith(
                                    SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix
                                ))
                            && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
                    )
                )
                .ExecuteCommandAsync();
            if (updatedRows > 0)
            {
                var lease = await QueryLeaseAsync(normalizedTaskType, normalizedScopeKey);
                return ScheduledTaskLeaseAcquireResult.CreateAcquired(lease!);
            }

            var runningLease = await QueryLeaseAsync(normalizedTaskType, normalizedScopeKey);
            await IncrementDuplicateSkipAsync(normalizedTaskType, normalizedScopeKey);
            return ScheduledTaskLeaseAcquireResult.CreateRunning(runningLease);
        }

        public async Task<bool> CompleteAsync(
            string taskType,
            string scopeKey,
            string leaseToken,
            bool success,
            string? errorMessage = null
        ) => await CompleteCoreAsync(taskType, scopeKey, leaseToken, success, errorMessage, null);

        internal async Task<bool> CompleteSessionGuardedAsync(
            string taskType,
            string scopeKey,
            string leaseToken,
            bool success,
            string? errorMessage,
            SalesStatisticsDateExecutionGuard guard
        ) => await CompleteCoreAsync(taskType, scopeKey, leaseToken, success, errorMessage, guard);

        private async Task<bool> CompleteCoreAsync(
            string taskType,
            string scopeKey,
            string leaseToken,
            bool success,
            string? errorMessage,
            SalesStatisticsDateExecutionGuard? guard
        )
        {
            var normalizedTaskType = NormalizeKey(taskType);
            var normalizedScopeKey = NormalizeKey(scopeKey);
            var ownerInstanceId = ResolveInstanceId();
            var now = DateTime.UtcNow;
            await EnsureSessionTokenGuardAsync(normalizedScopeKey, leaseToken, guard, "完成日期租约");

            var updatedRows = await _context.Db.Updateable<ScheduledTaskLease>()
                .SetColumns(x => x.Status == (success ? ScheduledTaskLeaseStatus.Success : ScheduledTaskLeaseStatus.Failed))
                .SetColumns(x => x.LeaseUntilUtc == null)
                .SetColumns(x => x.CompletedAtUtc == now)
                .SetColumns(x => x.LastError == errorMessage)
                .SetColumns(x => x.UpdatedAtUtc == now)
                .Where(x =>
                    x.TaskType == normalizedTaskType
                    && x.ScopeKey == normalizedScopeKey
                    && x.OwnerInstanceId == ownerInstanceId
                    && x.LeaseToken == leaseToken
                )
                .ExecuteCommandAsync();
            return updatedRows > 0;
        }

        public async Task<bool> RenewAsync(
            string taskType,
            string scopeKey,
            string leaseToken,
            TimeSpan leaseDuration
        ) => await RenewCoreAsync(taskType, scopeKey, leaseToken, leaseDuration, null);

        internal async Task<bool> RenewSessionGuardedAsync(
            string taskType,
            string scopeKey,
            string leaseToken,
            TimeSpan leaseDuration,
            SalesStatisticsDateExecutionGuard guard
        ) => await RenewCoreAsync(taskType, scopeKey, leaseToken, leaseDuration, guard);

        private async Task<bool> RenewCoreAsync(
            string taskType,
            string scopeKey,
            string leaseToken,
            TimeSpan leaseDuration,
            SalesStatisticsDateExecutionGuard? guard
        )
        {
            var normalizedTaskType = NormalizeKey(taskType);
            var normalizedScopeKey = NormalizeKey(scopeKey);
            var normalizedLeaseToken = NormalizeKey(leaseToken);
            var ownerInstanceId = ResolveInstanceId();
            var now = DateTime.UtcNow;
            var leaseUntil = now.Add(leaseDuration);

            if (normalizedLeaseToken.StartsWith(SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix, StringComparison.Ordinal))
            {
                await EnsureSessionTokenGuardAsync(normalizedScopeKey, normalizedLeaseToken, guard, "续租日期租约");
                var sessionLeaseUntil = SessionGuardLeaseUntilUtc;
                var sessionRenewRows = await _context.Db.Updateable<ScheduledTaskLease>()
                    .SetColumns(x => x.LeaseUntilUtc == sessionLeaseUntil)
                    .SetColumns(x => x.UpdatedAtUtc == now)
                    .Where(x =>
                        x.TaskType == normalizedTaskType
                        && x.ScopeKey == normalizedScopeKey
                        && x.Status == ScheduledTaskLeaseStatus.Running
                        && x.OwnerInstanceId == ownerInstanceId
                        && x.LeaseToken == normalizedLeaseToken
                        && x.LeaseUntilUtc == sessionLeaseUntil
                    )
                    .ExecuteCommandAsync();
                return sessionRenewRows > 0;
            }

            // 续租必须同时匹配 owner 和 fencing token，且旧租约还未过期；过期后不能被旧 worker 复活。
            var updatedRows = await _context.Db.Updateable<ScheduledTaskLease>()
                .SetColumns(x => x.LeaseUntilUtc == leaseUntil)
                .SetColumns(x => x.UpdatedAtUtc == now)
                .Where(x =>
                    x.TaskType == normalizedTaskType
                    && x.ScopeKey == normalizedScopeKey
                    && x.Status == ScheduledTaskLeaseStatus.Running
                    && x.OwnerInstanceId == ownerInstanceId
                    && x.LeaseToken == normalizedLeaseToken
                    && x.LeaseUntilUtc != null
                    && x.LeaseUntilUtc > now
                )
                .ExecuteCommandAsync();

            return updatedRows > 0;
        }

        public async Task EnsureActiveAsync(
            string taskType,
            string scopeKey,
            string leaseToken,
            TimeSpan leaseDuration,
            string stepName
        ) => await EnsureActiveCoreAsync(taskType, scopeKey, leaseToken, leaseDuration, stepName, null);

        internal async Task EnsureSessionGuardedActiveAsync(
            string taskType,
            string scopeKey,
            string leaseToken,
            TimeSpan leaseDuration,
            string stepName,
            SalesStatisticsDateExecutionGuard guard
        ) => await EnsureActiveCoreAsync(taskType, scopeKey, leaseToken, leaseDuration, stepName, guard);

        private async Task EnsureActiveCoreAsync(
            string taskType,
            string scopeKey,
            string leaseToken,
            TimeSpan leaseDuration,
            string stepName,
            SalesStatisticsDateExecutionGuard? guard
        )
        {
            if (await RenewCoreAsync(taskType, scopeKey, leaseToken, leaseDuration, guard))
            {
                return;
            }

            throw new InvalidOperationException(
                $"统计任务租约已失效，停止执行 {scopeKey} {stepName}"
            );
        }

        private async Task EnsureSessionTokenGuardAsync(
            string normalizedScopeKey,
            string leaseToken,
            SalesStatisticsDateExecutionGuard? guard,
            string operation
        )
        {
            if (!leaseToken.StartsWith(SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix, StringComparison.Ordinal))
                return;
            if (guard == null || !guard.IsBoundTo(_context.Db, normalizedScopeKey))
            {
                throw new SalesStatisticsDateExecutionGuardLostException(
                    $"{operation}缺少当前 SQL 会话执行权"
                );
            }
            await guard.EnsureActiveAsync(operation);
        }

        public async Task<int> GetRunningLeaseCountAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var leases = await _context.Db.Queryable<ScheduledTaskLease>()
                    .Where(x =>
                        x.Status == ScheduledTaskLeaseStatus.Running
                        && x.LeaseUntilUtc != null
                        && x.LeaseUntilUtc > now
                    )
                    .ToListAsync();
                return (await FilterActiveSessionGuardedLeasesAsync(leases)).Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取统计任务运行中租约数量失败");
                return 0;
            }
        }

        public async Task<int> GetRecentDuplicateSkipCountAsync(TimeSpan window)
        {
            try
            {
                var sinceUtc = DateTime.UtcNow.Subtract(window);
                var rows = await _context.Db.Queryable<ScheduledTaskLease>()
                    .Where(x => x.UpdatedAtUtc >= sinceUtc)
                    .Select(x => new { x.DuplicateSkipCount })
                    .ToListAsync();
                return rows.Sum(x => x.DuplicateSkipCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取统计任务重复跳过次数失败");
                return 0;
            }
        }

        public async Task<List<ScheduledTaskLease>> GetRunningLeasesAsync(
            string taskType,
            DateTime startDate,
            DateTime endDate
        )
        {
            var normalizedTaskType = NormalizeKey(taskType);
            var now = DateTime.UtcNow;
            var startKey = startDate.Date.ToString("yyyy-MM-dd");
            var endKey = endDate.Date.ToString("yyyy-MM-dd");

            var leases = await _context.Db.Queryable<ScheduledTaskLease>()
                .Where(x =>
                    x.TaskType == normalizedTaskType
                    && x.Status == ScheduledTaskLeaseStatus.Running
                    && x.LeaseUntilUtc != null
                    && x.LeaseUntilUtc > now
                )
                .ToListAsync();
            var scopedLeases = leases
                .Where(x =>
                    string.CompareOrdinal(x.ScopeKey, startKey) >= 0
                    && string.CompareOrdinal(x.ScopeKey, endKey) <= 0
                )
                .ToList();
            return await FilterActiveSessionGuardedLeasesAsync(scopedLeases);
        }

        /// <summary>
        /// sqlsess1/9999 行是 crash 后可由持锁 successor CAS 接管的 marker，不能仅凭 TTL
        /// 当作活跃任务。对它们以独立只读连接 APPLOCK_TEST 观察同日期 session 锁；旧 TTL
        /// 和非 SQL Server 保持原租约语义。探测失败时保守保留，避免监控故障误报可重入。
        /// </summary>
        private async Task<List<ScheduledTaskLease>> FilterActiveSessionGuardedLeasesAsync(
            List<ScheduledTaskLease> leases
        )
        {
            var sessionLeases = leases
                .Where(lease => lease.LeaseToken?.StartsWith(
                    SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix,
                    StringComparison.Ordinal) == true)
                .ToList();
            if (sessionLeases.Count == 0)
            {
                return leases;
            }

            HashSet<string>? activeScopeKeys;
            try
            {
                activeScopeKeys = await ProbeHeldSessionScopeKeysAsync(
                    sessionLeases.Select(lease => lease.ScopeKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "探测 SQL session 日期锁失败，保守保留运行中 sqlsess1 租约");
                return leases;
            }
            if (activeScopeKeys == null)
            {
                return leases;
            }

            return leases.Where(lease =>
                !sessionLeases.Contains(lease)
                || activeScopeKeys.Contains(lease.ScopeKey)
            ).ToList();
        }

        /// <summary>
        /// 以独立连接探测哪些日期的 session applock 仍被持有；返回 null 表示当前数据库不支持
        /// 探测（非 SQL Server），调用方须保留原租约语义。异常由调用方按"保守视为活跃"处理。
        /// </summary>
        internal virtual async Task<HashSet<string>?> ProbeHeldSessionScopeKeysAsync(
            IReadOnlyCollection<string> scopeKeys
        )
        {
            if (_context.Db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return null;
            }

            var activeScopeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var connection = new SqlConnection(
                _context.Db.CurrentConnectionConfig.ConnectionString
            );
            await connection.OpenAsync();
            foreach (var scopeKey in scopeKeys)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT CASE
                        WHEN APPLOCK_MODE(N'public', @resource, N'Session') = N'Exclusive' THEN 1
                        WHEN APPLOCK_TEST(N'public', @resource, N'Exclusive', N'Session') = 0 THEN 1
                        ELSE 0
                    END;
                    """;
                command.Parameters.AddWithValue(
                    "@resource",
                    SalesStatisticsDateExecutionGuard.GetLockResource(scopeKey)
                );
                if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 1)
                {
                    activeScopeKeys.Add(scopeKey);
                }
            }

            return activeScopeKeys;
        }

        private async Task<ScheduledTaskLease?> QueryLeaseAsync(string taskType, string scopeKey)
        {
            return await _context.Db.Queryable<ScheduledTaskLease>()
                .Where(x => x.TaskType == taskType && x.ScopeKey == scopeKey)
                .FirstAsync();
        }

        private async Task IncrementDuplicateSkipAsync(string taskType, string scopeKey)
        {
            var now = DateTime.UtcNow;
            await _context.Db.Updateable<ScheduledTaskLease>()
                .SetColumns(x => x.DuplicateSkipCount == x.DuplicateSkipCount + 1)
                .SetColumns(x => x.UpdatedAtUtc == now)
                .Where(x => x.TaskType == taskType && x.ScopeKey == scopeKey)
                .ExecuteCommandAsync();
        }

        private string ResolveInstanceId()
        {
            if (!string.IsNullOrWhiteSpace(_options.InstanceId))
            {
                return _options.InstanceId.Trim();
            }

            return $"{Environment.MachineName}-{Environment.ProcessId}";
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("任务租约键不能为空", nameof(value));
            }

            return value.Trim();
        }
    }

    public sealed class ScheduledTaskLeaseAcquireResult
    {
        public bool Acquired { get; init; }
        public bool IsRunning => !Acquired;
        public ScheduledTaskLease? Lease { get; init; }

        public static ScheduledTaskLeaseAcquireResult CreateAcquired(ScheduledTaskLease lease)
        {
            return new ScheduledTaskLeaseAcquireResult
            {
                Acquired = true,
                Lease = lease,
            };
        }

        public static ScheduledTaskLeaseAcquireResult CreateRunning(ScheduledTaskLease? lease)
        {
            return new ScheduledTaskLeaseAcquireResult
            {
                Acquired = false,
                Lease = lease,
            };
        }
    }
}
