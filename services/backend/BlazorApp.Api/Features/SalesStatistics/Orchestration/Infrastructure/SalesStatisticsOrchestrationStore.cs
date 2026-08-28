using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Services;

/// <summary>
/// 完整刷新编排切片的持久化适配器。所有 SqlSugar 查询、写入和事务均集中在此处。
/// </summary>
internal sealed class SalesStatisticsOrchestrationStore
{
    private const int BatchSize = 5000;
    private const string UnknownSupplierCode = "UNKNOWN";

    internal async Task<SalesStatisticRefreshState?> GetProductStoreDailyRefreshStateAsync(
        SqlSugarContext context,
        DateTime date)
    {
        // SqlSugar 的 FirstAsync 可返回空记录，但其泛型签名未标注可空；在此处 await 后如实暴露契约。
        return await context.Db
            .Queryable<SalesStatisticRefreshState>()
            .Where(state =>
                state.StatisticType == SalesStatisticType.ProductStoreDaily
                && state.Date == date.Date)
            .FirstAsync();
    }

    internal async Task UpsertDailySalesStatisticAsync(
        SqlSugarContext context,
        DailySalesStatistic statistic)
    {
        var existing = await context.Db.Queryable<DailySalesStatistic>()
            .Where(row => row.Date == statistic.Date)
            .FirstAsync();
        if (existing != null)
        {
            await context.Db.Updateable(statistic).ExecuteCommandAsync();
            return;
        }

        await context.Db.Insertable(statistic).ExecuteCommandAsync();
    }

    internal async Task ReplaceStoreStatisticsAsync(
        SqlSugarContext context,
        ILogger logger,
        DateTime targetDate,
        List<string>? branchCodes,
        List<StoreSalesStatistic> statisticsList)
    {
        var targetBranchCodes = SalesStatisticsCodeRules.NormalizeBranchCodes(branchCodes);
        await SalesStatisticsTransactionExecutor.ExecuteAsync(
            beginAsync: () => context.Db.Ado.BeginTranAsync(),
            workAsync: async () =>
            {
                var deleteable = context.Db.Deleteable<StoreSalesStatistic>()
                    .Where(row => row.Date == targetDate);
                if (targetBranchCodes.Any())
                {
                    deleteable = deleteable.Where(row =>
                        targetBranchCodes.Contains(row.BranchCode));
                }

                var deletedCount = await deleteable.ExecuteCommandAsync();
                logger.LogInformation("删除 {Count} 条分店统计旧记录", deletedCount);
                if (statisticsList.Any())
                {
                    context.Db.Fastest<StoreSalesStatistic>()
                        .PageSize(BatchSize)
                        .BulkCopy(statisticsList);
                }
            },
            commitAsync: () => context.Db.Ado.CommitTranAsync(),
            rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
            logger: logger,
            operationName: "并发分店统计数据更新"
        );
    }

