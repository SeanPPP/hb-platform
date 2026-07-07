using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.DependencyInjection;
using TaskTrigger = BlazorApp.Shared.Models.HBweb.TaskTrigger;
using TaskType = BlazorApp.Shared.Models.HBweb.TaskType;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 数据对齐异常日期后台补算提交服务。
    /// </summary>
    public class SalesStatisticsAlignmentBackgroundRecalculateService
    {
        private readonly ScheduledTaskLogService _taskLogService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<SalesStatisticsAlignmentBackgroundRecalculateService> _logger;

        public SalesStatisticsAlignmentBackgroundRecalculateService(
            ScheduledTaskLogService taskLogService,
            IServiceScopeFactory serviceScopeFactory,
            ILogger<SalesStatisticsAlignmentBackgroundRecalculateService> logger
        )
        {
            _taskLogService = taskLogService;
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        public async Task<DailyStatisticsAlignmentRecalculateResponseDto> QueueAsync(
            IEnumerable<DateTime> dates,
            int maxConcurrency
        )
        {
            var targetDates = dates
                .Select(date => date.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();
            var concurrency = Math.Clamp(maxConcurrency, 1, 10);
            var parameters = new TaskParameters
            {
                StartDate = targetDates.First().ToString("yyyy-MM-dd"),
                EndDate = targetDates.Last().ToString("yyyy-MM-dd"),
                MaxConcurrency = concurrency,
                CustomParameters = new Dictionary<string, object>
                {
                    ["dates"] = targetDates.Select(date => date.ToString("yyyy-MM-dd")).ToList(),
                },
            };

            var taskLog = await _taskLogService.LogTaskStartAsync(
                TaskType.RecalculateDailyStatisticsAlignment,
                parameters,
                TaskTrigger.Manual,
                canRetry: false
            );

            // 关键位置：后台线程重新创建作用域，避免复用请求结束后的 scoped 服务。
            _ = Task.Run(
                () => ExecuteRecalculateAsync(taskLog.Id, targetDates, concurrency),
                CancellationToken.None
            );

            return new DailyStatisticsAlignmentRecalculateResponseDto
            {
                JobId = taskLog.Id,
                Success = true,
                Message = $"已提交 {targetDates.Count} 天异常统计后台补算，可在任务日志查看执行结果",
            };
        }

        private async Task ExecuteRecalculateAsync(
            Guid taskId,
            IReadOnlyCollection<DateTime> targetDates,
            int concurrency
        )
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var alignmentService = scope.ServiceProvider.GetRequiredService<SalesStatisticsAlignmentService>();
                var taskLogService = scope.ServiceProvider.GetRequiredService<ScheduledTaskLogService>();
                var cacheWarmer = scope.ServiceProvider.GetRequiredService<ISalesDashboardCacheWarmer>();

                var result = await alignmentService.RecalculateAsync(targetDates, concurrency);
                await cacheWarmer.ClearCacheAsync();

                if (result.Success)
                {
                    await taskLogService.LogTaskSuccessAsync(taskId);
                    return;
                }

                await taskLogService.LogTaskFailureAsync(
                    taskId,
                    BuildFailureMessage(result),
                    canRetry: false
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据对齐后台补算失败: {TaskId}", taskId);
                await LogFailureWithFreshScopeAsync(taskId, $"{ex.Message}\n{ex.StackTrace}");
            }
        }

        private async Task LogFailureWithFreshScopeAsync(Guid taskId, string errorMessage)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var taskLogService = scope.ServiceProvider.GetRequiredService<ScheduledTaskLogService>();
                await taskLogService.LogTaskFailureAsync(taskId, errorMessage, canRetry: false);
            }
            catch (Exception logException)
            {
                _logger.LogError(logException, "记录数据对齐后台补算失败日志时发生异常: {TaskId}", taskId);
            }
        }

        private static string BuildFailureMessage(DailyStatisticsAlignmentRecalculateResponseDto result)
        {
            var message = string.IsNullOrWhiteSpace(result.Message)
                ? "数据对齐后台补算失败"
                : result.Message;
            if (result.FailedDates.Count == 0)
            {
                return message;
            }

            var failedDates = string.Join(
                ", ",
                result.FailedDates.OrderBy(date => date).Select(date => date.ToString("yyyy-MM-dd"))
            );
            return $"{message}；失败日期：{failedDates}";
        }
    }
}
