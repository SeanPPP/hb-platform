using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Services
{
    /// <summary>销售统计垂直切片：SalesStatisticsOrchestrationSlice。</summary>
    internal sealed class SalesStatisticsOrchestrationSlice : SalesStatisticsSliceBase
    {
        private readonly SalesStatisticsOrchestrationStore _store = new();
        private readonly SalesStatisticsProductStoreDailyRefreshSlice _productRefresh;
        private readonly SalesStatisticsProductStoreDailySupportSlice _productSupport;

        public SalesStatisticsOrchestrationSlice(
            SalesStatisticsSliceContext shared,
            SalesStatisticsProductStoreDailyRefreshSlice productRefresh,
            SalesStatisticsProductStoreDailySupportSlice productSupport)
            : base(shared)
        {
            _productRefresh = productRefresh;
            _productSupport = productSupport;
        }

    public async Task<BatchStatisticsUpdateResult> BatchFullRefreshByMonths(
        string startYearMonth,
        string endYearMonth,
        int maxMonths = 12
    )
    {
        var result = new BatchStatisticsUpdateResult();

        // 解析开始年月
        if (
            !DateTime.TryParseExact(
                startYearMonth,
                "yyyy-MM",
                null,
                System.Globalization.DateTimeStyles.None,
                out var startDate
            )
        )
        {
            result.Message = $"开始年月格式错误，应为 yyyy-MM 格式（如 2024-01）";
            _logger.LogWarning(result.Message);
            return result;
        }

        // 解析结束年月
        if (
            !DateTime.TryParseExact(
                endYearMonth,
                "yyyy-MM",
                null,
                System.Globalization.DateTimeStyles.None,
                out var endDate
            )
        )
        {
            result.Message = $"结束年月格式错误，应为 yyyy-MM 格式（如 2024-06）";
            _logger.LogWarning(result.Message);
            return result;
        }

        // 设置开始日期为该月第一天
        startDate = new DateTime(startDate.Year, startDate.Month, 1);
        // 设置结束日期为该月最后一天
        var endMonthLastDay = new DateTime(
            endDate.Year,
            endDate.Month,
            DateTime.DaysInMonth(endDate.Year, endDate.Month)
        );

        // 验证月份范围
        var validation = ValidateMonthRange(startDate, endMonthLastDay, maxMonths);
        if (!validation.Success)
        {
            return validation;
        }

        result.TotalMonths =
            ((endMonthLastDay.Year - startDate.Year) * 12)
            + endMonthLastDay.Month
            - startDate.Month
            + 1;
        result.TotalDays = (int)(endMonthLastDay - startDate).TotalDays + 1;

        _logger.LogInformation(
            "开始批量按月份刷新完整数据: {StartYearMonth} 至 {EndYearMonth}, 共 {Months} 个月, {Days} 天",
            startYearMonth,
            endYearMonth,
            result.TotalMonths,
            result.TotalDays
        );

        var fullRefreshMaxConcurrency = ResolveFullRefreshMaxConcurrency(
            startDate,
            endMonthLastDay,
            _maxConcurrentUpdates
        );

        for (var rangeStart = startDate; rangeStart <= endMonthLastDay;)
        {
            var rangeEnd = rangeStart.AddDays(_maxDaysForConcurrentUpdate - 1);
            if (rangeEnd > endMonthLastDay)
            {
                rangeEnd = endMonthLastDay;
            }

            // 月/季度复查复用同一条带数据库租约的日级全量刷新路径，避免旧批量入口漏表或重复跑。
            var rangeResult = await BatchFullRefreshConcurrent(
                rangeStart,
                rangeEnd,
                fullRefreshMaxConcurrency
            );
            result.ProcessedDays += rangeResult.ProcessedDays;
            result.FailedDates.AddRange(rangeResult.FailedDates);
            result.SkippedDates.AddRange(rangeResult.SkippedDates);
            rangeStart = rangeEnd.AddDays(1);
        }

        for (var month = startDate; month <= endMonthLastDay; month = month.AddMonths(1))
        {
            var monthKey = month.ToString("yyyy-MM");
            result.ProcessedMonths++;
            if (result.FailedDates.Any(date => date.StartsWith(monthKey, StringComparison.Ordinal)))
            {
                result.FailedMonths.Add(monthKey);
            }
        }

        result.Success = result.FailedDates.Count == 0;
        result.Message = result.Success
            ? $"批量按月份刷新完成: {result.ProcessedDays}/{result.TotalDays} 天, 跳过: {result.SkippedDates.Count} 天, {result.ProcessedMonths}/{result.TotalMonths} 个月"
            : $"批量按月份刷新部分完成: {result.ProcessedDays}/{result.TotalDays} 天, 跳过: {result.SkippedDates.Count} 天, 失败 {result.FailedDates.Count} 天, 失败月份: {string.Join(", ", result.FailedMonths)}";

        _logger.LogInformation(result.Message);
        return result;
    }

    /// <summary>
    /// 并发全量刷新数据
    /// 将日期范围拆分为多个块，并发处理以提高效率
    /// </summary>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="maxConcurrency">最大并发数，为空则使用配置值</param>
    /// <returns>批量更新结果</returns>
    public async Task<BatchStatisticsUpdateResult> BatchFullRefreshConcurrent(
        DateTime startDate,
        DateTime endDate,
        int? maxConcurrency = null
    )
    {
        var result = new BatchStatisticsUpdateResult();

        // 验证日期范围
        if (startDate > endDate)
        {
            result.Message = "开始日期不能大于结束日期";
            _logger.LogWarning(result.Message);
            return result;
        }

        var totalDays = (int)(endDate - startDate).TotalDays + 1;

        // 检查日期范围是否超出并发更新支持的最大天数
        if (totalDays > _maxDaysForConcurrentUpdate)
        {
            result.Message =
                $"日期范围过大，并发更新最多支持 {_maxDaysForConcurrentUpdate} 天（当前: {totalDays} 天）";
            _logger.LogWarning(result.Message);
            return result;
        }

        // 确定并发度
        var concurrency = ResolveFullRefreshMaxConcurrency(
            startDate,
            endDate,
            maxConcurrency ?? _maxConcurrentUpdates
        );

        _logger.LogInformation(
            "开始并发完整刷新统计数据: {StartDate} 至 {EndDate}, 并发度: {Concurrency}",
            startDate.ToString("yyyy-MM-dd"),
            endDate.ToString("yyyy-MM-dd"),
            concurrency
        );

        // 拆分日期范围为多个并发块
        var dateRanges = SalesStatisticsSupplierBatchSlice.SplitDateRange(
            startDate,
            endDate,
            concurrency,
            _maxDaysPerChunk
        );
        _logger.LogInformation(
            "将 {TotalDays} 天拆分为 {ChunkCount} 个并发块",
            totalDays,
            dateRanges.Count
        );

        var processedDays = 0;
        var failedDates = new List<string>();
        var failedRanges = new List<string>();
        var skippedDates = new List<string>();
        var syncLock = new object();

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = concurrency };

        // 启动计时器
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 并发处理每个日期块
            await Parallel.ForEachAsync(
                dateRanges,
                parallelOptions,
                async (dateRange, cancellationToken) =>
                {
                    try
                    {
                        _logger.LogInformation(
                            "开始处理并发块 {StartDate} 至 {EndDate} ({Days} 天)",
                            dateRange.StartDate.ToString("yyyy-MM-dd"),
                            dateRange.EndDate.ToString("yyyy-MM-dd"),
                            dateRange.DayCount
                        );

                        // 为每个并发任务创建独立的作用域和数据库上下文
                        using var scope = _serviceScopeFactory.CreateScope();
                        var context =
                            scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
                        var posmContext =
                            scope.ServiceProvider.GetRequiredService<POSMSqlSugarContext>();
                        var hbSalesContext =
                            scope.ServiceProvider.GetService<HBSalesRecordSqlSugarContext>();
                        var logger = _logger;
                        var leaseService =
                            scope.ServiceProvider.GetRequiredService<ScheduledTaskLeaseService>();

                        // 调用带上下文的全量刷新方法
                        var rangeResult = await FullRefreshDateRangeWithContext(
                            context,
                            posmContext,
                            hbSalesContext,
                            logger,
                            leaseService,
                            dateRange.StartDate,
                            dateRange.EndDate
                        );

                        // 使用锁更新进度
                        lock (syncLock)
                        {
                            processedDays += rangeResult.ProcessedDays;
                            skippedDates.AddRange(rangeResult.SkippedDates);
                            failedDates.AddRange(rangeResult.FailedDates);
                        }

                        logger.LogInformation(
                            "并发块处理完成 {StartDate} 至 {EndDate} ({Days} 天), 累计进度: {Progress}/{Total}, 跳过: {Skipped}, 失败: {Failed}",
                            dateRange.StartDate.ToString("yyyy-MM-dd"),
                            dateRange.EndDate.ToString("yyyy-MM-dd"),
                            dateRange.DayCount,
                            processedDays,
                            totalDays,
                            rangeResult.SkippedDates.Count,
                            rangeResult.FailedDates.Count
                        );
                    }
                    catch (Exception ex)
                    {
                        var rangeKey =
                            $"{dateRange.StartDate:yyyy-MM-dd} 至 {dateRange.EndDate:yyyy-MM-dd}";
                        lock (syncLock)
                        {
                            failedRanges.Add(rangeKey);

                            // 记录失败的日期
                            for (
                                var d = dateRange.StartDate;
                                d <= dateRange.EndDate;
                                d = d.AddDays(1)
                            )
                            {
                                failedDates.Add(d.ToString("yyyy-MM-dd"));
                            }
                        }

                        _logger.LogError(
                            ex,
                            "并发块处理失败 {StartDate} 至 {EndDate}",
                            dateRange.StartDate.ToString("yyyy-MM-dd"),
                            dateRange.EndDate.ToString("yyyy-MM-dd")
                        );
                    }
                }
            );

            stopwatch.Stop();

            result.TotalDays = totalDays;
            result.ProcessedDays = processedDays;
            result.FailedDates = failedDates;
            result.SkippedDates = skippedDates;
            result.Success = failedDates.Count == 0;

            var avgTimePerDay = stopwatch.Elapsed.TotalSeconds / totalDays;
            result.Message = result.Success
                ? $"并发完整刷新完成: {processedDays}/{totalDays} 天, 跳过: {skippedDates.Count} 天, 总耗时: {stopwatch.Elapsed:mm\\:ss}, 平均每天: {avgTimePerDay:F2}秒"
                : $"并发完整刷新部分完成: {processedDays}/{totalDays} 天, 跳过: {skippedDates.Count} 天, 失败: {failedDates.Count} 天, 总耗时: {stopwatch.Elapsed:mm\\:ss}";

            _logger.LogInformation(
                "{ResultMessage}, 失败的日期块: {FailedRanges}",
                result.Message,
                failedRanges.Count > 0 ? string.Join("; ", failedRanges) : "无"
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.Message =
                $"并发完整刷新失败: 处理 {processedDays}/{totalDays} 天, 错误: {ex.Message}";
            _logger.LogError(
                ex,
                "并发完整刷新失败: {StartDate} 至 {EndDate}",
                startDate,
                endDate
            );
        }

        return result;
    }

    internal static int ResolveFullRefreshMaxConcurrency(
        DateTime startDate,
        DateTime endDate,
        int configuredConcurrency
    )
    {
        var includes2025 = startDate.Date <= new DateTime(2025, 12, 31)
            && endDate.Date >= new DateTime(2025, 1, 1);
        return includes2025 ? 1 : configuredConcurrency;
    }

    /// <summary>
    /// 带上下文全量刷新日期范围（用于并发处理）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="posmContext">POSM数据库上下文</param>
    /// <param name="hbSalesContext">HBSalesRecord 数据库上下文</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="leaseService">统计任务数据库租约服务</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    internal async Task<FullRefreshRangeExecutionResult> FullRefreshDateRangeWithContext(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        HBSalesRecordSqlSugarContext? hbSalesContext,
        ILogger logger,
        ScheduledTaskLeaseService leaseService,
        DateTime startDate,
        DateTime endDate
    )
    {
        var result = new FullRefreshRangeExecutionResult();
        try
        {
            logger.LogInformation(
                "开始完整刷新日期范围: {StartDate} 至 {EndDate} ({Days} 天)",
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd"),
                (int)(endDate - startDate).TotalDays + 1
            );

            // 逐日刷新所有统计数据
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                var dateStr = date.ToString("yyyy-MM-dd");
                var leaseTaskType = SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType;
                var leaseDuration = TimeSpan.FromHours(2);
                string? leaseToken = null;
                try
                {
                    var lease = await leaseService.TryAcquireAsync(
                        leaseTaskType,
                        dateStr,
                        leaseDuration
                    );
                    if (!lease.Acquired)
                    {
                        result.SkippedDates.Add(dateStr);
                        logger.LogInformation("日期 {Date} 已有统计租约运行中，本次完整刷新跳过", dateStr);
                        continue;
                    }

                    leaseToken = lease.Lease?.LeaseToken;
                    if (string.IsNullOrWhiteSpace(leaseToken))
                    {
                        throw new InvalidOperationException($"统计租约缺少 fencing token: {dateStr}");
                    }

                    var sourceWatermark = await SalesStatisticsProductStoreDailyStateSlice
                        .QueryDailySourceWatermarkAsync(
                        posmContext,
                        hbSalesContext,
                        date
                    );

                    async Task RunStep(
                        string statisticType,
                        string stepName,
                        Func<Task> action,
                        bool markFreshOnSuccess = true
                    )
                    {
                        await leaseService.EnsureActiveAsync(
                            leaseTaskType,
                            dateStr,
                            leaseToken,
                            leaseDuration,
                            stepName
                        );
                        await SalesStatisticsProductStoreDailyStateSlice.UpsertStatisticStateAsync(
                            context,
                            statisticType,
                            date,
                            SalesStatisticRefreshStatus.Running,
                            sourceWatermark,
                            null
                        );

                        try
                        {
                            await action();
                            await leaseService.EnsureActiveAsync(
                                leaseTaskType,
                                dateStr,
                                leaseToken,
                                leaseDuration,
                                $"{stepName}完成确认"
                            );
                            if (markFreshOnSuccess)
                            {
                                await SalesStatisticsProductStoreDailyStateSlice
                                    .UpsertStatisticStateAsync(
                                    context,
                                    statisticType,
                                    date,
                                    SalesStatisticRefreshStatus.Fresh,
                                    sourceWatermark,
                                    null
                                );
                            }
                        }
                        catch (Exception stepEx)
                        {
                            await SalesStatisticsProductStoreDailyStateSlice.UpsertStatisticStateAsync(
                                context,
                                statisticType,
                                date,
                                SalesStatisticRefreshStatus.Failed,
                                sourceWatermark,
                                stepEx.Message
                            );
                            throw;
                        }
                    }

                    await RunStep(
                        SalesStatisticType.DailySales,
                        "每日统计",
                        () => UpdateDailyStatisticsWithContext(context, posmContext, logger, dateStr)
                    );
                    await RunStep(
                        SalesStatisticType.HourlySales,
                        "分时统计",
                        () => UpdateHourlyStatisticsWithContext(context, posmContext, logger, date, null)
                    );
                    if (date.Year != 2025)
                    {
                        await RunStep(
                            SalesStatisticType.StoreSales,
                            "分店统计",
                            () => UpdateStoreStatisticsWithContext(
                                context,
                                posmContext,
                                hbSalesContext,
                                logger,
                                date,
                                null
                            )
                        );
                    }
                    await RunStep(
                        SalesStatisticType.SupplierSales,
                        "供应商统计",
                        () => SalesStatisticsSupplierBatchSlice.UpdateSupplierStatisticsWithContext(
                            context,
                            posmContext,
                            logger,
                            date,
                            date,
                            null
                        )
                    );
                    await RunStep(
                        SalesStatisticType.StoreSupplierSales,
                        "门店供应商统计",
                        () => UpdateStoreSupplierStatisticsWithContext(context, posmContext, logger, date, null, null)
                    );
                    await RunStep(
                        SalesStatisticType.ProductStoreDaily,
                        "商品分店每日统计",
                        async () =>
                        {
                            if (date.Year == 2025)
                            {
                                // 完整刷新也必须复用 2025 原子入口，不能先独立提交分店统计。
                                await _productRefresh.Update2025StoreAndProductStatisticsAtomically(
                                    context,
                                    posmContext,
                                    hbSalesContext
                                        ?? throw new InvalidOperationException(
                                            "2025 年完整刷新缺少 HBSalesRecord 上下文"
                                        ),
                                    logger,
                                    date,
                                    sourceWatermark
                                );
                            }
                            else
                            {
                                await _productRefresh.UpdateProductStoreDailyStatisticsWithContext(
                                    context,
                                    posmContext,
                                    hbSalesContext,
                                    logger,
                                    date
                                );
                            }

                            var productState = await _store.GetProductStoreDailyRefreshStateAsync(context, date);
                            if (productState == null
                                || productState.Status == SalesStatisticRefreshStatus.Failed)
                            {
                                throw new InvalidOperationException(
                                    productState?.ErrorMessage
                                        ?? $"商品分店每日统计状态缺失: {date:yyyy-MM-dd}"
                                );
                            }
                        },
                        false
                    );
                    await RunStep(
                        SalesStatisticType.AustralianSupplierStoreSales,
                        "澳洲供应商门店统计",
                        () => SalesStatisticsSupplierStoreSlice
                            .UpdateAustralianSupplierStoreStatisticsWithContext(
                                context,
                                posmContext,
                                logger,
                                date,
                                null,
                                null
                            )
                    );
                    await RunStep(
                        SalesStatisticType.ChinaSupplierStoreSales,
                        "中国供应商门店统计",
                        () => SalesStatisticsSupplierStoreSlice
                            .UpdateChinaSupplierStoreStatisticsWithContext(
                                context,
                                posmContext,
                                logger,
                                date,
                                null,
                                null
                            )
                    );

                    logger.LogInformation(
                        "日期 {Date} 完整刷新完成",
                        date.ToString("yyyy-MM-dd")
                    );
                    if (!await leaseService.CompleteAsync(leaseTaskType, dateStr, leaseToken, true))
                    {
                        throw new InvalidOperationException($"统计租约完成失败，token 已失效: {dateStr}");
                    }
                    result.ProcessedDays += 1;
                }
                catch (Exception ex)
                {
                    if (!string.IsNullOrWhiteSpace(leaseToken))
                    {
                        await leaseService.CompleteAsync(
                            leaseTaskType,
                            dateStr,
                            leaseToken,
                            false,
                            ex.Message
                        );
                    }
                    logger.LogError(
                        ex,
                        "日期 {Date} 完整刷新失败",
                        date.ToString("yyyy-MM-dd")
                    );
                    result.FailedDates.Add(dateStr);
                }
            }

            logger.LogInformation(
                "日期范围完整刷新完成: {StartDate} 至 {EndDate}",
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd")
            );
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "日期范围完整刷新失败: {StartDate} 至 {EndDate}",
                startDate,
                endDate
            );
            throw;
        }
    }

    /// <summary>
    /// 带上下文更新每日统计数据（用于并发处理）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="posmContext">POSM数据库上下文</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="dateStr">日期字符串</param>
