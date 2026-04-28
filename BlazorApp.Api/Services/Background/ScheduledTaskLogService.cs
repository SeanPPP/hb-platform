using System.Text.Json;
using BlazorApp.Api.Data;
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
        /// <returns>任务日志记录</returns>
        public async Task<ScheduledTaskLog> LogTaskStartAsync(
            string taskType,
            TaskParameters parameters,
            string triggeredBy = TaskTrigger.Scheduled
        )
        {
            try
            {
                var taskLog = new ScheduledTaskLog
                {
                    TaskType = taskType,
                    TaskParameters = JsonSerializer.Serialize(parameters),
                    Status = TaskStatus.Running,
                    StartedAt = DateTime.UtcNow,
                    ScheduledTime = DateTime.UtcNow,
                    TriggeredBy = triggeredBy,
                    CanRetry = true,
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

                return taskLog;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录任务开始时发生异常（数据库不可用），创建临时日志: {TaskType}", taskType);
                return new ScheduledTaskLog
                {
                    Id = Guid.NewGuid(),
                    TaskType = taskType,
                    TaskParameters = JsonSerializer.Serialize(parameters),
                    Status = TaskStatus.Running,
                    StartedAt = DateTime.UtcNow,
                    ScheduledTime = DateTime.UtcNow,
                    TriggeredBy = triggeredBy,
                    CanRetry = true,
                    ErrorMessage = string.Empty,
                };
            }
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
                await _context.ScheduledTaskLogDb.UpdateAsync(taskLog);

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
                await _context.ScheduledTaskLogDb.UpdateAsync(taskLog);

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
