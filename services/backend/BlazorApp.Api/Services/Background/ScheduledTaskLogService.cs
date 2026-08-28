using System.Text.Json;
using System.Collections.Concurrent;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;
using TaskStatus = BlazorApp.Shared.Models.HBweb.TaskStatus;
using TaskTrigger = BlazorApp.Shared.Models.HBweb.TaskTrigger;
using TaskType = BlazorApp.Shared.Models.HBweb.TaskType;

namespace BlazorApp.Api.Services.Background
{
    /// <summary>
    /// 定时任务日志服务
    /// 负责统一管理所有定时任务的日志记录，包括任务的开始、成功、失败状态记录，
    /// 以及任务的查询、统计和删除操作
    /// </summary>
    public class ScheduledTaskLogService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<ScheduledTaskLogService> _logger;
        private static readonly ConcurrentDictionary<Guid, (string ExternalRunId, int Attempt)> PerformanceRuns = new();

        public ScheduledTaskLogService(
            SqlSugarContext context,
            ILogger<ScheduledTaskLogService> logger
        )
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// 记录任务开始
        /// </summary>
        /// <param name="taskType">任务类型</param>
        /// <param name="parameters">任务参数</param>
        /// <param name="triggeredBy">触发方式（默认为定时触发）</param>
        /// <param name="canRetry">是否允许重试（默认为 true）</param>
        /// <param name="performanceExternalRunId">跨重试关联的性能运行标识</param>
        /// <param name="performanceAttempt">当前执行尝试序号</param>
        /// <returns>任务日志记录</returns>
        public async Task<ScheduledTaskLog> LogTaskStartAsync(
            string taskType,
            TaskParameters parameters,
            string triggeredBy = TaskTrigger.Scheduled,
            bool canRetry = true,
            string? performanceExternalRunId = null,
            int performanceAttempt = 1
        )
        {
            ScheduledTaskLog taskLog;
            try
            {
                taskLog = new ScheduledTaskLog
                {
                    TaskType = taskType,
                    TaskParameters = JsonSerializer.Serialize(parameters),
                    Status = TaskStatus.Running,
                    StartedAt = DateTime.UtcNow,
                    ScheduledTime = DateTime.UtcNow,
                    TriggeredBy = triggeredBy,
                    CanRetry = canRetry,
                    ErrorMessage = string.Empty,
                };

                // 插入任务日志并返回实体
                await _context.ScheduledTaskLogDb.InsertReturnEntityAsync(taskLog);

                _logger.LogInformation(
                    "任务开始: {TaskType}, TaskId: {TaskId}, 参数: {@Parameters}",
                    taskType,
                    taskLog.Id,
                    parameters
                );

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录任务开始时发生异常（数据库不可用），创建临时日志: {TaskType}", taskType);
                taskLog = new ScheduledTaskLog
                {
                    Id = Guid.NewGuid(),
                    TaskType = taskType,
                    TaskParameters = JsonSerializer.Serialize(parameters),
                    Status = TaskStatus.Running,
                    StartedAt = DateTime.UtcNow,
                    ScheduledTime = DateTime.UtcNow,
                    TriggeredBy = triggeredBy,
                    CanRetry = canRetry,
                    ErrorMessage = string.Empty,
                };
            }

            PublishTaskStartedAfterCommit(
                taskLog,
                performanceExternalRunId,
                performanceAttempt
            );
            return taskLog;
        }

