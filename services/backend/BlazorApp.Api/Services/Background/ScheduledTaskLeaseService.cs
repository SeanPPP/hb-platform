using BlazorApp.Api.Data;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Background
{
    /// <summary>
    /// 基于数据库的任务租约服务，避免多实例或手动重复触发同一范围统计。
    /// </summary>
    public class ScheduledTaskLeaseService
    {
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
                        || x.LeaseUntilUtc == null
                        || x.LeaseUntilUtc <= now
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
        )
        {
            var normalizedTaskType = NormalizeKey(taskType);
            var normalizedScopeKey = NormalizeKey(scopeKey);
            var ownerInstanceId = ResolveInstanceId();
            var now = DateTime.UtcNow;

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
        )
        {
            var normalizedTaskType = NormalizeKey(taskType);
            var normalizedScopeKey = NormalizeKey(scopeKey);
            var normalizedLeaseToken = NormalizeKey(leaseToken);
            var ownerInstanceId = ResolveInstanceId();
            var now = DateTime.UtcNow;
            var leaseUntil = now.Add(leaseDuration);

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
        )
        {
            if (await RenewAsync(taskType, scopeKey, leaseToken, leaseDuration))
            {
                return;
            }

            throw new InvalidOperationException(
                $"统计任务租约已失效，停止执行 {scopeKey} {stepName}"
            );
        }

        public async Task<int> GetRunningLeaseCountAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                return await _context.Db.Queryable<ScheduledTaskLease>()
                    .Where(x =>
                        x.Status == ScheduledTaskLeaseStatus.Running
                        && x.LeaseUntilUtc != null
                        && x.LeaseUntilUtc > now
                    )
                    .CountAsync();
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
            return leases
                .Where(x =>
                    string.CompareOrdinal(x.ScopeKey, startKey) >= 0
                    && string.CompareOrdinal(x.ScopeKey, endKey) <= 0
                )
                .ToList();
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
