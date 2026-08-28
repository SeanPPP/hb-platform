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
    /// <summary>销售统计垂直切片：SalesStatisticsStoreDailySlice。</summary>
    internal sealed class SalesStatisticsStoreDailySlice : SalesStatisticsSliceBase
    {
        private readonly SalesStatisticsProductStoreDailyRefreshSlice _productRefresh;
        private readonly SalesStatisticsProductStoreDailySupportSlice _productSupport;
        private readonly SalesStatisticsOrchestrationSlice _orchestration;

        public SalesStatisticsStoreDailySlice(
            SalesStatisticsSliceContext shared,
            SalesStatisticsProductStoreDailyRefreshSlice productRefresh,
            SalesStatisticsProductStoreDailySupportSlice productSupport,
            SalesStatisticsOrchestrationSlice orchestration)
            : base(shared)
        {
            _productRefresh = productRefresh;
            _productSupport = productSupport;
            _orchestration = orchestration;
        }

    public async Task UpdateStoreStatistics(DateTime? date = null)
    {
        var targetDate = (date ?? DateTime.Now).Date;
        try
        {
            _logger.LogInformation("开始更新分店统计数据: {Date}", targetDate);

            if (targetDate.Year == 2025)
            {
                // 2025 的分店与商品统计来自双来源，必须在同一事务内同时替换。
                await _productRefresh.Update2025StoreAndProductStatisticsAtomically(
                    _context,
                    _posmContext,
                    _hbSalesContext,
                    _logger,
                    targetDate
                );
                return;
            }

            var statisticsList = await _productSupport.BuildStoreStatisticsAsync(
                _context,
                _posmContext,
                GetHBSalesContextFor2025(targetDate),
                targetDate,
                null
            );

            await SalesStatisticsTransactionExecutor.ExecuteAsync(
                beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                workAsync: async () =>
                {
                    // 删除该日期的所有旧记录
                    var deletedCount = await _context
                        .Db.Deleteable<StoreSalesStatistic>()
                        .Where(s => s.Date == targetDate)
                        .ExecuteCommandAsync();
                    _logger.LogInformation("删除 {Count} 条分店统计旧记录", deletedCount);

                    // 批量插入新记录
                    if (statisticsList.Any())
                    {
                        _context
                            .Db.Fastest<StoreSalesStatistic>()
                            .PageSize(BatchSize)
                            .BulkCopy(statisticsList);
                    }
                },
                commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                logger: _logger,
                operationName: "分店统计数据更新"
            );

            _logger.LogInformation(
                "分店统计数据更新完成: {Date}, 总记录: {Total}",
                targetDate,
                statisticsList.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新分店统计数据失败: {Date}", targetDate);
            throw;
        }
    }

    /// <summary>
    /// 全量刷新前一天数据
    /// 刷新前一天的每日统计、分时统计、分店统计和供应商统计
    /// </summary>
    public async Task FullRefreshPreviousDay()
    {
        try
        {
            var previousDay = DateTime.Now.AddDays(-1).Date;

            _logger.LogInformation("开始全量刷新前一天数据: {Date}", previousDay);

            // 全量刷新统一走带数据库租约的入口，保证 8 张日级统计表口径一致且跨实例不重复跑。
            await RunLeasedFullRefreshForSingleDateAsync(previousDay, "前一天");

            _logger.LogInformation("前一天数据全量刷新完成: {Date}", previousDay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "全量刷新前一天数据失败");
            throw;
        }
    }

    /// <summary>
    /// 全量刷新当天数据
    /// 刷新当天的每日统计、分时统计、分店统计和供应商统计
    /// </summary>
    public async Task FullRefreshCurrentDay()
    {
        try
        {
            var currentDay = DateTime.Now.Date;

            _logger.LogInformation("开始全量刷新当天数据: {Date}", currentDay);

            // 当天主刷新也复用带数据库租约的完整路径，避免和手动补算抢同一天。
            var refreshed = await RunLeasedFullRefreshForSingleDateAsync(currentDay, "当天");
            if (!refreshed)
            {
                return;
            }

            // POSM 可能延迟上传，商品统计额外滚动补算最近 7 天。
            for (var offset = 1; offset < 7; offset++)
            {
                await RunLeasedProductStoreDailyRefreshAsync(currentDay.AddDays(-offset));
            }

            _logger.LogInformation("当天数据全量刷新完成: {Date}", currentDay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "全量刷新当天数据失败");
            throw;
        }
    }

    internal async Task<bool> RunLeasedFullRefreshForSingleDateAsync(
        DateTime date,
        string label
    )
    {
        var result = await _orchestration.BatchFullRefreshConcurrent(date, date, 1);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }

        if (result.SkippedDates.Any())
        {
            _logger.LogInformation(
                "{Label}数据全量刷新跳过，日期 {Date} 已有运行中统计租约",
                label,
                date.ToString("yyyy-MM-dd")
            );
            return false;
        }

        return true;
    }

    internal async Task<bool> RunLeasedProductStoreDailyRefreshAsync(DateTime date)
    {
        var targetDate = date.Date;
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
        var posmContext = scope.ServiceProvider.GetRequiredService<POSMSqlSugarContext>();
        var hbSalesContext = scope.ServiceProvider.GetService<HBSalesRecordSqlSugarContext>();
        var logger = _logger;
        var leaseService = scope.ServiceProvider.GetRequiredService<ScheduledTaskLeaseService>();
        var dateStr = targetDate.ToString("yyyy-MM-dd");
        var leaseTaskType = SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType;
        var leaseDuration = TimeSpan.FromHours(2);
        string? leaseToken = null;
        DateTime? sourceWatermark = null;

        try
        {
            var lease = await leaseService.TryAcquireAsync(leaseTaskType, dateStr, leaseDuration);
            if (!lease.Acquired)
            {
                _logger.LogInformation("日期 {Date} 已有统计租约运行中，商品滚动补算跳过", dateStr);
                return false;
            }

            leaseToken = lease.Lease?.LeaseToken;
            if (string.IsNullOrWhiteSpace(leaseToken))
            {
                throw new InvalidOperationException($"统计租约缺少 fencing token: {dateStr}");
            }

            await leaseService.EnsureActiveAsync(
                leaseTaskType,
                dateStr,
                leaseToken,
                leaseDuration,
                "商品分店每日滚动补算"
            );
            if (targetDate.Year == 2025)
            {
                // 滚动补算同样不能让商品表单独 Running/Failed，原子入口会成对维护状态。
                await _productRefresh.Update2025StoreAndProductStatisticsAtomically(
                    context,
                    posmContext,
                    hbSalesContext,
                    logger,
                    targetDate
                );
            }
            else
            {
                sourceWatermark = await SalesStatisticsProductStoreDailyStateSlice
                    .QueryDailySourceWatermarkAsync(
                    posmContext,
                    hbSalesContext,
                    targetDate
                );
                await SalesStatisticsProductStoreDailyStateSlice.UpsertStatisticStateAsync(
                    context,
                    SalesStatisticType.ProductStoreDaily,
                    targetDate,
                    SalesStatisticRefreshStatus.Running,
                    sourceWatermark,
                    null
                );
                await _productRefresh.UpdateProductStoreDailyStatisticsWithContext(
                    context,
                    posmContext,
                    hbSalesContext,
                    logger,
                    targetDate
                );
            }
            await leaseService.EnsureActiveAsync(
                leaseTaskType,
                dateStr,
                leaseToken,
                leaseDuration,
                "商品分店每日滚动补算完成确认"
            );
            if (!await leaseService.CompleteAsync(leaseTaskType, dateStr, leaseToken, true))
            {
                throw new InvalidOperationException($"统计租约完成失败，token 已失效: {dateStr}");
            }
            return true;
        }
        catch (Exception ex)
        {
            if (targetDate.Year != 2025)
            {
                await SalesStatisticsProductStoreDailyStateSlice.UpsertStatisticStateAsync(
                    context,
                    SalesStatisticType.ProductStoreDaily,
                    targetDate,
                    SalesStatisticRefreshStatus.Failed,
                    sourceWatermark,
                    ex.Message
                );
            }
            if (!string.IsNullOrWhiteSpace(leaseToken))
            {
                await leaseService.CompleteAsync(leaseTaskType, dateStr, leaseToken, false, ex.Message);
            }
            throw;
        }
    }

    /// <summary>
    /// 检查是否为国内供应商
    /// </summary>
    /// <param name="supplierCode">供应商代码</param>
    /// <returns>是否为国内供应商</returns>
    internal async Task<bool> CheckIsDomesticSupplierAsync(string supplierCode)
    {
        try
        {
            // 查询中国供应商表
            var chinaSupplier = await _context.ChinaSupplierDb.GetFirstAsync(s =>
                s.SupplierCode == supplierCode && !s.IsDeleted
            );

            if (chinaSupplier != null)
            {
                return true;
            }

            // 查询国内产品表
            var domesticProduct = await _context
                .Db.Queryable<DomesticProduct>()
                .Where(dp => dp.SupplierCode == supplierCode && !dp.IsDeleted)
                .FirstAsync();

            return domesticProduct != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 更新指定分店统计数据
    /// 可以指定分店代码列表，只更新这些分店的统计数据
    /// </summary>
    /// <param name="date">目标日期</param>
    /// <param name="branchCodes">分店代码列表，为空则更新所有分店</param>
    public async Task UpdateStoreStatistics(DateTime date, List<string>? branchCodes = null)
    {
        var targetDate = date.Date;
        var targetBranchCodes = SalesStatisticsCodeRules.NormalizeBranchCodes(branchCodes);
        try
        {
            _logger.LogInformation(
                "开始更新指定分店统计数据: {Date}, Branches: {Branches}",
                targetDate,
                targetBranchCodes.Any() ? string.Join(", ", targetBranchCodes) : "All"
            );

            if (targetDate.Year == 2025)
            {
                if (targetBranchCodes.Any())
                {
                    throw new InvalidOperationException(
                        "2025 年不能仅刷新指定分店：该操作会破坏 ProductStoreDaily 与 StoreSales 的双表一致性，请执行全分店刷新"
                    );
                }

                // null、空集合和空白分店代码均等同全分店，统一进入双表原子刷新。
                await _productRefresh.Update2025StoreAndProductStatisticsAtomically(
                    _context,
                    _posmContext,
                    _hbSalesContext,
                    _logger,
                    targetDate
                );
                return;
            }

            var statisticsList = await _productSupport.BuildStoreStatisticsAsync(
                _context,
                _posmContext,
                GetHBSalesContextFor2025(targetDate),
                targetDate,
                branchCodes
            );

            await SalesStatisticsTransactionExecutor.ExecuteAsync(
                beginAsync: () => _context.Db.Ado.BeginTranAsync(),
                workAsync: async () =>
                {
                    // 指定分店重算只替换对应分店，避免清掉同日其它分店统计。
                    var deleteable = _context.Db.Deleteable<StoreSalesStatistic>()
                        .Where(s => s.Date == targetDate);
                    if (targetBranchCodes.Any())
                    {
                        deleteable = deleteable.Where(s => targetBranchCodes.Contains(s.BranchCode));
                    }

                    var deletedCount = await deleteable.ExecuteCommandAsync();
                    _logger.LogInformation("删除 {Count} 条分店统计旧记录", deletedCount);

                    // 批量插入新记录
                    if (statisticsList.Any())
                    {
                        _context
                            .Db.Fastest<StoreSalesStatistic>()
                            .PageSize(BatchSize)
                            .BulkCopy(statisticsList);
                    }
                },
                commitAsync: () => _context.Db.Ado.CommitTranAsync(),
                rollbackAsync: () => _context.Db.Ado.RollbackTranAsync(),
                logger: _logger,
                operationName: "指定分店统计数据更新"
            );

            _logger.LogInformation(
                "指定分店统计数据更新完成: {Date}, 总记录: {Total}",
                targetDate,
                statisticsList.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新指定分店统计数据失败: {Date}", targetDate);
            throw;
        }
    }

    }
}
