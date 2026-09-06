using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services;

/// <summary>
/// 供应商分店统计兼容入口。所有入口都从已完成商品分店日快照派生两张供应商表，
/// 不再回扫销售明细或重新读取成本。
/// </summary>
internal sealed class SalesStatisticsSupplierStoreSlice : SalesStatisticsSliceBase
{
    public SalesStatisticsSupplierStoreSlice(SalesStatisticsSliceContext shared)
        : base(shared) { }

    private static async Task RebuildDerivedSupplierStoreStatisticsAsync(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        ILogger logger,
        DateTime date,
        List<string>? branchCodes,
        List<string>? supplierCodes)
    {
        var targetDate = date.Date;
        var service = new SalesStatisticsSupplierStoreSummaryService();
        var normalizedBranches = SalesStatisticsCodeRules.NormalizeBranchCodes(branchCodes);
        var normalizedSuppliers = SalesStatisticsCodeRules.NormalizeSupplierCodes(supplierCodes);
        IReadOnlyCollection<string>? branchFilter = normalizedBranches.Count == 0 ? null : normalizedBranches;
        IReadOnlyCollection<string>? supplierFilter = normalizedSuppliers.Count == 0 ? null : normalizedSuppliers;
        var (build, productVersion, productStatus, sourceWatermark, fence) = await service.BuildFromCompletedProductSnapshotAsync(
            context, posmContext, targetDate, DateTime.Now, branchFilter, supplierFilter);
        var publishStatus = branchFilter is null && supplierFilter is null
            ? productStatus
            : SalesStatisticRefreshStatus.Stale;

        await SalesStatisticsTransactionExecutor.ExecuteAsync(
            beginAsync: () => context.Db.Ado.BeginTranAsync(),
            workAsync: async () =>
            {
                // 版本为空的历史商品状态在这里一次性补齐；版本变化则拒绝旧入口发布。
                await service.EnsureProductVersionUnchangedAsync(context, targetDate, productVersion, fence);
                await service.PersistWithinTransactionAsync(
                    context, targetDate, build, publishStatus, productVersion, sourceWatermark, branchFilter, supplierFilter);
            },
            commitAsync: () => context.Db.Ado.CommitTranAsync(),
            rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
            logger: logger,
            operationName: "从商品分店日快照重建供应商分店统计");
    }

    public Task UpdateAustralianSupplierStoreStatistics(
        DateTime? date = null,
        List<string>? branchCodes = null,
        List<string>? supplierCodes = null) =>
        RebuildDerivedSupplierStoreStatisticsAsync(
            _context, _posmContext, _logger, (date ?? DateTime.Now.Date).Date, branchCodes, supplierCodes);

    public Task UpdateChinaSupplierStoreStatistics(
        DateTime? date = null,
        List<string>? branchCodes = null,
        List<string>? supplierCodes = null) =>
        RebuildDerivedSupplierStoreStatisticsAsync(
            _context, _posmContext, _logger, (date ?? DateTime.Now.Date).Date, branchCodes, supplierCodes);

    internal static Task UpdateAustralianSupplierStoreStatisticsWithContext(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        ILogger logger,
        DateTime? date,
        List<string>? branchCodes,
        List<string>? supplierCodes) =>
        RebuildDerivedSupplierStoreStatisticsAsync(
            context, posmContext, logger, (date ?? DateTime.Now.Date).Date, branchCodes, supplierCodes);

    internal static Task UpdateChinaSupplierStoreStatisticsWithContext(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        ILogger logger,
        DateTime? date,
        List<string>? branchCodes,
        List<string>? supplierCodes) =>
        RebuildDerivedSupplierStoreStatisticsAsync(
            context, posmContext, logger, (date ?? DateTime.Now.Date).Date, branchCodes, supplierCodes);
}