        /// <summary>
        /// 严格插入运行中任务日志。调用方可预生成任务 ID，并把本方法放进自己的数据库事务。
        /// 本方法不发布遥测；只有事务提交成功后才能调用 <see cref="PublishTaskStartedAfterCommit"/>。
        /// </summary>
        public async Task<ScheduledTaskLog> LogTaskStartStrictAsync(
            Guid taskId,
            string taskType,
            TaskParameters parameters,
            string triggeredBy = TaskTrigger.Scheduled,
            bool canRetry = true,
            DateTime? startedAtUtc = null
        )
        {
            if (taskId == Guid.Empty)
            {
                throw new ArgumentException("严格任务日志必须使用非空任务 ID", nameof(taskId));
            }

            var startedAt = startedAtUtc ?? DateTime.UtcNow;
            var taskLog = new ScheduledTaskLog
            {
                Id = taskId,
                TaskType = taskType,
                TaskParameters = JsonSerializer.Serialize(parameters),
                Status = TaskStatus.Running,
                StartedAt = startedAt,
                ScheduledTime = startedAt,
                TriggeredBy = triggeredBy,
                CanRetry = canRetry,
                ErrorMessage = string.Empty,
            };

            var inserted = await _context.Db.Insertable(taskLog).ExecuteCommandAsync();
            if (inserted != 1)
            {
                throw new InvalidOperationException($"任务日志未严格插入: {taskId}");
            }

            return taskLog;
        }

        /// <summary>
        /// 在包含任务日志的业务事务提交后发布 started 遥测，避免回滚任务污染运行基线。
        /// </summary>
        public void PublishTaskStartedAfterCommit(
            ScheduledTaskLog taskLog,
            string? performanceExternalRunId = null,
            int performanceAttempt = 1
        )
        {
            var externalRunId = string.IsNullOrWhiteSpace(performanceExternalRunId)
                ? taskLog.Id.ToString("N")
                : performanceExternalRunId.Trim();
            var attempt = Math.Max(1, performanceAttempt);
            PerformanceRuns[taskLog.Id] = (externalRunId, attempt);
            PerformanceOperationalRunBridge.Publish(
                PerformanceOperationalRunTransition.Queued(
                    externalRunId,
                    "background",
                    taskLog.TaskType,
                    taskLog.ScheduledTime,
                    attempt: attempt
                )
            );
            PerformanceOperationalRunBridge.Publish(
                PerformanceOperationalRunTransition.Started(
                    externalRunId,
                    "background",
                    taskLog.TaskType,
                    taskLog.StartedAt,
                    attempt: attempt
                )
            );
        }

