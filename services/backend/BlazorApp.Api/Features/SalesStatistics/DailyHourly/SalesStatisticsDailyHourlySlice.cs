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
    /// <summary>销售统计垂直切片：SalesStatisticsDailyHourlySlice。</summary>
    internal sealed class SalesStatisticsDailyHourlySlice : SalesStatisticsSliceBase
    {
        private readonly SalesStatisticsStoreDailySlice _storeDaily;
        private readonly SalesStatisticsSupplierStoreSlice _supplierStore;

        public SalesStatisticsDailyHourlySlice(
            SalesStatisticsSliceContext shared,
            SalesStatisticsStoreDailySlice storeDaily,
            SalesStatisticsSupplierStoreSlice supplierStore)
            : base(shared)
        {
            _storeDaily = storeDaily;
            _supplierStore = supplierStore;
        }

    internal static async Task ExecuteTransactionSafelyAsync(
        Func<Task> beginAsync,
        Func<Task> workAsync,
        Func<Task> commitAsync,
        Func<Task> rollbackAsync,
        ILogger logger,
        string operationName
    ) => await SalesStatisticsTransactionExecutor.ExecuteAsync(
        beginAsync,
        workAsync,
        commitAsync,
        rollbackAsync,
        logger,
        operationName
    );

    /// <summary>
    /// 更新当前小时统计数据
    /// 包括分时统计、每日统计、分店统计、澳洲供应商门店统计、中国供应商门店统计
    /// </summary>
    public async Task UpdateCurrentHourStatistics()
    {
        try
        {
            // 获取当前时间
            var now = DateTime.Now;
            var currentHour = now.Hour;
            var currentDate = now.Date;

            _logger.LogInformation(
                "开始更新当前小时统计数据: {Date} {Hour}",
                currentDate,
                currentHour
            );

            // 更新分时统计数据
            await UpdateHourlyStatistics(currentDate, currentHour);
            // 更新每日统计数据
            await UpdateDailyStatistics(currentDate.ToString("yyyy-MM-dd"));
            // 更新分店统计数据
            await _storeDaily.UpdateStoreStatistics(currentDate);
            // await UpdateSupplierStatistics(currentDate);
            // await UpdateStoreSupplierStatistics(currentDate);
            // 更新澳洲供应商门店统计数据
            await _supplierStore.UpdateAustralianSupplierStoreStatistics(currentDate);
            // 更新中国供应商门店统计数据
            await _supplierStore.UpdateChinaSupplierStoreStatistics(currentDate);

            _logger.LogInformation(
                "当前小时统计数据更新完成: {Date} {Hour}",
                currentDate,
                currentHour
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新当前小时统计数据失败");
            throw;
        }
    }

    /// <summary>
    /// 更新每日统计数据
    /// 从POSM系统聚合当日销售订单的汇总数据
    /// </summary>
    /// <param name="dateStr">日期字符串（格式yyyy-MM-dd），为空则更新当天</param>
    public async Task UpdateDailyStatistics(string? dateStr = null)
    {
        try
        {
            // 确定目标日期
            var date = string.IsNullOrEmpty(dateStr)
                ? DateTime.Now.Date
                : DateTime.Parse(dateStr).Date;

            _logger.LogInformation("开始更新每日统计数据: {Date}", date);

            var statistic = await BuildDailySalesStatisticAsync(_posmContext, date, DateTime.Now);

            await ReplaceDailySalesStatisticAsync(_context, _logger, date, statistic);

            _logger.LogInformation("每日统计数据更新完成: {Date}", date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新每日统计数据失败: {Date}", dateStr);
            throw;
        }
    }

    internal static async Task<DailySalesStatistic?> BuildDailySalesStatisticAsync(
        POSMSqlSugarContext posmContext,
        DateTime date,
        DateTime updateTime
    )
    {
        var targetDate = date.Date;
        var nextDate = targetDate.AddDays(1);

        var paymentSummary = await posmContext.Db.Queryable<PaymentDetail, SalesOrder>(
                (pd, so) => pd.OrderGuid == so.OrderGuid
            )
            .Where((pd, so) =>
                so.Status != null
                && (so.Status == 1 || so.Status == 4)
                && so.OrderTime != null
                && so.OrderTime >= targetDate
                && so.OrderTime < nextDate
            )
            .GroupBy((pd, so) => so.OrderTime!.Value.Date)
            .Select((pd, so) => new
            {
                TotalAmount = SqlFunc.AggregateSum(pd.Amount) ?? 0m,
            })
            .FirstAsync();

        var quantitySummary = await posmContext.Db.Queryable<SalesOrderDetail, SalesOrder>(
                (d, so) => d.OrderGuid == so.OrderGuid
            )
            .Where((d, so) =>
                so.Status != null
                && (so.Status == 1 || so.Status == 4)
                && so.OrderTime != null
                && so.OrderTime >= targetDate
                && so.OrderTime < nextDate
            )
            .GroupBy((d, so) => so.OrderTime!.Value.Date)
            .Select((d, so) => new
            {
                TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0),
            })
            .FirstAsync();

        var skuCodes = await posmContext.Db.Queryable<SalesOrderDetail, SalesOrder>(
                (detail, order) => detail.OrderGuid == order.OrderGuid)
            .Where((detail, order) =>
                order.Status != null && (order.Status == 1 || order.Status == 4)
                && order.OrderTime != null && order.OrderTime >= targetDate && order.OrderTime < nextDate
                && detail.ProductCode != null && detail.ProductCode != string.Empty)
            .Select((detail, order) => detail.ProductCode)
            .Distinct()
            .ToListAsync();
        var orderRows = await posmContext.Db.Queryable<SalesOrder>()
            .Where(so =>
                so.Status != null
                && (so.Status == 1 || so.Status == 4)
                && so.OrderTime != null
                && so.OrderTime >= targetDate
                && so.OrderTime < nextDate
            )
            .GroupBy(so => so.OrderGuid)
            .Select(so => new StoreStatisticOrderRow
            {
                OrderGuid = so.OrderGuid,
            })
            .ToListAsync();

        var totalAmount = paymentSummary?.TotalAmount ?? 0m;
        var totalQuantity = quantitySummary?.TotalQuantity ?? 0;
        // 日统计金额、数量、订单数拆开在 SQL 端聚合，避免拆分支付或明细行把非金额指标放大。
        var orderCount = orderRows
            .Select(row => row.OrderGuid)
            .Where(orderGuid => !string.IsNullOrWhiteSpace(orderGuid))
            .Count();

        if (totalAmount == 0m && totalQuantity == 0 && orderCount == 0)
        {
            return null;
        }

        return new DailySalesStatistic
        {
            Date = targetDate,
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = orderCount,
            SkuCount = skuCodes.Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            CustomerCount = orderCount,
            AverageOrderValue = orderCount > 0 ? totalAmount / orderCount : 0m,
            UpdateTime = updateTime,
        };
    }

    private static Task ReplaceDailySalesStatisticAsync(
        SqlSugarContext context,
        ILogger logger,
        DateTime date,
        DailySalesStatistic? statistic) =>
        ExecuteTransactionSafelyAsync(
            beginAsync: () => context.Db.Ado.BeginTranAsync(),
            workAsync: async () =>
            {
                await context.Db.Deleteable<DailySalesStatistic>()
                    .Where(existing => existing.Date >= date.Date && existing.Date < date.Date.AddDays(1))
                    .ExecuteCommandAsync();
                if (statistic != null)
                    await context.Db.Insertable(statistic).ExecuteCommandAsync();
            },
            commitAsync: () => context.Db.Ado.CommitTranAsync(),
            rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
            logger: logger,
            operationName: "每日统计数据更新");

    /// <summary>
    /// 更新分时统计数据
    /// 按小时和分店维度聚合销售数据，包含全店汇总记录，按支付明细统计营业额
    /// </summary>
    /// <param name="date">目标日期</param>
    /// <param name="hour">指定小时，为空则更新全天24小时</param>
    public async Task UpdateHourlyStatistics(DateTime date, int? hour = null)
    {
        try
        {
            // 确定要更新的小时列表
            var targetHours = hour.HasValue
                ? new[] { hour.Value }
                : Enumerable.Range(0, 24).ToArray();
            var rangeStart = hour.HasValue ? date.Date.AddHours(hour.Value) : date.Date;
            var rangeEnd = hour.HasValue ? rangeStart.AddHours(1) : date.Date.AddDays(1);

            _logger.LogInformation(
                "开始更新分时统计数据: {Date}, 小时: {Hours}",
                date,
                hour.HasValue ? hour.Value.ToString() : "0-23"
            );

            // 金额取支付明细、销量取销售明细、订单数取订单头，避免拆分支付放大非金额指标。
            var hourlyRevenueRows = await _posmContext
                .Db.Queryable<PaymentDetail, SalesOrder>(
                    (pd, so) => pd.OrderGuid == so.OrderGuid
                )
                .Where(
                    (pd, so) =>
                        so.Status != null
                        && (so.Status == 1 || so.Status == 4)
                        && so.OrderTime != null
                        && so.OrderTime >= rangeStart
                        && so.OrderTime < rangeEnd
                )
                .GroupBy(
                    (pd, so) =>
                        new
                        {
                            Date = so.OrderTime!.Value.Date,
                            Hour = so.OrderTime!.Value.Hour,
                            so.BranchCode,
                            so.DeviceCode,
                        }
                )
                .Select(
                    (pd, so) =>
                        new HourlyStatisticSourceRow
                        {
                            Date = so.OrderTime!.Value.Date,
                            Hour = so.OrderTime!.Value.Hour,
                            BranchCode = so.BranchCode,
                            DeviceCode = so.DeviceCode,
                            TotalAmount = SqlFunc.AggregateSum(pd.Amount) ?? 0m,
                        }
                )
                .ToListAsync();

            var hourlyQuantityRows = await _posmContext
                .Db.Queryable<SalesOrderDetail, SalesOrder>(
                    (detail, so) => detail.OrderGuid == so.OrderGuid
                )
                .Where(
                    (detail, so) =>
                        so.Status != null
                        && (so.Status == 1 || so.Status == 4)
                        && so.OrderTime != null
                        && so.OrderTime >= rangeStart
                        && so.OrderTime < rangeEnd
                )
                .GroupBy(
                    (detail, so) =>
                        new
                        {
                            Date = so.OrderTime!.Value.Date,
                            Hour = so.OrderTime!.Value.Hour,
                            so.BranchCode,
                            so.DeviceCode,
                        }
                )
                .Select(
                    (detail, so) =>
                        new HourlyStatisticSourceRow
                        {
                            Date = so.OrderTime!.Value.Date,
                            Hour = so.OrderTime!.Value.Hour,
                            BranchCode = so.BranchCode,
                            DeviceCode = so.DeviceCode,
                            TotalQuantity = SqlFunc.AggregateSum(detail.Quantity) ?? 0,
                        }
                )
                .ToListAsync();

            var hourlyOrderRows = await _posmContext
                .Db.Queryable<SalesOrder>()
                .Where(
                    so =>
                        so.Status != null
                        && (so.Status == 1 || so.Status == 4)
                        && so.OrderTime != null
                        && so.OrderTime >= rangeStart
                        && so.OrderTime < rangeEnd
                )
                .GroupBy(
                    so =>
                        new
                        {
                            Date = so.OrderTime!.Value.Date,
                            Hour = so.OrderTime!.Value.Hour,
                            so.BranchCode,
                            so.DeviceCode,
                        }
                )
                .Select(
                    so =>
                        new HourlyStatisticSourceRow
                        {
                            Date = so.OrderTime!.Value.Date,
                            Hour = so.OrderTime!.Value.Hour,
                            BranchCode = so.BranchCode,
                            DeviceCode = so.DeviceCode,
                            OrderCount = SqlFunc.AggregateCount(so.OrderGuid),
                            CustomerCount = SqlFunc.AggregateCount(so.OrderGuid),
                        }
                )
                .ToListAsync();

            var combinedHourlyRows = hourlyRevenueRows
                .Concat(hourlyQuantityRows)
                .Concat(hourlyOrderRows)
                .ToList();
            var deviceBranchMap = await SalesStatisticsProductStoreDailySourceQueries.LoadDeviceBranchMapAsync(
                _posmContext,
                combinedHourlyRows.Where(row => string.IsNullOrWhiteSpace(row.BranchCode))
                    .Select(row => row.DeviceCode));
            var allHourlyData = combinedHourlyRows
                .Select(row => new
                {
                    Row = row,
                    BranchCode = SalesStatisticsCodeRules.ResolveBranchCode(
                        row.BranchCode,
                        row.DeviceCode,
                        deviceBranchMap),
                })
                .GroupBy(row => new { row.Row.Date, row.Row.Hour, row.BranchCode })
                .Select(group => new HourlyStatisticSourceRow
                {
                    Date = group.Key.Date.Date,
                    Hour = group.Key.Hour,
                    BranchCode = group.Key.BranchCode,
                    TotalAmount = group.Sum(row => row.Row.TotalAmount),
                    TotalQuantity = group.Sum(row => row.Row.TotalQuantity),
                    OrderCount = group.Sum(row => row.Row.OrderCount),
                    CustomerCount = group.Sum(row => row.Row.CustomerCount),
                })
                .ToList();

            if (!allHourlyData.Any())
            {
                _logger.LogInformation("没有找到销售数据: {Date}", date);
            }

            // 获取所有分店代码
            var branchCodes = allHourlyData
                .Select(d => d.BranchCode)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .ToList();

            // 查询分店信息
            var stores = await _context
                .Db.Queryable<Store>()
                .Where(s => branchCodes.Contains(s.StoreCode))
                .ToListAsync();

            var storeDict = stores.ToDictionary(s => s.StoreCode, s => s);

            var statisticsList = new List<HourlySalesStatistic>();

            // 为每个小时创建全店汇总记录
            foreach (var h in targetHours)
            {
                var hourlyDataForHour = allHourlyData.Where(d => d.Hour == h).ToList();

                if (hourlyDataForHour.Any())
                {
                    var allStoreData = new HourlySalesStatistic
                    {
                        Date = date,
                        Hour = h,
                        BranchCode = "ALL",
                        BranchName = "All Stores",
                        TotalAmount = hourlyDataForHour.Sum(d => d.TotalAmount),
                        TotalQuantity = (int)hourlyDataForHour.Sum(d => d.TotalQuantity),
                        OrderCount = hourlyDataForHour.Sum(d => d.OrderCount),
                        CustomerCount = hourlyDataForHour.Sum(d => d.CustomerCount),
                        AverageOrderValue =
                            hourlyDataForHour.Sum(d => d.OrderCount) > 0
                                ? hourlyDataForHour.Sum(d => d.TotalAmount)
                                    / hourlyDataForHour.Sum(d => d.OrderCount)
                                : 0m,
                        UpdateTime = DateTime.Now,
                    };
                    statisticsList.Add(allStoreData);
                }
            }

            LogSkippedBranchCodeRows(
                "分时分店销售统计",
                allHourlyData,
                data => data.BranchCode,
                data => data.TotalAmount,
                data => data.TotalQuantity
            );

            // 为每个分店创建分时统计记录
            foreach (var data in allHourlyData)
            {
                // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                if (string.IsNullOrWhiteSpace(data.BranchCode))
                    continue;
                var branchCode = data.BranchCode;
                var store = storeDict.GetValueOrDefault(branchCode);

                var storeStatistic = new HourlySalesStatistic
                {
                    Date = data.Date,
                    Hour = data.Hour,
                    BranchCode = branchCode,
                    BranchName = store?.StoreName ?? branchCode,
                    TotalAmount = data.TotalAmount,
                    TotalQuantity = (int)data.TotalQuantity,
                    OrderCount = data.OrderCount,
                    CustomerCount = data.CustomerCount,
                    AverageOrderValue =
                        data.OrderCount > 0 ? data.TotalAmount / data.OrderCount : 0m,
                    UpdateTime = DateTime.Now,
                };
                statisticsList.Add(storeStatistic);
            }

            await ExecuteTransactionSafelyAsync(
                beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                workAsync: async () =>
                {
                    // 删除指定日期和小时的旧记录
                    var deletedCount = await _context
                        .Db.Deleteable<HourlySalesStatistic>()
                        .Where(s => s.Date >= date.Date && s.Date < date.Date.AddDays(1) && targetHours.Contains(s.Hour))
                        .ExecuteCommandAsync();
                    _logger.LogInformation("删除 {Count} 条分时统计旧记录", deletedCount);

                    // 批量插入新记录
                    if (statisticsList.Any())
                        _context.Db.Fastest<HourlySalesStatistic>()
                            .PageSize(BatchSize)
                            .BulkCopy(statisticsList);
                },
                commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                logger: _logger,
                operationName: "分时统计数据更新"
            );

            _logger.LogInformation(
                "分时统计数据更新完成: {Date}, 小时: {Hours}, 总记录: {Total}",
                date,
                hour.HasValue ? hour.Value.ToString() : "0-23",
                statisticsList.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新分时统计数据失败: {Date} {Hour}", date, hour);
            throw;
        }
    }

    }
}
