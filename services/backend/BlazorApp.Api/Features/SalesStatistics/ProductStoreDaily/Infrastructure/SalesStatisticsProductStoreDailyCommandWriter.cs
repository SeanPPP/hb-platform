using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Services;

/// <summary>商品分店日统计写入边界：只在此处定义主统计替换事务。</summary>
internal sealed class SalesStatisticsProductStoreDailyCommandWriter
{
    private const int BatchSize = 5000;

    internal sealed record PersistResult(
        SalesStatisticsProductStoreDailyStateSlice.ProductStatisticStatusResult Status,
        ProductStoreDailyBatchFence? BatchFence);

    internal async Task<PersistResult> PersistAsync(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        ILogger logger,
        ProductStoreDailyRefreshInput input,
        ProductStoreDailyRefreshBuildResult build,
        IReadOnlyList<StoreSalesStatistic>? atomicStoreStatistics,
        DateTime? sourceWatermarkOverride,
        Func<Task>? validateSourceWatermarkBeforeCommitAsync,
        string? atomicSuccessStatusOverride,
        Guid? expectedJobId = null,
        Func<Task>? validateExecutionOwnershipBeforeCommitAsync = null)
    {
        var status = await SalesStatisticsProductStoreDailyStateSlice.BuildProductStatisticStatusAsync(
            context,
            input.TargetDate,
            build.Statistics,
            build.Diagnostics,
            input.LastSourceUploadTime,
            build.SupplementalReturnAdjustments,
            atomicStoreStatistics
        );
        if (build.Statistics.Count == 0 && input.RawRows.Count > 0)
        {
            status = new SalesStatisticsProductStoreDailyStateSlice.ProductStatisticStatusResult(
                SalesStatisticRefreshStatus.Failed,
                $"商品分店每日统计存在 {input.RawRows.Count} 条来源记录，但没有可写入的有效分店商品: {input.TargetDate:yyyy-MM-dd}");
        }
        if (atomicStoreStatistics != null && status.Status == SalesStatisticRefreshStatus.Failed)
            throw new InvalidOperationException(status.ErrorMessage ?? "2025 商品分店每日统计业务校验失败");
        if (atomicStoreStatistics != null && status.Status == SalesStatisticRefreshStatus.Fresh
            && !string.IsNullOrWhiteSpace(atomicSuccessStatusOverride))
        {
            status = new SalesStatisticsProductStoreDailyStateSlice.ProductStatisticStatusResult(
                atomicSuccessStatusOverride,
                status.ErrorMessage,
                status.SourceProductVersion
            );
        }

        var effectiveSourceWatermark = sourceWatermarkOverride ?? input.LastSourceUploadTime;
        SupplierStoreStatisticBuildResult? supplierBuild = null;
        string? productVersion = null;
        ProductStoreDailyBatchFence? capturedBatchFence = null;

        if (validateSourceWatermarkBeforeCommitAsync != null)
            await validateSourceWatermarkBeforeCommitAsync();

        // 双表入口和普通入口共用这一处主事务，保证删除、批量写入及状态切换不可拆分。
        await SalesStatisticsTransactionExecutor.ExecuteAsync(
            beginAsync: () => context.Db.Ado.BeginTranAsync(),
            workAsync: async () =>
            {
                await SalesStatisticsProductStoreDailyStateSlice.FenceProductStatisticExecutionOwnerAsync(
                    context,
                    input.TargetDate,
                    expectedJobId);
                if (expectedJobId.HasValue && input.TargetDate.Date < DateTime.Today)
                {
                    // 历史日期的队列恢复只修复销售事实；沿用旧快照的成本，不能按恢复当天的进价重算毛利。
                    var previousRows = await context.Db.Queryable<ProductStoreDailySalesStatistic>()
                        .Where(row => row.Date >= input.TargetDate.Date && row.Date < input.TargetDate.Date.AddDays(1))
                        .With(SqlWith.UpdLock).ToListAsync();
                    PreserveHistoricalCostSnapshots(build.Statistics, previousRows);
                }
                if (atomicStoreStatistics != null)
                {
                    await context.Db.Deleteable<StoreSalesStatistic>().Where(row => row.Date == input.TargetDate).ExecuteCommandAsync();
                    if (atomicStoreStatistics.Any())
                        context.Db.Fastest<StoreSalesStatistic>().PageSize(BatchSize).BulkCopy(atomicStoreStatistics.ToList());
                }
                var deletedCount = await context.Db.Deleteable<ProductStoreDailySalesStatistic>()
                    .Where(row => row.Date == input.TargetDate).ExecuteCommandAsync();
                logger.LogInformation("删除 {Count} 条商品分店每日统计旧记录", deletedCount);
                if (build.Statistics.Any())
                    context.Db.Fastest<ProductStoreDailySalesStatistic>().PageSize(BatchSize).BulkCopy(build.Statistics);
                if (status.Status == SalesStatisticRefreshStatus.Fresh
                    || status.Status == SalesStatisticRefreshStatus.ProvisionalFresh)
                {
                    // 版本与供应商汇总必须基于数据库已持久化的 decimal 表示，避免内存中的无限精度
                    // 在 decimal(18,4) 落库后改变 hash 或供应商金额对账结果。
                    var persistedProductStatistics = await context.Db.Queryable<ProductStoreDailySalesStatistic>()
                        .Where(row => row.Date >= input.TargetDate.Date && row.Date < input.TargetDate.Date.AddDays(1))
                        .ToListAsync();
                    productVersion = SupplierStatisticVersion.ComputeProductVersion(persistedProductStatistics);
                    status = status with { SourceProductVersion = productVersion };
                    supplierBuild = await new SalesStatisticsSupplierStoreSummaryService()
                        .BuildFromProductStatisticsAsync(context, posmContext, persistedProductStatistics, DateTime.Now);
                    await new SalesStatisticsSupplierStoreSummaryService().PersistWithinTransactionAsync(
                        context,
                        input.TargetDate,
                        supplierBuild,
                        status.Status,
                        productVersion,
                        sourceWatermark: effectiveSourceWatermark);
                }
                // 所有商品与供应商派生数据写入后、四类状态发布前再次确认来源水位；
                // 回调失败会由统一事务执行器回滚整批写入。
                if (validateSourceWatermarkBeforeCommitAsync != null)
                    await validateSourceWatermarkBeforeCommitAsync();
                if (validateExecutionOwnershipBeforeCommitAsync != null)
                    await validateExecutionOwnershipBeforeCommitAsync();
                await SalesStatisticsProductStoreDailyStateSlice.UpsertProductStatisticStateAsync(
                    context,
                    input.TargetDate,
                    status,
                    effectiveSourceWatermark,
                    overwriteLastSourceUploadTime: atomicStoreStatistics != null
                );
                if (atomicStoreStatistics != null)
                {
                    await SalesStatisticsProductStoreDailyStateSlice.UpsertStatisticStateAsync(
                        context,
                        SalesStatisticType.StoreSales,
                        input.TargetDate,
                        status.Status,
                        effectiveSourceWatermark,
                        status.ErrorMessage,
                        overwriteLastSourceUploadTime: true
                    );

                    // fence 在同一原子事务中、四类状态均已写入后捕获，避免提交后按日期重读时
                    // 被下一批替换，从而把 B 批误认为 A 批返回给批末 finalize/fail。
                    var capturedStates = await context.Db.Queryable<SalesStatisticRefreshState>()
                        .Where(row => row.Date >= input.TargetDate.Date
                            && row.Date < input.TargetDate.Date.AddDays(1)
                            && (row.StatisticType == SalesStatisticType.ProductStoreDaily
                                || row.StatisticType == SalesStatisticType.StoreSales
                                || row.StatisticType == SalesStatisticType.AustralianSupplierStoreSales
                                || row.StatisticType == SalesStatisticType.ChinaSupplierStoreSales))
                        .With(SqlWith.UpdLock)
                        .ToListAsync();
                    if (capturedStates.Count != 4)
                        throw new InvalidOperationException($"2025 批次四类状态写入不完整，拒绝返回 fence: {input.TargetDate:yyyy-MM-dd}");
                    capturedBatchFence = new ProductStoreDailyBatchFence(
                        input.TargetDate.Date,
                        capturedStates.Select(state => new ProductStoreDailyStateFence(
                            state.StatisticType,
                            state.Status,
                            state.SourceProductVersion,
                            state.JobId,
                            state.LastAggregatedAtUtc,
                            state.CompletedAtUtc,
                            state.LastSourceUploadTime)).ToList());
                }
            },
            commitAsync: () => context.Db.Ado.CommitTranAsync(),
            rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
            logger: logger,
            operationName: "商品分店每日统计更新");
        return new PersistResult(status, capturedBatchFence);
    }