internal async Task UpdateDailyStatisticsWithContext(
    SqlSugarContext context,
    POSMSqlSugarContext posmContext,
    ILogger logger,
    string dateStr
)
{
    try
    {
        var date = string.IsNullOrEmpty(dateStr)
            ? DateTime.Now.Date
            : DateTime.Parse(dateStr).Date;

        logger.LogInformation("开始更新每日统计数据: {Date}", date);
        var statistic = await SalesStatisticsDailyHourlySlice.BuildDailySalesStatisticAsync(
            posmContext,
            date,
            DateTime.Now
        );
        if (statistic != null)
        {
            await _store.UpsertDailySalesStatisticAsync(context, statistic);
        }

        logger.LogInformation("每日统计数据更新完成: {Date}", date);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "更新每日统计数据失败: {Date}", dateStr);
        throw;
    }
}

    /// <summary>
    /// 带上下文更新分时统计数据（用于并发处理）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="posmContext">POSM数据库上下文</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="date">目标日期</param>
    /// <param name="hour">指定小时，为空则更新所有小时</param>
internal Task UpdateHourlyStatisticsWithContext(
    SqlSugarContext context,
    POSMSqlSugarContext posmContext,
    ILogger logger,
    DateTime date,
    int? hour
)
{
    return _store.UpdateHourlyStatisticsWithContext(context, posmContext, logger, date, hour);
}

    /// <summary>
    /// 带上下文更新分店统计数据（用于并发处理）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="posmContext">POSM数据库上下文</param>
    /// <param name="hbSalesContext">HBSalesRecord 数据库上下文；仅 2025 年使用</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="date">目标日期</param>
    /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
