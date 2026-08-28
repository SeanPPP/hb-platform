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
    /// <summary>销售统计垂直切片：SalesStatisticsProductStoreDailyStateSlice。</summary>
    internal sealed class SalesStatisticsProductStoreDailyStateSlice : SalesStatisticsSliceBase
    {
        public SalesStatisticsProductStoreDailyStateSlice(SalesStatisticsSliceContext shared)
            : base(shared) { }

    internal static decimal? ResolveUnitCost(
        string branchCode,
        string supplierCode,
        string productCode,
        Dictionary<string, decimal?> storeCostMap,
        Dictionary<string, decimal?> productCostMap,
        Dictionary<string, decimal?> warehouseCostMap,
        out string costSource
    ) => SalesStatisticsProductStoreDailyDomainRules.ResolveUnitCost(
        branchCode,
        supplierCode,
        productCode,
        storeCostMap,
        productCostMap,
        warehouseCostMap,
        out costSource
    );

    internal sealed record ProductStatisticStatusResult(string Status, string? ErrorMessage);

    internal static async Task<ProductStatisticStatusResult> BuildProductStatisticStatusAsync(
        SqlSugarContext context,
        DateTime targetDate,
        List<ProductStoreDailySalesStatistic> statisticsList,
        ProductStatisticDiagnostics diagnostics,
        DateTime? lastSourceUploadTime,
        IReadOnlyList<ProductStoreDailyBranchRollup> supplementalReturnAdjustments,
        IReadOnlyList<StoreSalesStatistic>? atomicStoreStatistics = null
    )
    {
        var existingStoreSales = atomicStoreStatistics == null
            ? await context.Db.Queryable<StoreSalesStatistic>()
                .Where(s => s.Date == targetDate)
                .Select(s => new { s.BranchCode, s.TotalAmount, s.TotalQuantity })
                .ToListAsync()
            : atomicStoreStatistics.Select(s => new
            {
                s.BranchCode,
                s.TotalAmount,
                s.TotalQuantity,
            }).ToList();

        var storeRollups = existingStoreSales
            .Where(x => !string.IsNullOrWhiteSpace(x.BranchCode))
            .Select(x => new ProductStoreDailyBranchRollup(
                x.BranchCode!,
                x.TotalAmount,
                x.TotalQuantity
            ))
            .ToList();
        var effectiveStoreRollups = ProductStoreDailyReconciliationCalculator
            .ApplyExistingStoreAdjustments(storeRollups, supplementalReturnAdjustments);

        var productRollups = statisticsList
            .Where(x => !string.IsNullOrWhiteSpace(x.BranchCode))
            .Select(x => new ProductStoreDailyBranchRollup(
                x.BranchCode,
                x.TotalAmount,
                x.TotalQuantity
            ));

        var branchDiagnostics = diagnostics.BranchDiagnostics
            .ToDictionary(
                item => item.Key,
                item => new ProductStoreDailyBranchDiagnostic(
                    item.Value.UnmatchedSupplierAmount,
                    item.Value.UnmatchedSupplierQuantity,
                    item.Value.UnmatchedSupplierProductCount
                )
            );

        var reconciliation = ProductStoreDailyReconciliationCalculator.Calculate(
            targetDate,
            productRollups,
            effectiveStoreRollups,
            branchDiagnostics
        );

        return new ProductStatisticStatusResult(reconciliation.Status, reconciliation.ErrorMessage);
    }

    internal static async Task UpsertProductStatisticStateAsync(
        SqlSugarContext context,
        DateTime targetDate,
        ProductStatisticStatusResult status,
        DateTime? lastSourceUploadTime,
        bool overwriteLastSourceUploadTime = false
    )
    {
        var nextDate = targetDate.Date.AddDays(1);
        var existing = await context.Db.Queryable<SalesStatisticRefreshState>()
            // SQLite/SQL Server 的 DateTime 精度不同，按规范化日期范围读取已有状态。
            .Where(s =>
                s.StatisticType == SalesStatisticType.ProductStoreDaily
                && s.Date >= targetDate.Date
                && s.Date < nextDate
            )
            .FirstAsync();

        if (existing == null)
        {
            existing = new SalesStatisticRefreshState
            {
                StatisticType = SalesStatisticType.ProductStoreDaily,
                Date = targetDate,
            };
            existing.Status = status.Status;
            existing.LastSourceUploadTime = lastSourceUploadTime;
            existing.SourceTimeZone = "POSM_LOCAL";
            existing.LastAggregatedAtUtc = DateTime.UtcNow;
            existing.LastCheckedAtUtc = DateTime.UtcNow;
            existing.ErrorMessage = status.ErrorMessage;
            if (
                status.Status == SalesStatisticRefreshStatus.Fresh
                || status.Status == ProvisionalFreshStatus
                || status.Status == SalesStatisticRefreshStatus.Failed
            )
            {
                existing.CompletedAtUtc = DateTime.UtcNow;
            }
            await context.Db.Insertable(existing).ExecuteCommandAsync();
            return;
        }

        existing.Status = status.Status;
        existing.LastSourceUploadTime = overwriteLastSourceUploadTime
            ? lastSourceUploadTime
            : lastSourceUploadTime ?? existing.LastSourceUploadTime;
        existing.SourceTimeZone = "POSM_LOCAL";
        existing.LastAggregatedAtUtc = DateTime.UtcNow;
        existing.LastCheckedAtUtc = DateTime.UtcNow;
        existing.ErrorMessage = status.ErrorMessage;
        if (
            status.Status == SalesStatisticRefreshStatus.Fresh
            || status.Status == ProvisionalFreshStatus
            || status.Status == SalesStatisticRefreshStatus.Failed
        )
        {
            existing.CompletedAtUtc = DateTime.UtcNow;
        }
        await context.Db.Updateable(existing).ExecuteCommandAsync();
    }

    internal static async Task UpsertStatisticStateAsync(
        SqlSugarContext context,
        string statisticType,
        DateTime targetDate,
        string status,
        DateTime? lastSourceUploadTime,
        string? errorMessage,
        bool overwriteLastSourceUploadTime = false
    )
    {
        var now = DateTime.UtcNow;
        var nextDate = targetDate.Date.AddDays(1);
        var existing = await context.Db.Queryable<SalesStatisticRefreshState>()
            .Where(s =>
                s.StatisticType == statisticType
                && s.Date >= targetDate.Date
                && s.Date < nextDate
            )
            .FirstAsync();
        var isNew = existing == null;

        if (isNew)
        {
            existing = new SalesStatisticRefreshState
            {
                StatisticType = statisticType,
                Date = targetDate.Date,
                SourceTimeZone = "POSM_LOCAL",
            };
        }
        var state = existing!;

        state.Status = status;
        state.LastSourceUploadTime = overwriteLastSourceUploadTime
            ? lastSourceUploadTime
            : lastSourceUploadTime ?? state.LastSourceUploadTime;
        state.LastCheckedAtUtc = now;
        state.ErrorMessage = errorMessage;
        if (status == SalesStatisticRefreshStatus.Running)
        {
            state.StartedAtUtc = now;
            state.CompletedAtUtc = null;
        }
        else
        {
            state.LastAggregatedAtUtc = now;
            state.CompletedAtUtc = now;
        }

        if (isNew)
        {
            await context.Db.Insertable(state).ExecuteCommandAsync();
        }
        else
        {
            await context.Db.Updateable(state).ExecuteCommandAsync();
        }
    }

    internal static async Task<DateTime?> QueryDailyPosmSourceWatermarkAsync(
        POSMSqlSugarContext posmContext,
        DateTime date
    )
    {
        var targetDate = date.Date;
        var nextDate = targetDate.AddDays(1);
        var watermarks = new List<DateTime?>
        {
            await posmContext.Db.Queryable<SalesOrder>()
                .Where(order =>
                    order.Status != null
                    && (order.Status == 1 || order.Status == 4)
                    && order.OrderTime != null
                    && order.OrderTime >= targetDate
                    && order.OrderTime < nextDate
                )
                .MaxAsync(order => order.LastUploadTime),
            await posmContext.Db.Queryable<PaymentDetail, SalesOrder>(
                    (payment, order) => payment.OrderGuid == order.OrderGuid
                )
                .Where((payment, order) =>
                    order.Status != null
                    && (order.Status == 1 || order.Status == 4)
                    && order.OrderTime != null
                    && order.OrderTime >= targetDate
                    && order.OrderTime < nextDate
                )
                .MaxAsync((payment, order) => payment.LastUploadTime),
            await posmContext.Db.Queryable<SalesOrderDetail, SalesOrder>(
                    (detail, order) => detail.OrderGuid == order.OrderGuid
                )
                .Where((detail, order) =>
                    order.Status != null
                    && (order.Status == 1 || order.Status == 4)
                    && order.OrderTime != null
                    && order.OrderTime >= targetDate
                    && order.OrderTime < nextDate
                )
                .MaxAsync((detail, order) => detail.LastUploadTime),
        };
        var values = watermarks.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return values.Count == 0 ? null : values.Max();
    }

    internal static async Task<DateTime?> QueryDailySourceWatermarkAsync(
        POSMSqlSugarContext posmContext,
        HBSalesRecordSqlSugarContext? hbSalesContext,
        DateTime date,
        IReadOnlyCollection<ProductStoreDailySourceRow>? preloadedHBSalesRows = null
    )
    {
        var targetDate = date.Date;
        var nextDate = targetDate.AddDays(1);
        var mainCheckoutDateWindowStart = targetDate.AddDays(
            -HBSalesMainCheckoutDateWindowDays
        );
        var mainCheckoutDateWindowEnd = nextDate.AddDays(HBSalesMainCheckoutDateWindowDays);
        var watermarks = new List<DateTime?>
        {
            await QueryDailyPosmSourceWatermarkAsync(posmContext, targetDate),
        };

        if (targetDate.Year == 2025)
        {
            var requiredHBSalesContext = hbSalesContext
                ?? throw new InvalidOperationException("2025 年统计水位查询缺少 HBSalesRecord 上下文");
            if (preloadedHBSalesRows != null)
            {
                // 仅 pre 阶段复用已加载明细；post 阶段不传此参数，必须重新查库检测漂移。
                watermarks.Add(
                    SalesStatisticsProductStoreDailyDomainRules.GetHBSalesSourceWatermark(
                        preloadedHBSalesRows
                    )
                );
            }
            else
            {
                var originalCommandTimeout = requiredHBSalesContext.Db.Ado.CommandTimeOut;
                requiredHBSalesContext.Db.Ado.CommandTimeOut = Math.Max(
                    originalCommandTimeout,
                    CommandTimeoutSeconds
                );
                HBSalesSourceWatermarkRow? hbSalesWatermark;
                try
                {
                    hbSalesWatermark = await requiredHBSalesContext.Db.Queryable<SalesOrderMain>()
                        .LeftJoin<SalesOrderDetailRecord>((main, detail) =>
                            main.B销售单号 == detail.B销售单号
                        )
                        .Where((main, detail) =>
                            detail.B结账日期.HasValue
                            && detail.B结账日期.Value >= targetDate
                            && detail.B结账日期.Value < nextDate
                            && main.B结账日期.HasValue
                            && main.B结账日期.Value >= mainCheckoutDateWindowStart
                            && main.B结账日期.Value < mainCheckoutDateWindowEnd
                            && (main.B单据类型 == null || main.B单据类型.Trim() != "2")
                        )
                        .Select((main, detail) => new HBSalesSourceWatermarkRow
                        {
                            MainLastModifiedAt = SqlFunc.AggregateMax(main.FGC_LastModifyDate),
                            MainCreatedAt = SqlFunc.AggregateMax(main.FGC_CreateDate),
                            DetailLastModifiedAt = SqlFunc.AggregateMax(detail.FGC_LastModifyDate),
                            DetailCreatedAt = SqlFunc.AggregateMax(detail.FGC_CreateDate),
                        })
                        .FirstAsync();
                }
                finally
                {
                    requiredHBSalesContext.Db.Ado.CommandTimeOut = originalCommandTimeout;
                }
                if (hbSalesWatermark != null)
                {
                    watermarks.Add(
                        SalesStatisticsProductStoreDailyDomainRules.GetLatestSourceTime(
                            hbSalesWatermark.MainLastModifiedAt,
                            hbSalesWatermark.MainCreatedAt,
                            hbSalesWatermark.DetailLastModifiedAt,
                            hbSalesWatermark.DetailCreatedAt
                        )
                    );
                }
            }
        }

        var values = watermarks
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
        return values.Count == 0 ? null : values.Max();
    }

    internal static string BuildProductStoreDailySubmitMessage(int submittedCount, int skippedCount)
    {
        if (submittedCount == 0 && skippedCount > 0)
        {
            return $"所选 {skippedCount} 天商品统计已有任务执行中，未重复提交";
        }

        if (skippedCount > 0)
        {
            return $"已提交 {submittedCount} 天商品统计重算，跳过 {skippedCount} 天执行中的任务";
        }

        return $"已提交 {submittedCount} 天商品统计重算任务";
    }

    internal static async Task UpsertProductStatisticQueuedStateAsync(
        SqlSugarContext context,
        DateTime targetDate,
        Guid jobId,
        string? requestedBy
    )
    {
        var now = DateTime.UtcNow;
        var existing = await context.Db.Queryable<SalesStatisticRefreshState>()
            .Where(s => s.StatisticType == SalesStatisticType.ProductStoreDaily && s.Date == targetDate)
            .FirstAsync();

        if (existing == null)
        {
            await context.Db.Insertable(new SalesStatisticRefreshState
            {
                StatisticType = SalesStatisticType.ProductStoreDaily,
                Date = targetDate,
                Status = SalesStatisticRefreshStatus.Queued,
                SourceTimeZone = "POSM_LOCAL",
                JobId = jobId,
                RequestedBy = requestedBy,
                RequestedAtUtc = now,
                LastCheckedAtUtc = now,
            }).ExecuteCommandAsync();
            return;
        }

        existing.Status = SalesStatisticRefreshStatus.Queued;
        existing.JobId = jobId;
        existing.RequestedBy = requestedBy;
        existing.RequestedAtUtc = now;
        existing.StartedAtUtc = null;
        existing.CompletedAtUtc = null;
        existing.LastCheckedAtUtc = now;
        existing.ErrorMessage = null;
        await context.Db.Updateable(existing).ExecuteCommandAsync();
    }

    /// <summary>
    /// 在主统计事务内以 Running + JobId 条件触碰状态行，防止租约已换主的旧 worker 覆盖后继任务。
    /// </summary>
    internal static async Task FenceProductStatisticExecutionOwnerAsync(
        SqlSugarContext context,
        DateTime targetDate,
        Guid? expectedJobId)
    {
        if (!expectedJobId.HasValue)
            return;
        if (expectedJobId.Value == Guid.Empty)
            throw new InvalidOperationException("队列任务 JobId 不能为空");

        var normalizedDate = targetDate.Date;
        var affectedRows = await context.Db.Updateable<SalesStatisticRefreshState>()
            .SetColumns(state => state.LastCheckedAtUtc == state.LastCheckedAtUtc)
            .Where(state =>
                state.StatisticType == SalesStatisticType.ProductStoreDaily
                && state.Date >= normalizedDate
                && state.Date < normalizedDate.AddDays(1)
                && state.JobId == expectedJobId.Value
                && state.Status == SalesStatisticRefreshStatus.Running)
            .ExecuteCommandAsync();
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"商品统计执行权已变化，拒绝旧 worker 写入: {normalizedDate:yyyy-MM-dd} {expectedJobId.Value}");
        }
    }

    }
}
