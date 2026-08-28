using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Services;

/// <summary>商品分店日统计写入边界：只在此处定义主统计替换事务。</summary>
internal sealed class SalesStatisticsProductStoreDailyCommandWriter
{
    private const int BatchSize = 5000;

    internal async Task<SalesStatisticsProductStoreDailyStateSlice.ProductStatisticStatusResult> PersistAsync(
        SqlSugarContext context,
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
                status.ErrorMessage
            );
        }

        var effectiveSourceWatermark = sourceWatermarkOverride ?? input.LastSourceUploadTime;
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
                }
            },
            commitAsync: () => context.Db.Ado.CommitTranAsync(),
            rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
            logger: logger,
            operationName: "商品分店每日统计更新");
        return status;
    }
}
