using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.Background
{
    /// <summary>
    /// 管理定时任务运行时开关，支持在后台把调度权切换到指定 API 实例。
    /// </summary>
    public class ScheduledTaskRuntimeControlService
    {
        private readonly SqlSugarContext _context;
        private readonly ScheduledTaskOptions _options;
        private readonly ILogger<ScheduledTaskRuntimeControlService> _logger;

        public ScheduledTaskRuntimeControlService(
            SqlSugarContext context,
            IOptions<ScheduledTaskOptions> options,
            ILogger<ScheduledTaskRuntimeControlService> logger
        )
        {
            _context = context;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<bool> IsCurrentInstanceSchedulerEnabledAsync()
        {
            await TouchCurrentInstanceAsync();
            if (!_options.Enabled)
            {
                return false;
            }

            var control = await GetOrCreateControlAsync();
            var instances = await QueryInstancesAsync();
            var leaseSnapshot = await QueryLeaseSnapshotAsync();
            await PromoteCurrentInstanceWhenActiveIsStaleAsync(control, instances);
            var status = BuildStatus(control, instances, leaseSnapshot);
            return status.EffectiveSchedulerEnabled;
        }

        public async Task<ScheduledTaskRuntimeControlStatusDto> GetStatusAsync()
        {
            var control = await QueryControlAsync();
            var instances = await QueryInstancesAsync();
            var leaseSnapshot = await QueryLeaseSnapshotAsync();

            return BuildStatus(control, instances, leaseSnapshot);
        }

        public async Task<ScheduledTaskRuntimeControlStatusDto> UpdateControlAsync(
            ScheduledTaskRuntimeControlUpdateDto request,
            string? updatedBy
        )
        {
            await TouchCurrentInstanceAsync();
            var control = await GetOrCreateControlAsync();

            control.SchedulerEnabled = request.SchedulerEnabled;
            control.ActiveInstanceId = string.IsNullOrWhiteSpace(request.ActiveInstanceId)
                ? null
                : request.ActiveInstanceId.Trim();
            control.UpdatedAtUtc = DateTime.UtcNow;
            control.UpdatedByUser = updatedBy;

            await _context.Db.Updateable(control).ExecuteCommandAsync();

            _logger.LogInformation(
                "定时任务运行时控制已更新: Enabled={Enabled}, ActiveInstanceId={ActiveInstanceId}, UpdatedBy={UpdatedBy}",
                control.SchedulerEnabled,
                control.ActiveInstanceId,
                updatedBy
            );

            var instances = await QueryInstancesAsync();
            var leaseSnapshot = await QueryLeaseSnapshotAsync();
            return BuildStatus(control, instances, leaseSnapshot);
        }

        public string GetCurrentInstanceId()
        {
            return ResolveInstanceId();
        }

        private async Task<ScheduledTaskRuntimeControl?> QueryControlAsync()
        {
            return await _context.Db.Queryable<ScheduledTaskRuntimeControl>()
                .Where(x => x.Id == ScheduledTaskRuntimeControl.DefaultId)
                .FirstAsync();
        }

        private Task<List<ScheduledTaskInstanceState>> QueryInstancesAsync()
        {
            return _context.Db.Queryable<ScheduledTaskInstanceState>()
                .OrderByDescending(x => x.LastSeenAtUtc)
                .ToListAsync();
        }

        private async Task TouchCurrentInstanceAsync()
        {
            var instanceId = ResolveInstanceId();
            var now = DateTime.UtcNow;
            var existing = await _context.Db.Queryable<ScheduledTaskInstanceState>()
                .Where(x => x.InstanceId == instanceId)
                .FirstAsync();

            if (existing == null)
            {
                var newState = new ScheduledTaskInstanceState
                {
                    InstanceId = instanceId,
                    HostName = Environment.MachineName,
                    ProcessId = Environment.ProcessId,
                    SchedulerEnabledByConfig = _options.Enabled,
                    LastSeenAtUtc = now,
                };

                try
                {
                    await _context.Db.Insertable(newState).ExecuteCommandAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "定时任务实例心跳初始化发生并发冲突，改为更新当前实例: {InstanceId}", instanceId);
                    await UpdateCurrentInstanceStateAsync(instanceId, now);
                }
                return;
            }

            existing.HostName = Environment.MachineName;
            existing.ProcessId = Environment.ProcessId;
            existing.SchedulerEnabledByConfig = _options.Enabled;
            existing.LastSeenAtUtc = now;
            await _context.Db.Updateable(existing).ExecuteCommandAsync();
        }

        private async Task<ScheduledTaskRuntimeControl> GetOrCreateControlAsync()
        {
            var control = await QueryControlAsync();
            if (control != null)
            {
                return control;
            }

            control = new ScheduledTaskRuntimeControl
            {
                Id = ScheduledTaskRuntimeControl.DefaultId,
                SchedulerEnabled = true,
                ActiveInstanceId = ResolveInstanceId(),
                UpdatedAtUtc = DateTime.UtcNow,
            };
            try
            {
                await _context.Db.Insertable(control).ExecuteCommandAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "定时任务运行时控制初始化发生并发冲突，改为重新查询控制记录");
                control = await QueryControlAsync();
                if (control == null)
                {
                    throw;
                }
            }

            return control;
        }

        private async Task UpdateCurrentInstanceStateAsync(string instanceId, DateTime now)
        {
            var existing = await _context.Db.Queryable<ScheduledTaskInstanceState>()
                .Where(x => x.InstanceId == instanceId)
                .FirstAsync();
            if (existing == null)
            {
                throw new InvalidOperationException($"无法更新定时任务实例心跳，实例不存在: {instanceId}");
            }

            existing.HostName = Environment.MachineName;
            existing.ProcessId = Environment.ProcessId;
            existing.SchedulerEnabledByConfig = _options.Enabled;
            existing.LastSeenAtUtc = now;
            await _context.Db.Updateable(existing).ExecuteCommandAsync();
        }

        private async Task PromoteCurrentInstanceWhenActiveIsStaleAsync(
            ScheduledTaskRuntimeControl control,
            List<ScheduledTaskInstanceState> instances
        )
        {
            if (!control.SchedulerEnabled || string.IsNullOrWhiteSpace(control.ActiveInstanceId))
            {
                return;
            }

            var currentInstanceId = ResolveInstanceId();
            if (string.Equals(control.ActiveInstanceId, currentInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var now = DateTime.UtcNow;
            var staleThreshold = TimeSpan.FromMinutes(Math.Max(30, _options.HourlyTaskIntervalMinutes * 3));
            var activeInstance = instances.FirstOrDefault(instance =>
                string.Equals(instance.InstanceId, control.ActiveInstanceId, StringComparison.OrdinalIgnoreCase)
            );
            if (activeInstance == null || activeInstance.LastSeenAtUtc >= now.Subtract(staleThreshold))
            {
                return;
            }

            // 旧容器被替换后 activeInstanceId 可能长期停在死实例；当前实例接管，避免统计任务永久跳过。
            var previousActiveInstanceId = control.ActiveInstanceId;
            control.ActiveInstanceId = currentInstanceId;
            control.UpdatedAtUtc = now;
            control.UpdatedByUser = "auto-stale-failover";
            await _context.Db.Updateable(control).ExecuteCommandAsync();
            _logger.LogWarning(
                "定时任务调度实例已自动接管: PreviousActive={PreviousActive}, Current={Current}, ThresholdMinutes={ThresholdMinutes}",
                previousActiveInstanceId,
                currentInstanceId,
                staleThreshold.TotalMinutes
            );
        }

        private async Task<ScheduledTaskLeaseSnapshot> QueryLeaseSnapshotAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var recentSince = now.AddHours(-24);
                var rows = await _context.Db.Queryable<ScheduledTaskLease>()
                    .Where(x =>
                        (x.Status == ScheduledTaskLeaseStatus.Running && x.LeaseUntilUtc != null && x.LeaseUntilUtc > now)
                        || x.UpdatedAtUtc >= recentSince
                    )
                    .Select(x => new
                    {
                        x.Status,
                        x.LeaseUntilUtc,
                        x.DuplicateSkipCount,
                        x.UpdatedAtUtc,
                    })
                    .ToListAsync();

                return new ScheduledTaskLeaseSnapshot(
                    rows.Count(x =>
                        x.Status == ScheduledTaskLeaseStatus.Running
                        && x.LeaseUntilUtc.HasValue
                        && x.LeaseUntilUtc.Value > now
                    ),
                    rows
                        .Where(x => x.UpdatedAtUtc >= recentSince)
                        .Sum(x => x.DuplicateSkipCount)
                );
            }
            catch (Exception ex)
            {
                // 租约表是新增控制面数据，读取失败不能影响已有调度开关判断。
                _logger.LogWarning(ex, "读取统计任务租约状态失败，运行控制状态将显示为 0");
                return new ScheduledTaskLeaseSnapshot(0, 0);
            }
        }

        private ScheduledTaskRuntimeControlStatusDto BuildStatus(
            ScheduledTaskRuntimeControl? control,
            List<ScheduledTaskInstanceState> instances,
            ScheduledTaskLeaseSnapshot leaseSnapshot
        )
        {
            var currentInstanceId = ResolveInstanceId();
            var schedulerEnabled = control?.SchedulerEnabled ?? false;
            var activeInstanceId = string.IsNullOrWhiteSpace(control?.ActiveInstanceId)
                ? null
                : control.ActiveInstanceId.Trim();
            var effectiveEnabled =
                _options.Enabled
                && schedulerEnabled
                && activeInstanceId != null
                && string.Equals(activeInstanceId, currentInstanceId, StringComparison.OrdinalIgnoreCase);

            return new ScheduledTaskRuntimeControlStatusDto
            {
                SchedulerEnabled = schedulerEnabled,
                SchedulerEnabledByConfig = _options.Enabled,
                EffectiveSchedulerEnabled = effectiveEnabled,
                CurrentInstanceId = currentInstanceId,
                ActiveInstanceId = activeInstanceId,
                UpdatedAtUtc = control?.UpdatedAtUtc ?? default,
                UpdatedBy = control?.UpdatedByUser,
                RunningLeaseCount = leaseSnapshot.RunningLeaseCount,
                RecentDuplicateSkipCount = leaseSnapshot.RecentDuplicateSkipCount,
                KnownInstances = instances
                    .Select(instance => new ScheduledTaskInstanceStateDto
                    {
                        InstanceId = instance.InstanceId,
                        HostName = instance.HostName,
                        ProcessId = instance.ProcessId,
                        SchedulerEnabledByConfig = instance.SchedulerEnabledByConfig,
                        LastSeenAtUtc = instance.LastSeenAtUtc,
                        IsCurrent = string.Equals(
                            instance.InstanceId,
                            currentInstanceId,
                            StringComparison.OrdinalIgnoreCase
                        ),
                        IsActive = string.Equals(
                            instance.InstanceId,
                            activeInstanceId,
                            StringComparison.OrdinalIgnoreCase
                        ),
                    })
                    .ToList(),
            };
        }

        private string ResolveInstanceId()
        {
            if (!string.IsNullOrWhiteSpace(_options.InstanceId))
            {
                return _options.InstanceId.Trim();
            }

            return $"{Environment.MachineName}-{Environment.ProcessId}";
        }

        private sealed record ScheduledTaskLeaseSnapshot(
            int RunningLeaseCount,
            int RecentDuplicateSkipCount
        );
    }
}