        /// <summary>
        /// 记录任务成功完成
        /// </summary>
        /// <param name="taskId">任务ID</param>
        public async Task LogTaskSuccessAsync(Guid taskId)
        {
            try
            {
                var taskLog = await _context.ScheduledTaskLogDb.GetByIdAsync(taskId);
                if (taskLog == null)
                {
                    _logger.LogWarning("任务日志不存在: {TaskId}", taskId);
                    return;
                }

                taskLog.Status = TaskStatus.Success;
                taskLog.CompletedAt = DateTime.UtcNow;
                taskLog.DurationMs = (int)(
                    (taskLog.CompletedAt.Value - taskLog.StartedAt).TotalMilliseconds
                );

                // 更新任务状态为成功
                var updated = await _context.ScheduledTaskLogDb.UpdateAsync(taskLog);
                if (!updated)
                {
                    // 权威任务日志没有终态时不得污染成功率；保留映射，由运行租约恢复为 interrupted。
                    _logger.LogWarning(
                        "任务成功状态未持久化，不发布性能完成事件: {TaskId}",
                        taskId
                    );
                    return;
                }
                PublishCompletion(taskLog, "success");

                _logger.LogInformation(
                    "任务成功完成: {TaskType}, TaskId: {TaskId}, 耗时: {Duration}ms",
                    taskLog.TaskType,
                    taskLog.Id,
                    taskLog.DurationMs
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录任务成功状态时发生异常: {TaskId}", taskId);
            }
        }

        /// <summary>
        /// 严格记录任务成功；任何持久化失败都向调用方抛出，供跨实例缓存版本依赖的统计任务使用。
        /// </summary>
        public async Task LogTaskSuccessStrictAsync(Guid taskId)
        {
            var taskLog = await _context.ScheduledTaskLogDb.GetByIdAsync(taskId);
            if (taskLog == null)
            {
                throw new InvalidOperationException($"任务日志不存在，无法确认成功版本: {taskId}");
            }

            taskLog.Status = TaskStatus.Success;
            taskLog.CompletedAt = DateTime.UtcNow;
            taskLog.DurationMs = (int)(
                (taskLog.CompletedAt.Value - taskLog.StartedAt).TotalMilliseconds
            );

            var updated = await _context.ScheduledTaskLogDb.UpdateAsync(taskLog);
            if (!updated)
            {
                throw new InvalidOperationException($"任务成功状态未持久化: {taskId}");
            }

            PublishCompletion(taskLog, "success");

            _logger.LogInformation(
                "任务成功完成并严格持久化: {TaskType}, TaskId: {TaskId}, 耗时: {Duration}ms",
                taskLog.TaskType,
                taskLog.Id,
                taskLog.DurationMs
            );
        }

        /// <summary>
        /// 严格记录统计任务失败，防止持久化异常被吞掉后长期残留 Running 状态。
        /// </summary>
        public async Task LogTaskFailureStrictAsync(Guid taskId, string errorMessage)
        {
            var taskLog = await _context.ScheduledTaskLogDb.GetByIdAsync(taskId);
            if (taskLog == null)
            {
                throw new InvalidOperationException($"任务日志不存在，无法严格标记失败: {taskId}");
            }

            taskLog.Status = TaskStatus.Failed;
            taskLog.CompletedAt = DateTime.UtcNow;
            taskLog.DurationMs = (int)(
                (taskLog.CompletedAt.Value - taskLog.StartedAt).TotalMilliseconds
            );
            taskLog.ErrorMessage = errorMessage;
            taskLog.CanRetry = true;
            taskLog.RetryCount++;

            var updated = await _context.ScheduledTaskLogDb.UpdateAsync(taskLog);
            if (!updated)
            {
                throw new InvalidOperationException($"任务失败状态未持久化: {taskId}");
            }

            PublishCompletion(taskLog, "failure");

            _logger.LogError(
                "任务失败状态已严格持久化: {TaskType}, TaskId: {TaskId}, 错误: {Error}",
                taskLog.TaskType,
                taskLog.Id,
                errorMessage
            );
        }

        /// <summary>
        /// 商品每日持久队列专用的原子终态写入。只有仍为 Running 的同一日志能完成，
        /// 其他实例已先行终结时返回 false，且不得重复发布 completion 遥测。
        /// </summary>
        public async Task<bool> TryCompleteProductStoreDailyTaskAsync(
            Guid taskId,
            bool success,
            string? errorMessage = null
        )
        {
            var taskLog = await _context.Db.Queryable<ScheduledTaskLog>()
                .Where(item => item.Id == taskId)
                .FirstAsync();
            if (taskLog == null)
            {
                throw new InvalidOperationException($"任务日志不存在，无法原子终结: {taskId}");
            }
            if (taskLog.Status != TaskStatus.Running)
            {
                return false;
            }

            var completedAt = DateTime.UtcNow;
            var durationMs = (int)(completedAt - taskLog.StartedAt).TotalMilliseconds;
            int updated;
            if (success)
            {
                updated = await _context.Db.Updateable<ScheduledTaskLog>()
                    .SetColumns(item => item.Status == TaskStatus.Success)
                    .SetColumns(item => item.CompletedAt == completedAt)
                    .SetColumns(item => item.DurationMs == durationMs)
                    .SetColumns(item => item.ErrorMessage == null)
                    .Where(item => item.Id == taskId && item.Status == TaskStatus.Running)
                    .ExecuteCommandAsync();
            }
            else
            {
                var normalizedError = string.IsNullOrWhiteSpace(errorMessage)
                    ? "未知错误"
                    : errorMessage.Trim();
                updated = await _context.Db.Updateable<ScheduledTaskLog>()
                    .SetColumns(item => item.Status == TaskStatus.Failed)
                    .SetColumns(item => item.CompletedAt == completedAt)
                    .SetColumns(item => item.DurationMs == durationMs)
                    .SetColumns(item => item.ErrorMessage == normalizedError)
                    .SetColumns(item => item.CanRetry == false)
                    .Where(item => item.Id == taskId && item.Status == TaskStatus.Running)
                    .ExecuteCommandAsync();
                taskLog.ErrorMessage = normalizedError;
                taskLog.CanRetry = false;
            }

            if (updated > 1)
            {
                throw new InvalidOperationException($"任务日志原子终结影响了异常行数: {taskId}");
            }
            if (updated == 0)
            {
                return false;
            }

            taskLog.Status = success ? TaskStatus.Success : TaskStatus.Failed;
            taskLog.CompletedAt = completedAt;
            taskLog.DurationMs = durationMs;
            PublishCompletion(taskLog, success ? "success" : "failure");
            return true;
        }

        /// <summary>
        /// 记录任务执行失败
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="errorMessage">错误信息</param>
        /// <param name="canRetry">是否允许重试（默认为 true）</param>
        public async Task LogTaskFailureAsync(
            Guid taskId,
            string errorMessage,
            bool canRetry = true
        )
        {
            try
            {
                var taskLog = await _context.ScheduledTaskLogDb.GetByIdAsync(taskId);
                if (taskLog == null)
                {
                    _logger.LogWarning("任务日志不存在: {TaskId}", taskId);
                    return;
                }

                taskLog.Status = TaskStatus.Failed;
                taskLog.CompletedAt = DateTime.UtcNow;
                taskLog.DurationMs = (int)(
                    (taskLog.CompletedAt.Value - taskLog.StartedAt).TotalMilliseconds
                );
                taskLog.ErrorMessage = errorMessage;
                taskLog.CanRetry = canRetry;
                taskLog.RetryCount++;

                // 更新任务状态为失败，并记录错误信息
                var updated = await _context.ScheduledTaskLogDb.UpdateAsync(taskLog);
                if (!updated)
                {
                    // 与成功路径保持同一权威顺序，避免不存在的失败终态进入冻结基线。
                    _logger.LogWarning(
                        "任务失败状态未持久化，不发布性能完成事件: {TaskId}",
                        taskId
                    );
                    return;
                }
                PublishCompletion(taskLog, "failure");

                _logger.LogError(
                    "任务执行失败: {TaskType}, TaskId: {TaskId}, 耗时: {Duration}ms, 错误: {Error}",
                    taskLog.TaskType,
                    taskLog.Id,
                    taskLog.DurationMs,
                    errorMessage
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录任务失败状态时发生异常: {TaskId}", taskId);
            }
        }

        private static void PublishCompletion(ScheduledTaskLog taskLog, string status)
        {
            var performanceRun = PerformanceRuns.TryRemove(taskLog.Id, out var mapped)
                ? mapped
                : (taskLog.Id.ToString("N"), Math.Max(1, taskLog.RetryCount));
            PerformanceOperationalRunBridge.Publish(
                PerformanceOperationalRunTransition.Completed(
                    performanceRun.Item1,
                    "background",
                    taskLog.TaskType,
                    status,
                    taskLog.CompletedAt ?? DateTime.UtcNow,
                    performanceRun.Item2
                )
            );
        }

        /// <summary>
        /// 获取可重试的失败任务列表
        /// </summary>
        /// <param name="taskType">任务类型（可选）</param>
        /// <param name="startDate">开始日期（可选）</param>
        /// <param name="endDate">结束日期（可选）</param>
        /// <param name="pageSize">每页数量（默认 100）</param>
        /// <param name="pageNumber">页码（默认 1）</param>
        /// <returns>失败任务列表</returns>
        public async Task<List<ScheduledTaskLog>> GetFailedTasksAsync(
            string? taskType = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int pageSize = 100,
            int pageNumber = 1
        )
        {
            try
            {
                var query = _context
                    .Db.Queryable<ScheduledTaskLog>()
                    .Where(t => t.Status == TaskStatus.Failed && t.CanRetry);

                if (!string.IsNullOrEmpty(taskType))
                {
                    query = query.Where(t => t.TaskType == taskType);
                }

                if (startDate.HasValue)
                {
                    query = query.Where(t => t.StartedAt >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(t => t.StartedAt <= endDate.Value);
                }

                // 分页获取失败任务
                var tasks = await query
                    .OrderByDescending(t => t.StartedAt)
                    .ToPageListAsync(pageNumber, pageSize);

                return tasks;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取失败任务列表时发生异常");
                return new List<ScheduledTaskLog>();
            }
        }

        /// <summary>
        /// 获取指定日期范围内的任务列表
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="taskType">任务类型（可选）</param>
        /// <returns>任务列表</returns>
        public async Task<List<ScheduledTaskLog>> GetTasksByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            string? taskType = null
        )
        {
            try
            {
                var query = _context
                    .Db.Queryable<ScheduledTaskLog>()
                    .Where(t => t.StartedAt >= startDate && t.StartedAt <= endDate);

                if (!string.IsNullOrEmpty(taskType))
                {
                    query = query.Where(t => t.TaskType == taskType);
                }

                // 按开始时间倒序返回
                return await query.OrderByDescending(t => t.StartedAt).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取指定日期范围任务列表时发生异常");
                return new List<ScheduledTaskLog>();
            }
        }

        /// <summary>
        /// 获取指定任务
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>任务日志记录，不存在则返回 null</returns>
        public async Task<ScheduledTaskLog?> GetTaskAsync(Guid taskId)
        {
            try
            {
                return await _context.ScheduledTaskLogDb.GetByIdAsync(taskId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取指定任务时发生异常: {TaskId}", taskId);
                return null;
            }
        }

        /// <summary>
        /// 获取任务统计数据
        /// </summary>
        /// <param name="date">统计日期（可选，如果不传则统计所有）</param>
        /// <returns>包含总数、成功、失败、运行中的统计字典</returns>
        public async Task<Dictionary<string, int>> GetTaskStatisticsAsync(DateTime? date = null)
        {
            try
            {
                var query = _context.Db.Queryable<ScheduledTaskLog>();

                if (date.HasValue)
                {
                    var targetDate = date.Value.Date;
                    var nextDay = targetDate.AddDays(1);
                    query = query.Where(t => t.StartedAt >= targetDate && t.StartedAt < nextDay);
                }

                // 查询任务列表（为了性能，这里最好直接用 GroupBy 或 Count，但为了保持现有逻辑简单，先这样写）
                // 优化：直接使用 Count 查询而不是拉取所有数据
                var total = await query.CountAsync();
                var success = await query
                    .Clone()
                    .Where(t => t.Status == TaskStatus.Success)
                    .CountAsync();
                var failed = await query
                    .Clone()
                    .Where(t => t.Status == TaskStatus.Failed)
                    .CountAsync();
                var running = await query
                    .Clone()
                    .Where(t => t.Status == TaskStatus.Running)
                    .CountAsync();

                // 统计各状态数量
                var statistics = new Dictionary<string, int>
                {
                    ["Total"] = total,
                    ["Success"] = success,
                    ["Failed"] = failed,
                    ["Running"] = running,
                };

                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取任务统计数据时发生异常");
                return new Dictionary<string, int>
                {
                    ["Total"] = 0,
                    ["Success"] = 0,
                    ["Failed"] = 0,
                    ["Running"] = 0,
                };
            }
        }

        /// <summary>
        /// 分页获取任务列表
        /// </summary>
        /// <param name="taskType">任务类型</param>
        /// <param name="status">状态</param>
        /// <param name="triggeredBy">触发方式</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="pageIndex">页码</param>
        /// <param name="pageSize">每页大小</param>
        /// <param name="sortBy">排序字段</param>
        /// <param name="sortDirection">排序方向 (asc/desc)</param>
        /// <returns>分页结果</returns>
        public async Task<PagedResult<ScheduledTaskLog>> GetPagedTasksAsync(
            string? taskType,
            string? status,
            string? triggeredBy,
            DateTime? startDate,
            DateTime? endDate,
            int pageIndex,
            int pageSize,
            string? sortBy,
            string? sortDirection
        )
        {
            try
            {
                var query = _context.Db.Queryable<ScheduledTaskLog>();

                if (!string.IsNullOrEmpty(taskType))
                {
                    query = query.Where(t => t.TaskType == taskType);
                }

                if (!string.IsNullOrEmpty(status))
                {
                    query = query.Where(t => t.Status == status);
                }

                if (!string.IsNullOrEmpty(triggeredBy))
                {
                    query = query.Where(t => t.TriggeredBy == triggeredBy);
                }

                if (startDate.HasValue)
                {
                    query = query.Where(t => t.StartedAt >= startDate.Value);
                }

                if (endDate.HasValue)
                {
                    query = query.Where(t => t.StartedAt <= endDate.Value);
                }

                // 排序
                if (!string.IsNullOrEmpty(sortBy))
                {
                    var isAsc = sortDirection?.ToLower() == "asc";
                    switch (sortBy.ToLower())
                    {
                        case "startedat":
                            query = isAsc
                                ? query.OrderBy(t => t.StartedAt)
                                : query.OrderByDescending(t => t.StartedAt);
                            break;
                        case "durationms":
                            query = isAsc
                                ? query.OrderBy(t => t.DurationMs)
                                : query.OrderByDescending(t => t.DurationMs);
                            break;
                        case "retrycount":
                            query = isAsc
                                ? query.OrderBy(t => t.RetryCount)
                                : query.OrderByDescending(t => t.RetryCount);
                            break;
                        default:
                            query = query.OrderByDescending(t => t.StartedAt);
                            break;
                    }
                }
                else
                {
                    query = query.OrderByDescending(t => t.StartedAt);
                }

                RefAsync<int> total = 0;
                var items = await query.ToPageListAsync(pageIndex, pageSize, total);

                return new PagedResult<ScheduledTaskLog>
                {
                    Items = items,
                    Total = total,
                    Page = pageIndex,
                    PageSize = pageSize,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分页任务列表时发生异常");
                return new PagedResult<ScheduledTaskLog>
                {
                    Items = new List<ScheduledTaskLog>(),
                    Total = 0,
                    Page = pageIndex,
                    PageSize = pageSize,
                };
            }
        }

        /// <summary>
        /// 获取最近的任务记录
        /// </summary>
        /// <param name="count">获取数量（默认 50）</param>
        /// <param name="taskType">任务类型（可选）</param>
        /// <returns>任务列表</returns>
        public async Task<List<ScheduledTaskLog>> GetRecentTasksAsync(
            int count = 50,
            string? taskType = null
        )
        {
            try
            {
                var query = _context.Db.Queryable<ScheduledTaskLog>();

                if (!string.IsNullOrEmpty(taskType))
                {
                    query = query.Where(t => t.TaskType == taskType);
                }

                // 获取最近的 N 条记录
                return await query.OrderByDescending(t => t.StartedAt).Take(count).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最近任务记录时发生异常");
                return new List<ScheduledTaskLog>();
            }
        }

        /// <summary>
        /// 删除指定任务
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>是否删除成功</returns>
        public async Task<bool> DeleteTaskAsync(Guid taskId)
        {
            try
            {
                return await _context.ScheduledTaskLogDb.DeleteByIdAsync(taskId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除指定任务时发生异常: {TaskId}", taskId);
                return false;
            }
        }

        /// <summary>
        /// 批量删除指定日期之前的旧任务
        /// </summary>
        /// <param name="beforeDate">截止日期</param>
        /// <returns>删除的任务数量</returns>
        public async Task<int> DeleteOldTasksAsync(DateTime beforeDate)
        {
            try
            {
                var tasksToDelete = await _context
                    .Db.Queryable<ScheduledTaskLog>()
                    .Where(t => t.StartedAt < beforeDate)
                    .ToListAsync();

                if (tasksToDelete.Any())
                {
                    var ids = tasksToDelete.Select(t => t.Id).ToList();
                    // 批量删除
                    return await _context
                        .Db.Deleteable<ScheduledTaskLog>()
                        .Where(t => ids.Contains(t.Id))
                        .ExecuteCommandAsync();
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除旧任务时发生异常");
                return 0;
            }
        }
    }
}