    internal async Task UpdateHourlyStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime date,
            int? hour
        )
        {
            try
            {
                // 确定要更新的小时列表
                var targetHours = hour.HasValue
                    ? new[] { hour.Value }
                    : Enumerable.Range(0, 24).ToArray();
                var rangeStart = hour.HasValue ? date.Date.AddHours(hour.Value) : date.Date;
                var rangeEnd = hour.HasValue ? rangeStart.AddHours(1) : date.Date.AddDays(1);

                logger.LogInformation(
                    "开始更新分时统计数据: {Date}, 小时: {Hours}",
                    date,
                    hour.HasValue ? hour.Value.ToString() : "0-23"
                );

                // 金额取支付明细、销量取销售明细、订单数取订单头，避免拆分支付放大非金额指标。
                var hourlyRevenueRows = await posmContext
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
                            }
                    )
                    .Select(
                        (pd, so) =>
                            new HourlyStatisticSourceRow
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                TotalAmount = SqlFunc.AggregateSum(pd.Amount) ?? 0m,
                            }
                    )
                    .ToListAsync();

                var hourlyQuantityRows = await posmContext
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
                            }
                    )
                    .Select(
                        (detail, so) =>
                            new HourlyStatisticSourceRow
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                TotalQuantity = SqlFunc.AggregateSum(detail.Quantity) ?? 0,
                            }
                    )
                    .ToListAsync();

                var hourlyOrderRows = await posmContext
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
                            }
                    )
                    .Select(
                        so =>
                            new HourlyStatisticSourceRow
                            {
                                Date = so.OrderTime!.Value.Date,
                                Hour = so.OrderTime!.Value.Hour,
                                BranchCode = so.BranchCode,
                                OrderCount = SqlFunc.AggregateCount(so.OrderGuid),
                                CustomerCount = SqlFunc.AggregateCount(so.OrderGuid),
                            }
                    )
                    .ToListAsync();

                var allHourlyData = hourlyRevenueRows
                    .Concat(hourlyQuantityRows)
                    .Concat(hourlyOrderRows)
                    .GroupBy(row => new { row.Date, row.Hour, row.BranchCode })
                    .Select(group => new HourlyStatisticSourceRow
                    {
                        Date = group.Key.Date,
                        Hour = group.Key.Hour,
                        BranchCode = group.Key.BranchCode,
                        TotalAmount = group.Sum(row => row.TotalAmount),
                        TotalQuantity = group.Sum(row => row.TotalQuantity),
                        OrderCount = group.Sum(row => row.OrderCount),
                        CustomerCount = group.Sum(row => row.CustomerCount),
                    })
                    .ToList();

                if (!allHourlyData.Any())
                {
                    logger.LogInformation("没有找到销售数据: {Date}", date);
                    return;
                }

                // 获取所有分店代码
                var branchCodes = allHourlyData
                    .Select(d => d.BranchCode)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct()
                    .ToList();

                // 查询分店信息
                var stores = await context
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
                    logger,
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

                // 查询数据库中已存在的记录
                var existingRecords = await context
                    .Db.Queryable<HourlySalesStatistic>()
                    .Where(s => s.Date == date && targetHours.Contains(s.Hour))
                    .ToListAsync();

                // 构建已存在记录的字典，用于快速查找
                var existingDict = existingRecords.ToDictionary(
                    s => $"{s.Date}_{s.Hour}_{s.BranchCode}",
                    s => s
                );

                var toInsert = new List<HourlySalesStatistic>();
                var toUpdate = new List<HourlySalesStatistic>();

                // 遍历统计数据，区分插入和更新操作
                foreach (var stat in statisticsList)
                {
                    var key = $"{stat.Date}_{stat.Hour}_{stat.BranchCode}";

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        stat.Date = existing.Date;
                        stat.Hour = existing.Hour;
                        stat.BranchCode = existing.BranchCode;
                        toUpdate.Add(stat);
                    }
                    else
                    {
                        toInsert.Add(stat);
                    }
                }

                // 批量插入新记录
                if (toInsert.Any())
                {
                    context
                        .Db.Fastest<HourlySalesStatistic>()
                        .PageSize(BatchSize)
                        .BulkCopy(toInsert);
                    logger.LogInformation("批量插入 {Count} 条分时统计记录", toInsert.Count);
                }

                // 批量更新已存在记录
                if (toUpdate.Any())
                {
                    context
                        .Db.Fastest<HourlySalesStatistic>()
                        .PageSize(BatchSize)
                        .BulkUpdate(toUpdate);
                    logger.LogInformation("批量更新 {Count} 条分时统计记录", toUpdate.Count);
                }

                logger.LogInformation(
                    "分时统计数据更新完成: {Date}, 小时: {Hours}, 总记录: {Total}",
                    date,
                    hour.HasValue ? hour.Value.ToString() : "0-23",
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "更新分时统计数据失败: {Date} {Hour}", date, hour);
                throw;
            }
        }

    internal async Task UpdateStoreSupplierStatisticsWithContext(
            SqlSugarContext context,
            POSMSqlSugarContext posmContext,
            ILogger logger,
            DateTime? date,
            List<string>? branchCodes,
            List<string>? supplierCodes
        )
        {
            try
            {
                var targetDate = (date ?? DateTime.Now.Date).Date;
                var nextDate = targetDate.AddDays(1);
                var targetBranchCodes = SalesStatisticsCodeRules.NormalizeBranchCodes(branchCodes);
                var targetSupplierCodes = SalesStatisticsCodeRules.NormalizeSupplierCodes(
                    supplierCodes
                );

                logger.LogInformation(
                    "开始更新门店供应商统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                    targetDate,
                    branchCodes != null ? string.Join(", ", branchCodes) : "All",
                    supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
                );

                // 构建查询
                var query = posmContext
                    .Db.Queryable<SalesOrder>()
                    .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                    .LeftJoin<PosmProductSupplierMapping>(
                        (o, d, m) => d.ProductCode == m.ProductCode
                    )
                    .Where(o =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime >= targetDate
                        && o.OrderTime < nextDate
                    );

                // 设置分店过滤条件
                if (targetBranchCodes.Any())
                {
                    query = query.Where(o =>
                        (o.BranchCode != null && targetBranchCodes.Contains(o.BranchCode.Trim()))
                        || o.BranchCode == null
                        || o.BranchCode.Trim() == ""
                    );
                }

                // 设置供应商过滤条件
                if (targetSupplierCodes.Any())
                {
                    var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                    query = query.Where(
                        (o, d, m) =>
                            (
                                m.LocalSupplierCode != null
                                && targetSupplierCodes.Contains(m.LocalSupplierCode.Trim())
                            )
                            || (
                                m.ChinaSupplierCode != null
                                && targetSupplierCodes.Contains(m.ChinaSupplierCode.Trim())
                            )
                            || (d.SupplierCode != null && targetSupplierCodes.Contains(d.SupplierCode.Trim()))
                            || (
                                includesUnknownSupplier
                                && (m.LocalSupplierCode == null || m.LocalSupplierCode.Trim() == "")
                                && (d.SupplierCode == null || d.SupplierCode.Trim() == "")
                            )
                    );
                }

                // 查询销售明细后按最终供应商编码聚合，确保订单数按订单去重。
                var rawStoreSupplierData = await query
                    .Select(
                        (o, d, m) =>
                            new
                            {
                                Date = o.OrderTime!.Value.Date,
                                BranchCode = o.BranchCode,
                                DeviceCode = o.DeviceCode,
                                OrderGuid = o.OrderGuid,
                                DetailSupplierCode = d.SupplierCode,
                                LocalSupplierCode = m.LocalSupplierCode,
                                ChinaSupplierCode = m.ChinaSupplierCode,
                                ActualAmount = d.ActualAmount ?? 0m,
                                Quantity = d.Quantity ?? 0m,
                            }
                    )
                    .ToListAsync();
                var orderAmountMaps = await SalesStatisticsProductStoreDailySourceQueries
                    .LoadOrderAmountMapsAsync(
                    posmContext,
                    targetDate,
                    nextDate,
                    rawStoreSupplierData,
                    row => row.OrderGuid,
                    row => row.ActualAmount
                );
                var deviceBranchMap = await SalesStatisticsProductStoreDailySourceQueries
                    .LoadDeviceBranchMapAsync(
                    posmContext,
                    rawStoreSupplierData
                        .Where(row => string.IsNullOrWhiteSpace(row.BranchCode))
                        .Select(row => row.DeviceCode)
                );
                var storeSupplierData = rawStoreSupplierData
                    .Select(row => new StoreSupplierSourceRow
                    {
                        Date = row.Date,
                        BranchCode = SalesStatisticsCodeRules.ResolveBranchCode(
                            row.BranchCode,
                            row.DeviceCode,
                            deviceBranchMap
                        ),
                        DeviceCode = row.DeviceCode,
                        OrderGuid = row.OrderGuid,
                        DetailSupplierCode = row.DetailSupplierCode,
                        LocalSupplierCode = row.LocalSupplierCode,
                        ChinaSupplierCode = row.ChinaSupplierCode,
                        ActualAmount = SalesStatisticsProductStoreDailyDomainRules
                            .ResolveStatisticAmount(
                            row.OrderGuid,
                            row.ActualAmount,
                            orderAmountMaps.PaymentAmounts,
                            orderAmountMaps.DetailAmounts
                        ),
                        Quantity = row.Quantity,
                    })
                    .Where(row =>
                        !targetBranchCodes.Any()
                        || targetBranchCodes.Contains(row.BranchCode ?? string.Empty)
                    )
                    .ToList();

                // 获取所有本地供应商代码
                var allLocalSupplierCodes = storeSupplierData
                    .Select(d =>
                        !string.IsNullOrWhiteSpace(d.LocalSupplierCode)
                            ? d.LocalSupplierCode!.Trim()
                            : d.DetailSupplierCode?.Trim()
                    )
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .Distinct()
                    .ToList();

                // 查询本地供应商信息
                var localSupplierDict = new Dictionary<string, HBLocalSupplier>();
                if (allLocalSupplierCodes.Any())
                {
                    var localSuppliers = await context.HBLocalSupplierDb.GetListAsync(s =>
                        s.LocalSupplierCode != null
                        && allLocalSupplierCodes.Contains(s.LocalSupplierCode)
                        && !s.IsDeleted
                    );
                    localSupplierDict = localSuppliers.ToDictionary(
                        s => s.LocalSupplierCode!,
                        s => s
                    );
                }

                // 获取所有国内供应商代码
                var allChinaSupplierCodes = storeSupplierData
                    .Where(d => !string.IsNullOrEmpty(d.ChinaSupplierCode))
                    .Select(d => d.ChinaSupplierCode!)
                    .Distinct()
                    .ToList();

                // 查询国内供应商信息
                var chinaSupplierDict = new Dictionary<string, ChinaSupplier>();
                if (allChinaSupplierCodes.Any())
                {
                    var chinaSuppliers = await context.ChinaSupplierDb.GetListAsync(cs =>
                        cs.SupplierCode != null
                        && allChinaSupplierCodes.Contains(cs.SupplierCode)
                        && !cs.IsDeleted
                    );
                    chinaSupplierDict = chinaSuppliers
                        .Where(cs => !string.IsNullOrEmpty(cs.SupplierCode))
                        .ToDictionary(cs => cs.SupplierCode!, cs => cs);
                }

                LogSkippedBranchCodeRows(
                    logger,
                    "分店供应商销售统计",
                    storeSupplierData,
                    data => data.BranchCode,
                    data => data.ActualAmount,
                    data => data.Quantity
                );

                var statisticsList = SalesStatisticsStoreSupplierSlice
                    .BuildStoreSupplierSalesDetails(
                    storeSupplierData,
                    localSupplierDict,
                    chinaSupplierDict,
                    DateTime.Now
                );

                await SalesStatisticsTransactionExecutor.ExecuteAsync(
                    beginAsync: () => context.Db.Ado.BeginTranAsync(),
                    workAsync: async () =>
                    {
                        // 并发路径也按本次影响范围重建，避免旧供应商统计残留。
                        var deleteable = context.Db.Deleteable<StoreSupplierSalesDetail>()
                            .Where(s => s.Date == targetDate);
                        if (targetBranchCodes.Any())
                        {
                            deleteable = deleteable.Where(s => targetBranchCodes.Contains(s.BranchCode));
                        }
                        if (targetSupplierCodes.Any())
                        {
                            var deleteSupplierCodes = new List<string>();
                            if (targetSupplierCodes.Contains("200"))
                            {
                                var existingDomesticQuery = context.Db.Queryable<StoreSupplierSalesDetail>()
                                    .Where(s => s.Date == targetDate && s.IsDomestic == true);
                                if (targetBranchCodes.Any())
                                {
                                    existingDomesticQuery = existingDomesticQuery.Where(s =>
                                        targetBranchCodes.Contains(s.BranchCode)
                                    );
                                }

                                var existingDomesticSupplierCodes = await existingDomesticQuery
                                    .Select(s => s.SupplierCode)
                                    .Distinct()
                                    .ToListAsync();
                                deleteSupplierCodes.AddRange(existingDomesticSupplierCodes);
                            }

                            deleteSupplierCodes = deleteSupplierCodes
                                .Concat(targetSupplierCodes)
                                .Concat(statisticsList.Select(s => s.SupplierCode))
                                .Where(code => !string.IsNullOrWhiteSpace(code))
                                .Select(code => code.Trim())
                                .Distinct()
                                .ToList();
                            var includesUnknownSupplier = targetSupplierCodes.Contains(UnknownSupplierCode);
                            deleteable = deleteable.Where(s =>
                                deleteSupplierCodes.Contains(s.SupplierCode.Trim())
                                || (
                                    includesUnknownSupplier
                                    && (s.SupplierCode == null || s.SupplierCode.Trim() == "")
                                )
                            );
                        }

                        var deletedCount = await deleteable.ExecuteCommandAsync();
                        logger.LogInformation("删除 {Count} 条门店供应商统计旧记录", deletedCount);

                        if (!statisticsList.Any())
                        {
                            logger.LogInformation("没有找到门店供应商统计数据: {Date}", targetDate);
                            return;
                        }

                        context
                            .Db.Fastest<StoreSupplierSalesDetail>()
                            .PageSize(BatchSize)
                            .BulkCopy(statisticsList);
                        logger.LogInformation("批量插入 {Count} 条门店供应商统计记录", statisticsList.Count);
                    },
                    commitAsync: () => context.Db.Ado.CommitTranAsync(),
                    rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
                    logger: logger,
                    operationName: "门店供应商统计数据更新"
                );

                logger.LogInformation(
                    "门店供应商统计数据更新完成: {Date}, 总记录: {Total}",
                    targetDate,
                    statisticsList.Count
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "更新门店供应商统计数据失败: {Date}", date);
                throw;
            }
        }

    private static void LogSkippedBranchCodeRows<T>(
        ILogger logger,
        string statisticName,
        IEnumerable<T> rows,
        Func<T, string?> branchCodeSelector,
        Func<T, decimal> amountSelector,
        Func<T, decimal> quantitySelector)
    {
        var skippedRows = rows
            .Where(row => string.IsNullOrWhiteSpace(branchCodeSelector(row)))
            .ToList();
        if (skippedRows.Count == 0)
        {
            return;
        }

        logger.LogWarning(
            "{StatisticName} 跳过 {Count} 条缺少分店编码的销售记录，金额合计 {Amount}，数量合计 {Quantity}",
            statisticName,
            skippedRows.Count,
            skippedRows.Sum(amountSelector),
            skippedRows.Sum(quantitySelector));
    }
}
