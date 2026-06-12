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
            var status = BuildStatus(control, instances);
            return status.EffectiveSchedulerEnabled;
        }

        public async Task<ScheduledTaskRuntimeControlStatusDto> GetStatusAsync()
        {
            var control = await QueryControlAsync();
            var instances = await QueryInstancesAsync();

            return BuildStatus(control, instances);
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
            return BuildStatus(control, instances);
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

        private ScheduledTaskRuntimeControlStatusDto BuildStatus(
            ScheduledTaskRuntimeControl? control,
            List<ScheduledTaskInstanceState> instances
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
    }
}
