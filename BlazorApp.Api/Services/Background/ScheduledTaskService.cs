using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.Background
{
    /// <summary>
    /// 定时任务配置选项
    /// </summary>
    public class ScheduledTaskOptions
    {
        /// <summary>
        /// 每小时任务间隔分钟数（默认 60 分钟）
        /// </summary>
        public int HourlyTaskIntervalMinutes { get; set; } = 20;

        /// <summary>
        /// 每日任务执行小时（默认 3 点）
        /// </summary>
        public int DailyTaskHour { get; set; } = 23;

        /// <summary>
        /// 每日任务执行分钟（默认 0 分）
        /// </summary>
        public int DailyTaskMinute { get; set; } = 0;

        /// <summary>
        /// 每周任务执行星期几（默认 0 = 星期日）
        /// </summary>
        public int WeeklyTaskDayOfWeek { get; set; } = 0;

        /// <summary>
        /// 每周任务执行小时（默认 23 点）
        /// </summary>
        public int WeeklyTaskHour { get; set; } = 23;

        /// <summary>
        /// 每周任务执行分钟（默认 59 分）
        /// </summary>
        public int WeeklyTaskMinute { get; set; } = 59;

        /// <summary>
        /// 每月任务执行日（默认 1 日）
        /// </summary>
        public int MonthlyTaskDay { get; set; } = 1;

        /// <summary>
        /// 每月任务执行小时（默认 3 点）
        /// </summary>
        public int MonthlyTaskHour { get; set; } = 3;

        /// <summary>
        /// 每月任务执行分钟（默认 0 分）
        /// </summary>
        public int MonthlyTaskMinute { get; set; } = 0;

        /// <summary>
        /// 是否启用随机冗余（默认 true）
        /// </summary>
        public bool EnableJitter { get; set; } = true;

        /// <summary>
        /// 随机冗余最大偏移分钟数（默认 5 分钟，即 -5 到 +5 分钟）
        /// </summary>
        public int JitterMaxMinutes { get; set; } = 5;
    }

    /// <summary>
    /// 定时任务调度服务
    /// 作为后台服务运行，负责按照固定时间间隔自动触发统计任务
    /// 包括每小时统计任务和每日全量刷新任务
    /// </summary>
    public class ScheduledTaskService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ScheduledTaskService> _logger;
        private readonly ScheduledTaskOptions _options;
        private readonly TimeZoneInfo _sydneyTimeZone;
        private Timer? _hourlyTimer;
        private Timer? _dailyTimer;
        private Timer? _weeklyTimer;
        private Timer? _monthlyTimer;

        public ScheduledTaskService(
            IServiceScopeFactory scopeFactory,
            ILogger<ScheduledTaskService> logger,
            IOptions<ScheduledTaskOptions> options
        )
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options.Value;
            _sydneyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
        }

        /// <summary>
        /// 后台服务执行入口
        /// 初始化每小时和每日定时器
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("定时任务服务启动");

            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _sydneyTimeZone);

            var baseInterval =
                _options.HourlyTaskIntervalMinutes
                - (now.Minute % _options.HourlyTaskIntervalMinutes);

            double nextHourlyMinutes;
            if (_options.EnableJitter)
            {
                var jitter = Random.Shared.Next(
                    -_options.JitterMaxMinutes,
                    _options.JitterMaxMinutes + 1
                );
                nextHourlyMinutes = Math.Max(1, baseInterval + jitter);
            }
            else
            {
                nextHourlyMinutes = baseInterval;
            }

            _hourlyTimer = new Timer(
                async _ => await ExecuteHourlyTask(),
                null,
                TimeSpan.FromMinutes(nextHourlyMinutes),
                TimeSpan.FromMinutes(_options.HourlyTaskIntervalMinutes)
            );

            // 计算到下一个每日任务执行时间的时间间隔
            var nextDaily = CalculateNextDailyRun(now);
            _dailyTimer = new Timer(
                async _ => await ExecuteDailyTask(),
                null,
                nextDaily,
                TimeSpan.FromDays(1)
            );

            // 计算到下一个每周任务执行时间的时间间隔
            var nextWeekly = CalculateNextWeeklyRun(now);
            _weeklyTimer = new Timer(
                async _ => await ExecuteWeeklyTask(),
                null,
                nextWeekly,
                TimeSpan.FromDays(7)
            );

            // 计算到下一个每月任务执行时间的时间间隔
            var nextMonthly = CalculateNextMonthlyRun(now);
            _monthlyTimer = new Timer(
                async _ => await ExecuteMonthlyTask(),
                null,
                nextMonthly,
                TimeSpan.FromDays(30) // 近似值，实际会由 CalculateNextMonthlyRun 精确计算
            );

            _logger.LogInformation(
                "定时任务已注册 - 每小时任务将在 {Minutes} 分钟后开始{EnableJitter}，"
                    + "每日任务将在 {Time} 开始，每周任务将在 {WeeklyTime} 开始，每月任务将在 {MonthlyTime} 开始",
                (int)nextHourlyMinutes,
                _options.EnableJitter ? $"（冗余偏移: ±{_options.JitterMaxMinutes}分钟）" : "",
                now.Add(nextDaily).ToString("HH:mm:ss"),
                now.Add(nextWeekly).ToString("yyyy-MM-dd HH:mm:ss"),
                now.Add(nextMonthly).ToString("yyyy-MM-dd HH:mm:ss")
            );

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        /// <summary>
        /// 计算下一次每日任务的执行时间
        /// </summary>
        /// <param name="now">当前时间</param>
        /// <returns>距离下次执行的时间间隔</returns>
        private TimeSpan CalculateNextDailyRun(DateTime now)
        {
            var nextRun = new DateTime(
                now.Year,
                now.Month,
                now.Day,
                _options.DailyTaskHour,
                _options.DailyTaskMinute,
                0
            );

            if (now >= nextRun)
            {
                nextRun = nextRun.AddDays(1);
            }

            if (_options.EnableJitter)
            {
                var jitter = Random.Shared.Next(
                    -_options.JitterMaxMinutes,
                    _options.JitterMaxMinutes + 1
                );
                nextRun = nextRun.AddMinutes(jitter);

                if (nextRun <= now)
                {
                    nextRun = now.AddMinutes(1);
                }
            }

            return nextRun - now;
        }

        /// <summary>
        /// 计算下一次每周任务的执行时间
        /// </summary>
        /// <param name="now">当前时间</param>
        /// <returns>距离下次执行的时间间隔</returns>
        private TimeSpan CalculateNextWeeklyRun(DateTime now)
        {
            var targetDayOfWeek = (DayOfWeek)_options.WeeklyTaskDayOfWeek;
            var daysUntil = ((int)targetDayOfWeek - (int)now.DayOfWeek + 7) % 7;
            var next = now
                .Date.AddDays(daysUntil)
                .AddHours(_options.WeeklyTaskHour)
                .AddMinutes(_options.WeeklyTaskMinute);

            if (next <= now)
            {
                next = next.AddDays(7);
            }

            if (_options.EnableJitter)
            {
                var jitter = Random.Shared.Next(
                    -_options.JitterMaxMinutes,
                    _options.JitterMaxMinutes + 1
                );
                next = next.AddMinutes(jitter);

                if (next <= now)
                {
                    next = now.AddMinutes(1);
                }
            }

            return next - now;
        }

        /// <summary>
        /// 计算下一次每月任务的执行时间
        /// </summary>
        /// <param name="now">当前时间</param>
        /// <returns>距离下次执行的时间间隔</returns>
        private TimeSpan CalculateNextMonthlyRun(DateTime now)
        {
            var year = now.Year;
            var month = now.Month;
            var next = new DateTime(
                year,
                month,
                _options.MonthlyTaskDay,
                _options.MonthlyTaskHour,
                _options.MonthlyTaskMinute,
                0
            );

            if (next <= now)
            {
                next = next.AddMonths(1);
            }

            if (_options.EnableJitter)
            {
                var jitter = Random.Shared.Next(
                    -_options.JitterMaxMinutes,
                    _options.JitterMaxMinutes + 1
                );
                next = next.AddMinutes(jitter);

                if (next <= now)
                {
                    next = now.AddMinutes(1);
                }
            }

            return next - now;
        }

        /// <summary>
        /// 执行每小时统计任务
        /// 更新当前小时的统计数据
        /// </summary>
        private async Task ExecuteHourlyTask()
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _sydneyTimeZone);
            var startHour = 7;
            var endHour = now.DayOfWeek == DayOfWeek.Thursday ? 23 : 20;

            // 检查是否在允许的时间段内 (8:00 - endHour:00)
            // 允许稍微超过 endHour 一点点（比如5分钟），以容忍 Timer 的微小延迟，确保整点任务能执行
            // 但主要是为了拦截如 18:20, 18:40 这种超出范围的任务
            if (
                now.Hour < startHour
                || now.Hour > endHour
                || (now.Hour == endHour && now.Minute > 5)
            )
            {
                _logger.LogInformation(
                    "当前时间 {Time} 不在定时任务执行范围内 ({Start}:00 - {End}:00)，跳过执行",
                    now.ToString("HH:mm"),
                    startHour,
                    endHour
                );
                return;
            }

            // 每个小时子任务都使用独立 scope，避免前一个任务的连接/上下文异常污染后续任务。
            await ExecuteHourlyTaskWithIndependentScopeAsync(
                TaskType.SyncPosmProductSupplierMappingsIncremental,
                "商品-供应商映射增量同步",
                async serviceProvider =>
                {
                    var dataSyncService =
                        serviceProvider.GetRequiredService<IDataSyncReactService>();
                    var result =
                        await dataSyncService.SyncPosmProductSupplierMappingsIncrementalAsync();
                    if (!result.IsSuccess)
                    {
                        // 同步服务以结果对象表达业务失败，调度层必须转成异常，避免随后把失败任务覆盖成成功。
                        throw new InvalidOperationException(
                            string.IsNullOrWhiteSpace(result.Message)
                                ? "商品-供应商映射增量同步返回失败"
                                : result.Message
                        );
                    }
                }
            );

            await ExecuteHourlyTaskWithIndependentScopeAsync(
                TaskType.UpdateCurrentHourStatistics,
                "每小时统计任务",
                async serviceProvider =>
                {
                    var statisticsJobService =
                        serviceProvider.GetRequiredService<SalesStatisticsJobService>();
                    await statisticsJobService.FullRefreshCurrentDay();
                }
            );

            await ExecuteHourlyTaskWithIndependentScopeAsync(
                TaskType.WarmUpStoreOrderCache,
                "商品列表缓存预热任务",
                async serviceProvider =>
                {
                    var cacheWarmer =
                        serviceProvider.GetRequiredService<BlazorApp.Api.Interfaces.IStoreOrderCacheWarmer>();
                    await cacheWarmer.WarmUpHomePageAsync();
                }
            );
        }

        /// <summary>
        /// 在独立作用域中执行每小时子任务
        /// </summary>
        private async Task ExecuteHourlyTaskWithIndependentScopeAsync(
            string taskType,
            string taskName,
            Func<IServiceProvider, Task> taskAction
        )
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;

                // 任务日志不是业务前置条件，日志服务拿不到时也要继续执行业务。
                var taskLogService = serviceProvider.GetService<ScheduledTaskLogService>();
                if (taskLogService == null)
                {
                    _logger.LogWarning("{TaskName} 未获取到任务日志服务，将继续执行业务任务", taskName);
                }

                ScheduledTaskLog? taskLog = null;
                if (taskLogService != null)
                {
                    try
                    {
                        taskLog = await taskLogService.LogTaskStartAsync(
                            taskType,
                            new TaskParameters()
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{TaskName} 记录任务开始日志失败，继续执行业务任务", taskName);
                    }
                }

                try
                {
                    _logger.LogInformation("开始执行{TaskName}", taskName);
                    await taskAction(serviceProvider);

                    if (taskLog != null && taskLogService != null)
                    {
                        await taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    }

                    _logger.LogInformation("{TaskName}执行完成", taskName);
                }
                catch (Exception ex)
                {
                    if (taskLog != null && taskLogService != null)
                    {
                        await TryLogTaskFailureAsync(
                            () =>
                                taskLogService.LogTaskFailureAsync(
                                    taskLog.Id,
                                    BuildTaskFailureMessage(ex)
                                ),
                            _logger,
                            taskName
                        );
                    }

                    _logger.LogError(ex, "{TaskName}执行失败", taskName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{TaskName} 创建独立作用域或解析依赖失败", taskName);
            }
        }

        /// <summary>
        /// 统一格式化任务异常信息
        /// </summary>
        private static string BuildTaskFailureMessage(Exception ex)
        {
            return ex.Message + "\n" + ex.StackTrace;
        }

        /// <summary>
        /// 失败日志写入只能作为附加信息，不能反过来打断业务异常处理链路
        /// </summary>
        private static async Task TryLogTaskFailureAsync(
            Func<Task> logFailureAsync,
            ILogger logger,
            string taskName
        )
        {
            try
            {
                await logFailureAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "记录任务失败日志时发生异常，任务：{TaskName}", taskName);
            }
        }

        /// <summary>
        /// 执行每日全量刷新任务
        /// 全量刷新当天的统计数据
        /// </summary>
        private async Task ExecuteDailyTask()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var taskLogService =
                    scope.ServiceProvider.GetRequiredService<ScheduledTaskLogService>();

                ScheduledTaskLog? taskLog = null;
                try
                {
                    taskLog = await taskLogService.LogTaskStartAsync(
                        TaskType.FullRefreshPreviousDay,
                        new TaskParameters()
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "记录任务开始日志失败，继续执行业务任务");
                }

                try
                {
                    _logger.LogInformation("开始执行每日全量刷新任务");

                    var statisticsJobService =
                        scope.ServiceProvider.GetRequiredService<SalesStatisticsJobService>();
                    await statisticsJobService.FullRefreshCurrentDay();

                    if (taskLog != null)
                        await taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    _logger.LogInformation("每日全量刷新任务执行完成");
                }
                catch (Exception ex)
                {
                    if (taskLog != null)
                        await TryLogTaskFailureAsync(
                            () =>
                                taskLogService.LogTaskFailureAsync(
                                    taskLog.Id,
                                    BuildTaskFailureMessage(ex)
                                ),
                            _logger,
                            "每日全量刷新任务"
                        );
                    _logger.LogError(ex, "每日全量刷新任务执行失败");
                }

                try
                {
                    _logger.LogInformation("开始执行公共假期自动同步任务");
                    var holidaySyncService =
                        scope.ServiceProvider.GetRequiredService<IAttendancePublicHolidaySyncService>();
                    var result = await holidaySyncService.SyncAllActiveStoresAsync();
                    _logger.LogInformation(
                        "公共假期自动同步完成：同步 {SyncedCount} 条，新增 {CreatedCount} 条，更新 {UpdatedCount} 条，跳过 {SkippedCount} 个分店",
                        result.SyncedCount,
                        result.CreatedCount,
                        result.UpdatedCount,
                        result.SkippedCount
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "公共假期自动同步任务执行失败");
                }
            }
        }

        /// <summary>
        /// 执行每周全量刷新任务
        /// 全量刷新本周的统计数据
        /// </summary>
        private async Task ExecuteWeeklyTask()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var taskLogService =
                    scope.ServiceProvider.GetRequiredService<ScheduledTaskLogService>();

                ScheduledTaskLog? taskLog = null;
                try
                {
                    taskLog = await taskLogService.LogTaskStartAsync(
                        TaskType.FullRefreshCurrentWeek,
                        new TaskParameters()
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "记录任务开始日志失败，继续执行业务任务");
                }

                try
                {
                    _logger.LogInformation("开始执行每周全量刷新任务");

                    var statisticsJobService =
                        scope.ServiceProvider.GetRequiredService<SalesStatisticsJobService>();

                    var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _sydneyTimeZone);
                    var monday = now.Date.AddDays(-(int)now.DayOfWeek + (int)DayOfWeek.Monday);
                    var sunday = monday.AddDays(6);

                    _logger.LogInformation(
                        "每周统计任务将刷新 {StartDate} 至 {EndDate} 的数据",
                        monday.ToString("yyyy-MM-dd"),
                        sunday.ToString("yyyy-MM-dd")
                    );

                    await statisticsJobService.BatchFullRefreshConcurrent(monday, sunday);

                    if (taskLog != null)
                        await taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    _logger.LogInformation("每周全量刷新任务执行完成");
                }
                catch (Exception ex)
                {
                    if (taskLog != null)
                        await TryLogTaskFailureAsync(
                            () =>
                                taskLogService.LogTaskFailureAsync(
                                    taskLog.Id,
                                    BuildTaskFailureMessage(ex)
                                ),
                            _logger,
                            "每周全量刷新任务"
                        );
                    _logger.LogError(ex, "每周全量刷新任务执行失败");
                }
            }
        }

        /// <summary>
        /// 执行每月全量刷新任务
        /// 全量刷新前一个月的统计数据
        /// </summary>
        private async Task ExecuteMonthlyTask()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var taskLogService =
                    scope.ServiceProvider.GetRequiredService<ScheduledTaskLogService>();

                ScheduledTaskLog? taskLog = null;
                try
                {
                    taskLog = await taskLogService.LogTaskStartAsync(
                        TaskType.FullRefreshPreviousMonth,
                        new TaskParameters()
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "记录任务开始日志失败，继续执行业务任务");
                }

                try
                {
                    _logger.LogInformation("开始执行每月全量刷新任务");

                    var statisticsJobService =
                        scope.ServiceProvider.GetRequiredService<SalesStatisticsJobService>();

                    var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _sydneyTimeZone);
                    var previousMonth = now.AddMonths(-1);
                    var startYearMonth = previousMonth.ToString("yyyy-MM");

                    _logger.LogInformation(
                        "每月统计任务将刷新 {YearMonth} 整月的数据",
                        startYearMonth
                    );

                    var result = await statisticsJobService.BatchFullRefreshByMonths(
                        startYearMonth,
                        startYearMonth
                    );

                    if (!result.Success)
                    {
                        throw new Exception($"每月统计任务失败: {result.Message}");
                    }

                    if (taskLog != null)
                        await taskLogService.LogTaskSuccessAsync(taskLog.Id);
                    _logger.LogInformation("每月全量刷新任务执行完成");
                }
                catch (Exception ex)
                {
                    if (taskLog != null)
                        await TryLogTaskFailureAsync(
                            () =>
                                taskLogService.LogTaskFailureAsync(
                                    taskLog.Id,
                                    BuildTaskFailureMessage(ex)
                                ),
                            _logger,
                            "每月全量刷新任务"
                        );
                    _logger.LogError(ex, "每月全量刷新任务执行失败");
                }
            }
        }

        /// <summary>
        /// 手动触发每小时统计任务
        /// </summary>
        public async Task TriggerHourlyTaskManually()
        {
            _logger.LogInformation("手动触发每小时统计任务");
            await ExecuteHourlyTask();
        }

        /// <summary>
        /// 手动触发每日全量刷新任务
        /// </summary>
        public async Task TriggerDailyTaskManually()
        {
            _logger.LogInformation("手动触发每日全量刷新任务");
            await ExecuteDailyTask();
        }

        /// <summary>
        /// 手动触发每周全量刷新任务
        /// </summary>
        public async Task TriggerWeeklyTaskManually()
        {
            _logger.LogInformation("手动触发每周全量刷新任务");
            await ExecuteWeeklyTask();
        }

        /// <summary>
        /// 手动触发每月全量刷新任务
        /// </summary>
        public async Task TriggerMonthlyTaskManually()
        {
            _logger.LogInformation("手动触发每月全量刷新任务");
            await ExecuteMonthlyTask();
        }

        /// <summary>
        /// 停止定时任务服务
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("定时任务服务停止");
            _hourlyTimer?.Dispose();
            _dailyTimer?.Dispose();
            _weeklyTimer?.Dispose();
            _monthlyTimer?.Dispose();
            await base.StopAsync(cancellationToken);
        }
    }
}