    internal static void PreserveHistoricalCostSnapshots(
        IReadOnlyList<ProductStoreDailySalesStatistic> rebuilt,
        IReadOnlyList<ProductStoreDailySalesStatistic> previous)
    {
        var previousByKey = previous.ToDictionary(
            row => (row.Date.Date, row.BranchCode, row.SupplierCode, row.ProductCode));
        foreach (var row in rebuilt)
        {
            previousByKey.TryGetValue((row.Date.Date, row.BranchCode, row.SupplierCode, row.ProductCode), out var old);
            row.UnitCostSnapshot = old?.UnitCostSnapshot;
            row.CostSource = old?.CostSource ?? "Missing";
            if (old != null && old.TotalQuantity == row.TotalQuantity && old.TotalAmount == row.TotalAmount)
            {
                // 未变动的销售行原样保留落库精度及历史成本缺口。
                row.TotalCost = old.TotalCost;
                row.GrossProfit = old.GrossProfit;
                row.GrossMarginRate = old.GrossMarginRate;
                continue;
            }
            // 新发现的历史行没有可信成本快照时保持缺失；数量修正只使用已有的历史单价。
            row.TotalCost = old?.UnitCostSnapshot * row.TotalQuantity;
            row.GrossProfit = row.TotalCost.HasValue ? row.TotalAmount - row.TotalCost.Value : null;
            row.GrossMarginRate = row.TotalAmount > 0 && row.GrossProfit.HasValue
                ? row.GrossProfit.Value / row.TotalAmount : null;
        }
    }

}