internal async Task UpdateStoreStatisticsWithContext(
    SqlSugarContext context,
    POSMSqlSugarContext posmContext,
    HBSalesRecordSqlSugarContext? hbSalesContext,
    ILogger logger,
    DateTime date,
    List<string>? branchCodes
)
{
    try
    {
        var targetDate = date.Date;
        logger.LogInformation(
            "开始更新指定分店统计数据: {Date}, Branches: {Branches}",
            date,
            branchCodes != null ? string.Join(", ", branchCodes) : "All"
        );

        var statisticsList = await _productSupport.BuildStoreStatisticsAsync(
            context,
            posmContext,
            targetDate.Year == 2025
                ? hbSalesContext
                    ?? throw new InvalidOperationException("2025 年分店统计缺少 HBSalesRecord 上下文")
                : null,
            targetDate,
            branchCodes
        );
        await _store.ReplaceStoreStatisticsAsync(
            context, logger, targetDate, branchCodes, statisticsList);

        logger.LogInformation(
            "指定分店统计数据更新完成: {Date}, 总记录: {Total}",
            date,
            statisticsList.Count
        );
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "更新指定分店统计数据失败: {Date}", date);
        throw;
    }
}

    /// <summary>
    /// 带上下文更新门店供应商统计数据（用于并发处理）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="posmContext">POSM数据库上下文</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="date">目标日期，为空则更新当天</param>
    /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
    /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
internal Task UpdateStoreSupplierStatisticsWithContext(
    SqlSugarContext context,
    POSMSqlSugarContext posmContext,
    ILogger logger,
    DateTime? date,
    List<string>? branchCodes,
    List<string>? supplierCodes
)
{
    return _store.UpdateStoreSupplierStatisticsWithContext(
        context, posmContext, logger, date, branchCodes, supplierCodes);
}

    }
}
