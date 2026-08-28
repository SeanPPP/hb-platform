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
    /// <summary>销售统计垂直切片：SalesStatisticsSupplierStoreSlice。</summary>
    internal sealed class SalesStatisticsSupplierStoreSlice : SalesStatisticsSliceBase
    {
        public SalesStatisticsSupplierStoreSlice(SalesStatisticsSliceContext shared)
            : base(shared) { }

    public async Task UpdateAustralianSupplierStoreStatistics(
        DateTime? date = null,
        List<string>? branchCodes = null,
        List<string>? supplierCodes = null
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

            _logger.LogInformation(
                "开始更新澳洲供应商门店统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                targetDate,
                branchCodes != null ? string.Join(", ", branchCodes) : "All",
                supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
            );

            // 构建查询
            var query = _posmContext
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
                            d.SupplierCode != null
                            && targetSupplierCodes.Contains(d.SupplierCode.Trim())
                        )
                        || (
                            includesUnknownSupplier
                            && (m.LocalSupplierCode == null || m.LocalSupplierCode.Trim() == "")
                            && (d.SupplierCode == null || d.SupplierCode.Trim() == "")
                        )
                );
            }

            // 查询销售明细后按最终供应商编码聚合，避免空映射在写表时形成重复主键。
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
                            ActualAmount = d.ActualAmount ?? 0m,
                            Quantity = d.Quantity ?? 0m,
                        }
                )
                .ToListAsync();
            var (paymentAmounts, detailAmounts) = await SalesStatisticsProductStoreDailySourceQueries
                .LoadOrderAmountMapsAsync(
                _posmContext,
                targetDate,
                nextDate,
                rawStoreSupplierData,
                row => row.OrderGuid,
                row => row.ActualAmount
            );
            var deviceBranchMap = await SalesStatisticsProductStoreDailySourceQueries
                .LoadDeviceBranchMapAsync(
                _posmContext,
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
                    ActualAmount = SalesStatisticsProductStoreDailyDomainRules.ResolveStatisticAmount(
                        row.OrderGuid,
                        row.ActualAmount,
                        paymentAmounts,
                        detailAmounts
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
                var localSuppliers = await _context.HBLocalSupplierDb.GetListAsync(s =>
                    s.LocalSupplierCode != null
                    && allLocalSupplierCodes.Contains(s.LocalSupplierCode)
                    && !s.IsDeleted
                );
                localSupplierDict = localSuppliers.ToDictionary(
                    s => s.LocalSupplierCode!,
                    s => s
                );
            }

            LogSkippedBranchCodeRows(
                "澳洲供应商分店销售统计",
                storeSupplierData,
                data => data.BranchCode,
                data => data.ActualAmount,
                data => data.Quantity
            );

            var statisticsList = SalesStatisticsStoreSupplierSlice
                .BuildAustralianSupplierStoreSalesDetails(
                storeSupplierData,
                localSupplierDict,
                DateTime.Now
            );

            await SalesStatisticsTransactionExecutor.ExecuteAsync(
                beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                workAsync: async () =>
                {
                    // 普通路径也按本次影响范围清理，避免局部供应商刷新误删同日其它供应商。
                    var deleteable = _context
                        .Db.Deleteable<AustralianSupplierStoreSalesDetail>()
                        .Where(s => s.Date == targetDate);

                    if (targetBranchCodes.Any())
                    {
                        deleteable = deleteable.Where(s =>
                            targetBranchCodes.Contains(s.BranchCode.Trim())
                        );
                    }

                    if (targetSupplierCodes.Any())
                    {
                        var deleteSupplierCodes = targetSupplierCodes
                            .Concat(statisticsList.Select(s => s.SupplierCode))
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Select(code => code.Trim())
                            .Distinct()
                            .ToList();
                        deleteable = deleteable.Where(s =>
                            // 局部补算也要清理历史空白供应商，避免新回退编码旁边残留旧空主键数据。
                            s.SupplierCode == null
                            || s.SupplierCode.Trim() == ""
                            || deleteSupplierCodes.Contains(s.SupplierCode.Trim())
                        );
                    }

                    var deletedCount = await deleteable.ExecuteCommandAsync();
                    _logger.LogInformation("删除 {Count} 条澳洲供应商门店统计旧记录", deletedCount);

                    if (!statisticsList.Any())
                    {
                        _logger.LogInformation("没有找到澳洲供应商门店数据: {Date}", targetDate);
                        return;
                    }

                    _context
                        .Db.Fastest<AustralianSupplierStoreSalesDetail>()
                        .PageSize(BatchSize)
                        .BulkCopy(statisticsList);
                },
                commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                logger: _logger,
                operationName: "澳洲供应商门店统计数据更新"
            );

            _logger.LogInformation(
                "澳洲供应商门店统计数据更新完成: {Date}, 总记录: {Total}",
                targetDate,
                statisticsList.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新澳洲供应商门店统计数据失败: {Date}", date);
            throw;
        }
    }

    /// <summary>
    /// 更新中国供应商门店统计数据
    /// </summary>
    /// <param name="date">目标日期，默认为当前日期</param>
    /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
    /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
    public async Task UpdateChinaSupplierStoreStatistics(
        DateTime? date = null,
        List<string>? branchCodes = null,
        List<string>? supplierCodes = null
    )
    {
        try
        {
            var targetDate = date ?? DateTime.Now.Date;

            _logger.LogInformation(
                "开始更新中国供应商门店统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                targetDate,
                branchCodes != null ? string.Join(", ", branchCodes) : "All",
                supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
            );

            // 构建查询（只查询LocalSupplierCode为"200"的记录，即国内供应商记录）
            var query = _posmContext
                .Db.Queryable<SalesOrder>()
                .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                .LeftJoin<PosmProductSupplierMapping>(
                    (o, d, m) => d.ProductCode == m.ProductCode
                )
                .Where(
                    (o, d, m) =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime.Value.Date == targetDate
                        && m.LocalSupplierCode == "200"
                );

            // 设置分店过滤条件
            if (branchCodes != null && branchCodes.Any())
            {
                query = query.Where(o =>
                    o.BranchCode != null && branchCodes.Contains(o.BranchCode)
                );
            }

            // 设置供应商过滤条件（按国内供应商代码）
            if (supplierCodes != null && supplierCodes.Any())
            {
                query = query.Where(
                    (o, d, m) =>
                        m.ChinaSupplierCode != null
                        && supplierCodes.Contains(m.ChinaSupplierCode)
                );
            }

            // 查询并聚合中国供应商门店销售数据
            var storeSupplierData = await query
                .GroupBy(
                    (o, d, m) =>
                        new
                        {
                            Date = o.OrderTime!.Value.Date,
                            BranchCode = o.BranchCode!,
                            ChinaSupplierCode = m.ChinaSupplierCode,
                        }
                )
                .Select(
                    (o, d, m) =>
                        new
                        {
                            Date = o.OrderTime!.Value.Date,
                            BranchCode = o.BranchCode!,
                            ChinaSupplierCode = m.ChinaSupplierCode,
                            TotalAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0m,
                            TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0m),
                            OrderCount = SqlFunc.AggregateCount(o.OrderGuid),
                        }
                )
                .ToListAsync();

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
                var chinaSuppliers = await _context.ChinaSupplierDb.GetListAsync(cs =>
                    cs.SupplierCode != null
                    && allChinaSupplierCodes.Contains(cs.SupplierCode)
                    && !cs.IsDeleted
                );
                chinaSupplierDict = chinaSuppliers
                    .Where(cs => !string.IsNullOrEmpty(cs.SupplierCode))
                    .ToDictionary(cs => cs.SupplierCode!, cs => cs);
            }

            var statisticsList = new List<ChinaSupplierStoreSalesDetail>();

            LogSkippedBranchCodeRows(
                "中国供应商分店销售统计",
                storeSupplierData,
                data => data.BranchCode,
                data => data.TotalAmount,
                data => data.TotalQuantity
            );

            // 构建每个中国供应商门店的统计记录
            foreach (var data in storeSupplierData)
            {
                // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                if (string.IsNullOrWhiteSpace(data.BranchCode))
                    continue;
                var branchCode = data.BranchCode;
                var chinaSupplierCode = data.ChinaSupplierCode ?? string.Empty;

                if (string.IsNullOrEmpty(chinaSupplierCode))
                    continue;

                var supplierCode = chinaSupplierCode;
                var supplierName = chinaSupplierCode;

                // 获取供应商名称
                if (chinaSupplierDict.TryGetValue(chinaSupplierCode, out var cs))
                {
                    supplierName = cs.SupplierName ?? supplierCode;
                }

                var statistic = new ChinaSupplierStoreSalesDetail
                {
                    Date = data.Date,
                    BranchCode = branchCode,
                    SupplierCode = supplierCode,
                    SupplierName = supplierName,
                    TotalAmount = data.TotalAmount,
                    TotalQuantity = (int)data.TotalQuantity,
                    OrderCount = data.OrderCount,
                    UpdateTime = DateTime.Now,
                };

                statisticsList.Add(statistic);
            }

            // 如果没有数据则返回
            if (!statisticsList.Any())
            {
                _logger.LogInformation("没有找到中国供应商门店数据: {Date}", targetDate);
                return;
            }

            await SalesStatisticsTransactionExecutor.ExecuteAsync(
                beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                workAsync: async () =>
                {
                    // 按日期删除该日期的所有旧数据
                    var deletedCount = await _context
                        .Db.Deleteable<ChinaSupplierStoreSalesDetail>()
                        .Where(s => s.Date == targetDate)
                        .ExecuteCommandAsync();
                    _logger.LogInformation("删除 {Count} 条中国供应商门店统计旧记录", deletedCount);

                    // 批量插入新记录
                    _context
                        .Db.Fastest<ChinaSupplierStoreSalesDetail>()
                        .PageSize(BatchSize)
                        .BulkCopy(statisticsList);
                },
                commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                logger: _logger,
                operationName: "中国供应商门店统计数据更新"
            );

            _logger.LogInformation(
                "中国供应商门店统计数据更新完成: {Date}, 总记录: {Total}",
                targetDate,
                statisticsList.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新中国供应商门店统计数据失败: {Date}", date);
            throw;
        }
    }

    /// <summary>
    /// 更新澳洲供应商门店统计数据（并发上下文版本）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="posmContext">POSM数据库上下文</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="date">目标日期，默认为当前日期</param>
    /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
    /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
    internal static async Task UpdateAustralianSupplierStoreStatisticsWithContext(
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
                "开始更新澳洲供应商门店统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                targetDate,
                branchCodes != null ? string.Join(", ", branchCodes) : "All",
                supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
            );

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

            if (targetBranchCodes.Any())
            {
                query = query.Where(o =>
                    (o.BranchCode != null && targetBranchCodes.Contains(o.BranchCode.Trim()))
                    || o.BranchCode == null
                    || o.BranchCode.Trim() == ""
                );
            }

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
                            d.SupplierCode != null
                            && targetSupplierCodes.Contains(d.SupplierCode.Trim())
                        )
                        || (
                            includesUnknownSupplier
                            && (m.LocalSupplierCode == null || m.LocalSupplierCode.Trim() == "")
                            && (d.SupplierCode == null || d.SupplierCode.Trim() == "")
                        )
                );
            }

            // 查询销售明细后按最终供应商编码聚合，避免空映射在写表时形成重复主键。
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
                            ActualAmount = d.ActualAmount ?? 0m,
                            Quantity = d.Quantity ?? 0m,
                        }
                )
                .ToListAsync();
            var (paymentAmounts, detailAmounts) = await SalesStatisticsProductStoreDailySourceQueries
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
                    ActualAmount = SalesStatisticsProductStoreDailyDomainRules.ResolveStatisticAmount(
                        row.OrderGuid,
                        row.ActualAmount,
                        paymentAmounts,
                        detailAmounts
                    ),
                    Quantity = row.Quantity,
                })
                .Where(row =>
                    !targetBranchCodes.Any()
                    || targetBranchCodes.Contains(row.BranchCode ?? string.Empty)
                )
                .ToList();

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

            LogSkippedBranchCodeRows(
                logger,
                "澳洲供应商分店销售统计",
                storeSupplierData,
                data => data.BranchCode,
                data => data.ActualAmount,
                data => data.Quantity
            );

            var statisticsList = SalesStatisticsStoreSupplierSlice
                .BuildAustralianSupplierStoreSalesDetails(
                storeSupplierData,
                localSupplierDict,
                DateTime.Now
            );

            await SalesStatisticsTransactionExecutor.ExecuteAsync(
                beginAsync: () => context.Db.Ado.BeginTranAsync(),
                workAsync: async () =>
                {
                    // 并发补算路径也先清理本次影响范围，避免旧空供应商主键在新 UNKNOWN 旁边残留。
                    var deleteable = context
                        .Db.Deleteable<AustralianSupplierStoreSalesDetail>()
                        .Where(s => s.Date == targetDate);

                    if (targetBranchCodes.Any())
                    {
                        deleteable = deleteable.Where(s =>
                            targetBranchCodes.Contains(s.BranchCode.Trim())
                        );
                    }

                    if (targetSupplierCodes.Any())
                    {
                        var deleteSupplierCodes = targetSupplierCodes
                            .Concat(statisticsList.Select(s => s.SupplierCode))
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Select(code => code.Trim())
                            .Distinct()
                            .ToList();
                        deleteable = deleteable.Where(s =>
                            // 局部补算也要清理历史空白供应商，避免新回退编码旁边残留旧空主键数据。
                            s.SupplierCode == null
                            || s.SupplierCode.Trim() == ""
                            || deleteSupplierCodes.Contains(s.SupplierCode.Trim())
                        );
                    }

                    var deletedCount = await deleteable.ExecuteCommandAsync();
                    logger.LogInformation("删除 {Count} 条澳洲供应商门店统计旧记录", deletedCount);

                    if (!statisticsList.Any())
                    {
                        logger.LogInformation("没有找到澳洲供应商门店数据: {Date}", targetDate);
                        return;
                    }

                    context
                        .Db.Fastest<AustralianSupplierStoreSalesDetail>()
                        .PageSize(BatchSize)
                        .BulkCopy(statisticsList);
                    logger.LogInformation(
                        "批量插入 {Count} 条澳洲供应商门店统计记录",
                        statisticsList.Count
                    );
                },
                commitAsync: () => context.Db.Ado.CommitTranAsync(),
                rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
                logger: logger,
                operationName: "澳洲供应商门店统计数据更新"
            );

            logger.LogInformation(
                "澳洲供应商门店统计数据更新完成: {Date}, 总记录: {Total}",
                targetDate,
                statisticsList.Count
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新澳洲供应商门店统计数据失败: {Date}", date);
            throw;
        }
    }

    /// <summary>
    /// 更新中国供应商门店统计数据（并发上下文版本）
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="posmContext">POSM数据库上下文</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="date">目标日期，默认为当前日期</param>
    /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
    /// <param name="supplierCodes">供应商代码列表，为空则更新所有供应商</param>
    internal static async Task UpdateChinaSupplierStoreStatisticsWithContext(
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
            var targetDate = date ?? DateTime.Now.Date;

            logger.LogInformation(
                "开始更新中国供应商门店统计数据: {Date}, 分店: {Branches}, 供应商: {Suppliers}",
                targetDate,
                branchCodes != null ? string.Join(", ", branchCodes) : "All",
                supplierCodes != null ? string.Join(", ", supplierCodes) : "All"
            );

            var query = posmContext
                .Db.Queryable<SalesOrder>()
                .LeftJoin<SalesOrderDetail>((o, d) => o.OrderGuid == d.OrderGuid)
                .LeftJoin<PosmProductSupplierMapping>(
                    (o, d, m) => d.ProductCode == m.ProductCode
                )
                .Where(
                    (o, d, m) =>
                        o.Status != null
                        && (o.Status == 1 || o.Status == 4)
                        && o.OrderTime != null
                        && o.OrderTime.Value.Date == targetDate
                        && m.LocalSupplierCode == "200"
                );

            if (branchCodes != null && branchCodes.Any())
            {
                query = query.Where(o =>
                    o.BranchCode != null && branchCodes.Contains(o.BranchCode)
                );
            }

            if (supplierCodes != null && supplierCodes.Any())
            {
                query = query.Where(
                    (o, d, m) =>
                        m.ChinaSupplierCode != null
                        && supplierCodes.Contains(m.ChinaSupplierCode)
                );
            }

            var storeSupplierData = await query
                .GroupBy(
                    (o, d, m) =>
                        new
                        {
                            Date = o.OrderTime!.Value.Date,
                            BranchCode = o.BranchCode!,
                            ChinaSupplierCode = m.ChinaSupplierCode,
                        }
                )
                .Select(
                    (o, d, m) =>
                        new
                        {
                            Date = o.OrderTime!.Value.Date,
                            BranchCode = o.BranchCode!,
                            ChinaSupplierCode = m.ChinaSupplierCode,
                            TotalAmount = SqlFunc.AggregateSum(d.ActualAmount) ?? 0m,
                            TotalQuantity = SqlFunc.AggregateSum(d.Quantity ?? 0m),
                            OrderCount = SqlFunc.AggregateCount(o.OrderGuid),
                        }
                )
                .ToListAsync();

            var allChinaSupplierCodes = storeSupplierData
                .Where(d => !string.IsNullOrEmpty(d.ChinaSupplierCode))
                .Select(d => d.ChinaSupplierCode!)
                .Distinct()
                .ToList();

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

            var statisticsList = new List<ChinaSupplierStoreSalesDetail>();

            LogSkippedBranchCodeRows(
                logger,
                "中国供应商分店销售统计",
                storeSupplierData,
                data => data.BranchCode,
                data => data.TotalAmount,
                data => data.TotalQuantity
            );

            // 构建每个中国供应商门店的统计记录
            foreach (var data in storeSupplierData)
            {
                // 分店维度统计必须有有效分店编码，避免把空编码写入统计表。
                if (string.IsNullOrWhiteSpace(data.BranchCode))
                    continue;
                var branchCode = data.BranchCode;
                var chinaSupplierCode = data.ChinaSupplierCode ?? string.Empty;

                if (string.IsNullOrEmpty(chinaSupplierCode))
                    continue;

                var supplierCode = chinaSupplierCode;
                var supplierName = chinaSupplierCode;

                // 获取供应商名称
                if (chinaSupplierDict.TryGetValue(chinaSupplierCode, out var cs))
                {
                    supplierName = cs.SupplierName ?? supplierCode;
                }

                var statistic = new ChinaSupplierStoreSalesDetail
                {
                    Date = data.Date,
                    BranchCode = branchCode,
                    SupplierCode = supplierCode,
                    SupplierName = supplierName,
                    TotalAmount = data.TotalAmount,
                    TotalQuantity = (int)data.TotalQuantity,
                    OrderCount = data.OrderCount,
                    UpdateTime = DateTime.Now,
                };

                statisticsList.Add(statistic);
            }

            // 如果没有数据则返回
            if (!statisticsList.Any())
            {
                logger.LogInformation("没有找到中国供应商门店数据: {Date}", targetDate);
                return;
            }

            // 获取所有分店和供应商代码
            var allBranchCodes = statisticsList.Select(s => s.BranchCode).Distinct().ToList();
            var allSupplierCodes = statisticsList
                .Select(s => s.SupplierCode)
                .Distinct()
                .ToList();

            // 查询数据库中已存在的记录
            var existingRecords = await context
                .Db.Queryable<ChinaSupplierStoreSalesDetail>()
                .Where(s =>
                    s.Date == targetDate
                    && allBranchCodes.Contains(s.BranchCode)
                    && allSupplierCodes.Contains(s.SupplierCode)
                )
                .ToListAsync();

            // 构建已存在记录的字典，用于快速查找
            var existingDict = existingRecords.ToDictionary(
                s => $"{s.Date}_{s.BranchCode}_{s.SupplierCode}",
                s => s
            );

            var toInsert = new List<ChinaSupplierStoreSalesDetail>();
            var toUpdate = new List<ChinaSupplierStoreSalesDetail>();

            // 遍历统计数据，区分插入和更新操作
            foreach (var stat in statisticsList)
            {
                var key = $"{stat.Date}_{stat.BranchCode}_{stat.SupplierCode}";

                if (existingDict.TryGetValue(key, out var existing))
                {
                    // 记录已存在，更新字段值
                    existing.SupplierName = stat.SupplierName;
                    existing.TotalAmount = stat.TotalAmount;
                    existing.TotalQuantity = stat.TotalQuantity;
                    existing.OrderCount = stat.OrderCount;
                    existing.UpdateTime = stat.UpdateTime;
                    toUpdate.Add(existing);
                }
                else
                {
                    // 新记录，加入插入列表
                    toInsert.Add(stat);
                }
            }

            // 批量插入新记录
            if (toInsert.Any())
            {
                context
                    .Db.Fastest<ChinaSupplierStoreSalesDetail>()
                    .PageSize(BatchSize)
                    .BulkCopy(toInsert);
                logger.LogInformation(
                    "批量插入 {Count} 条中国供应商门店统计记录",
                    toInsert.Count
                );
            }

            // 批量更新已存在记录
            if (toUpdate.Any())
            {
                context
                    .Db.Fastest<ChinaSupplierStoreSalesDetail>()
                    .PageSize(BatchSize)
                    .BulkUpdate(toUpdate);
                logger.LogInformation(
                    "批量更新 {Count} 条中国供应商门店统计记录",
                    toUpdate.Count
                );
            }

            logger.LogInformation(
                "中国供应商门店统计数据更新完成: {Date}, 总记录: {Total}",
                targetDate,
                statisticsList.Count
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新中国供应商门店统计数据失败: {Date}", date);
            throw;
        }
    }

    private static void LogSkippedBranchCodeRows<T>(
        ILogger logger,
        string statisticName,
        IEnumerable<T> rows,
        Func<T, string?> branchCodeSelector,
        Func<T, decimal> amountSelector,
        Func<T, decimal> quantitySelector
    )
    {
        var skippedRows = rows
            .Where(row => string.IsNullOrWhiteSpace(branchCodeSelector(row)))
            .ToList();
        if (skippedRows.Count == 0)
        {
            return;
        }

        // 与实例路径使用完全相同的空分店过滤口径，静态上下文只显式传入日志器。
        logger.LogWarning(
            "{StatisticName} 跳过 {Count} 条缺少分店编码的销售记录，金额合计 {Amount}，数量合计 {Quantity}",
            statisticName,
            skippedRows.Count,
            skippedRows.Sum(amountSelector),
            skippedRows.Sum(quantitySelector)
        );
    }
}
}
