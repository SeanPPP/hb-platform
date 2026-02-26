using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Services.Background
{
    /// <summary>
    /// 定时任务重试服务
    /// 负责重试执行失败的任务，保证数据完整性
    /// 支持单个任务重试和批量任务重试
    /// </summary>
    public class ScheduledTaskRetryService
    {
        private readonly SalesStatisticsJobService _statisticsJobService;
        private readonly ScheduledTaskLogService _taskLogService;
        private readonly ILogger<ScheduledTaskRetryService> _logger;

        public ScheduledTaskRetryService(
            SalesStatisticsJobService statisticsJobService,
            ScheduledTaskLogService taskLogService,
            ILogger<ScheduledTaskRetryService> logger
        )
        {
            _statisticsJobService = statisticsJobService;
            _taskLogService = taskLogService;
            _logger = logger;
        }

        /// <summary>
        /// 重试单个失败任务
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>重试是否成功</returns>
        public async Task<bool> RetryTaskAsync(Guid taskId)
        {
            var taskLog = await _taskLogService.GetTaskAsync(taskId);
            if (taskLog == null)
            {
                _logger.LogWarning("任务不存在: {TaskId}", taskId);
                return false;
            }

            if (!taskLog.CanRetry)
            {
                _logger.LogWarning("任务不允许重试: {TaskId}", taskId);
                return false;
            }

            var parameters = taskLog.GetParameters();

            _logger.LogInformation(
                "开始重试任务: {TaskType}, TaskId: {TaskId}, 重试次数: {RetryCount}",
                taskLog.TaskType,
                taskLog.Id,
                taskLog.RetryCount + 1
            );

            try
            {
                await ExecuteTaskByType(taskLog.TaskType, parameters, TaskTrigger.Retry);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "重试任务失败: {TaskType}, TaskId: {TaskId}",
                    taskLog.TaskType,
                    taskId
                );
                return false;
            }
        }

        /// <summary>
        /// 根据任务类型执行对应的任务
        /// 支持以下任务类型：
        /// - UpdateCurrentHourStatistics: 更新当前小时统计
        /// - UpdateDailyStatistics: 更新指定日期的日统计
        /// - UpdateDailyStatisticsBatch: 批量更新日期范围的日统计
        /// - UpdateHourlyStatistics: 更新指定日期的小时统计
        /// - UpdateHourlyStatisticsBatch: 批量更新日期范围的小时统计
        /// - UpdateStoreStatistics: 更新指定日期的门店统计
        /// - UpdateStoreStatisticsBatch: 批量更新日期范围的门店统计
        /// - UpdateSupplierStatistics: 更新指定日期的供应商统计
        /// - UpdateSupplierStatisticsBatch: 批量更新日期范围的供应商统计
        /// - UpdateStoreSupplierStatistics: 更新指定日期的门店供应商统计
        /// - UpdateStoreSupplierStatisticsBatch: 批量更新日期范围的门店供应商统计
        /// - FullRefreshPreviousDay: 全量刷新前一天数据
        /// </summary>
        /// <param name="taskType">任务类型</param>
        /// <param name="parameters">任务参数</param>
        /// <param name="triggeredBy">触发方式</param>
        private async Task ExecuteTaskByType(string taskType, TaskParameters parameters, string triggeredBy)
        {
            var newTaskLog = await _taskLogService.LogTaskStartAsync(taskType, parameters, triggeredBy);

            try
            {
                switch (taskType)
                {
                    case TaskType.UpdateCurrentHourStatistics:
                        await _statisticsJobService.UpdateCurrentHourStatistics();
                        break;

                    case TaskType.UpdateDailyStatistics:
                        await _statisticsJobService.UpdateDailyStatistics(parameters.Date);
                        break;

                    case TaskType.UpdateDailyStatisticsBatch:
                        if (
                            !string.IsNullOrEmpty(parameters.StartDate)
                            && !string.IsNullOrEmpty(parameters.EndDate)
                        )
                        {
                            await _statisticsJobService.BatchUpdateDailyStatistics(
                                DateTime.Parse(parameters.StartDate),
                                DateTime.Parse(parameters.EndDate)
                            );
                        }
                        break;

                    case TaskType.UpdateHourlyStatistics:
                        if (!string.IsNullOrEmpty(parameters.Date))
                        {
                            await _statisticsJobService.UpdateHourlyStatistics(
                                DateTime.Parse(parameters.Date),
                                parameters.Hour
                            );
                        }
                        break;

                    case TaskType.UpdateHourlyStatisticsBatch:
                        if (
                            !string.IsNullOrEmpty(parameters.StartDate)
                            && !string.IsNullOrEmpty(parameters.EndDate)
                        )
                        {
                            await _statisticsJobService.BatchUpdateHourlyStatistics(
                                DateTime.Parse(parameters.StartDate),
                                DateTime.Parse(parameters.EndDate),
                                parameters.Hour
                            );
                        }
                        break;

                    case TaskType.UpdateStoreStatistics:
                        if (!string.IsNullOrEmpty(parameters.Date))
                        {
                            await _statisticsJobService.UpdateStoreStatistics(
                                DateTime.Parse(parameters.Date),
                                parameters.BranchCodes
                            );
                        }
                        break;

                    case TaskType.UpdateStoreStatisticsBatch:
                        if (
                            !string.IsNullOrEmpty(parameters.StartDate)
                            && !string.IsNullOrEmpty(parameters.EndDate)
                        )
                        {
                            await _statisticsJobService.BatchUpdateStoreStatistics(
                                DateTime.Parse(parameters.StartDate),
                                DateTime.Parse(parameters.EndDate),
                                parameters.BranchCodes
                            );
                        }
                        break;

                    case TaskType.UpdateSupplierStatistics:
                        if (!string.IsNullOrEmpty(parameters.Date))
                        {
                            await _statisticsJobService.UpdateSupplierStatistics(
                                DateTime.Parse(parameters.Date),
                                null,
                                parameters.SupplierCodes
                            );
                        }
                        break;

                    case TaskType.UpdateSupplierStatisticsBatch:
                        if (
                            !string.IsNullOrEmpty(parameters.StartDate)
                            && !string.IsNullOrEmpty(parameters.EndDate)
                        )
                        {
                            await _statisticsJobService.BatchUpdateSupplierStatistics(
                                DateTime.Parse(parameters.StartDate),
                                DateTime.Parse(parameters.EndDate),
                                parameters.SupplierCodes
                            );
                        }
                        break;

                    case TaskType.FullRefreshPreviousDay:
                        await _statisticsJobService.FullRefreshPreviousDay();
                        break;

                    case TaskType.UpdateStoreSupplierStatistics:
                        if (!string.IsNullOrEmpty(parameters.Date))
                        {
                            await _statisticsJobService.UpdateStoreSupplierStatistics(
                                DateTime.Parse(parameters.Date),
                                parameters.BranchCodes,
                                parameters.SupplierCodes
                            );
                        }
                        break;

                    case TaskType.UpdateStoreSupplierStatisticsBatch:
                        if (
                            !string.IsNullOrEmpty(parameters.StartDate)
                            && !string.IsNullOrEmpty(parameters.EndDate)
                        )
                        {
                            await _statisticsJobService.BatchUpdateStoreSupplierStatistics(
                                DateTime.Parse(parameters.StartDate),
                                DateTime.Parse(parameters.EndDate),
                                parameters.BranchCodes,
                                parameters.SupplierCodes
                            );
                        }
                        break;

                    default:
                        throw new ArgumentException($"未知的任务类型: {taskType}");
                }

                await _taskLogService.LogTaskSuccessAsync(newTaskLog.Id);
            }
            catch (Exception ex)
            {
                await _taskLogService.LogTaskFailureAsync(
                    newTaskLog.Id,
                    ex.Message + "\n" + ex.StackTrace
                );
                throw;
            }
        }

        /// <summary>
        /// 批量重试失败任务
        /// </summary>
        /// <param name="taskType">任务类型（可选）</param>
        /// <param name="startDate">开始日期（可选）</param>
        /// <param name="endDate">结束日期（可选）</param>
        /// <returns>重试成功的任务数量</returns>
        public async Task<int> BatchRetryFailedTasksAsync(
            string? taskType = null,
            DateTime? startDate = null,
            DateTime? endDate = null
        )
        {
            var failedTasks = await _taskLogService.GetFailedTasksAsync(
                taskType,
                startDate,
                endDate
            );

            int successCount = 0;
            int failureCount = 0;

            _logger.LogInformation(
                "开始批量重试失败任务，共 {Count} 个任务",
                failedTasks.Count
            );

            foreach (var task in failedTasks)
            {
                try
                {
                    var result = await RetryTaskAsync(task.Id);
                    if (result)
                    {
                        successCount++;
                    }
                    else
                    {
                        failureCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重试任务异常: {TaskId}", task.Id);
                    failureCount++;
                }
            }

            _logger.LogInformation(
                "批量重试完成，成功: {SuccessCount}, 失败: {FailureCount}",
                successCount,
                failureCount
            );

            return successCount;
        }
    }
}
