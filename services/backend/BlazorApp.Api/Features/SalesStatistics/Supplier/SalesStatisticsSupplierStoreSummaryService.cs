using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Services;

/// <summary>
/// 供应商分店统计的唯一派生写入边界。
/// 商品日统计完成后，所有入口都通过此服务生成两张供应商表。
/// </summary>
internal sealed class SalesStatisticsSupplierStoreSummaryService
{
    private const int BatchSize = 5000;

    internal sealed record ProductSnapshotFence(
        string Status,
        DateTime? LastAggregatedAtUtc,
        DateTime? CompletedAtUtc,
        Guid? JobId,
        DateTime? LastSourceUploadTime,
        string? SourceProductVersion);

    internal async Task<SupplierStoreStatisticBuildResult> BuildFromProductStatisticsAsync(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        IReadOnlyList<ProductStoreDailySalesStatistic> productStatistics,
        DateTime updateTime,
        IReadOnlyCollection<string>? branchCodes = null,
        IReadOnlyCollection<string>? supplierCodes = null)
    {
        var productCodes = productStatistics.Select(row => row.ProductCode)
            .Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var mappings = productCodes.Count == 0
            ? new List<PosmProductSupplierMapping>()
            : await posmContext.Db.Queryable<PosmProductSupplierMapping>()
                .Where(row => productCodes.Contains(row.ProductCode) && !row.IsDeleted)
                .ToListAsync();
        var mappingsByProduct = mappings
            .Where(row => !string.IsNullOrWhiteSpace(row.ProductCode))
            .GroupBy(row => row.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var chinaSuppliers = await context.ChinaSupplierDb.GetListAsync(row => row.SupplierCode != null && !row.IsDeleted);
        var chinaCodes = chinaSuppliers.Where(row => !string.IsNullOrWhiteSpace(row.SupplierCode))
            .Select(row => row.SupplierCode!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chinaNames = chinaCodes.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : chinaSuppliers
                .Where(row => !string.IsNullOrWhiteSpace(row.SupplierCode))
                .GroupBy(row => row.SupplierCode!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().SupplierName ?? group.Key, StringComparer.OrdinalIgnoreCase);

        // 直接中国供应商码也归属澳洲报表 200；先加载中国码集合，确保 direct-only 日期仍会加载本地 200 名称。
        var localCodes = productStatistics.Select(row =>
                chinaCodes.Contains(SalesStatisticsCodeRules.Normalize(row.SupplierCode))
                    ? "200"
                    : ResolveAustralianCode(row))
            .Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var localNames = localCodes.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : (await context.HBLocalSupplierDb.GetListAsync(row => localCodes.Contains(row.LocalSupplierCode) && !row.IsDeleted))
                .Where(row => !string.IsNullOrWhiteSpace(row.LocalSupplierCode))
                .GroupBy(row => row.LocalSupplierCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Name ?? group.Key, StringComparer.OrdinalIgnoreCase);

        return SalesStatisticsSupplierStoreSummaryBuilder.Build(
            productStatistics, mappingsByProduct, localNames, chinaNames, updateTime, chinaCodes, branchCodes, supplierCodes);
    }

    internal async Task<(SupplierStoreStatisticBuildResult Build, string ProductVersion, string ProductStatus, DateTime? SourceWatermark, ProductSnapshotFence Fence)> BuildFromCompletedProductSnapshotAsync(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        DateTime date,
        DateTime updateTime,
        IReadOnlyCollection<string>? branchCodes = null,
        IReadOnlyCollection<string>? supplierCodes = null)
    {
        var targetDate = date.Date;
        var state = await context.Db.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.StatisticType == SalesStatisticType.ProductStoreDaily
                && row.Date >= targetDate && row.Date < targetDate.AddDays(1))
            .FirstAsync();
        var existingProductVersion = SupplierStatisticVersion.GetProductVersion(state);
        if (state == null || (state.Status != SalesStatisticRefreshStatus.Fresh && state.Status != SalesStatisticRefreshStatus.ProvisionalFresh)
            || !state.LastAggregatedAtUtc.HasValue || !state.CompletedAtUtc.HasValue)
            throw new InvalidOperationException($"商品分店每日统计未完成，拒绝发布供应商汇总: {targetDate:yyyy-MM-dd}, 状态={state?.Status ?? "Missing"}");

        var rows = await context.Db.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date >= targetDate && row.Date < targetDate.AddDays(1))
            .ToListAsync();
        var actualVersion = SupplierStatisticVersion.ComputeProductVersion(rows);
        if (existingProductVersion != null && !string.Equals(existingProductVersion, actualVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"商品分店每日统计来源版本已变化，拒绝发布供应商汇总: {targetDate:yyyy-MM-dd}");

        var build = await BuildFromProductStatisticsAsync(context, posmContext, rows, updateTime, branchCodes, supplierCodes);
        return (
            build,
            actualVersion,
            state.Status,
            state.LastSourceUploadTime,
            new ProductSnapshotFence(
                state.Status,
                state.LastAggregatedAtUtc,
                state.CompletedAtUtc,
                state.JobId,
                state.LastSourceUploadTime,
                state.SourceProductVersion));
    }

    internal async Task EnsureProductVersionUnchangedAsync(
        SqlSugarContext context,
        DateTime date,
        string expectedProductVersion,
        ProductSnapshotFence? expectedFence = null)
    {
        var targetDate = date.Date;
        var state = await context.Db.Queryable<SalesStatisticRefreshState>()
            .Where(row => row.StatisticType == SalesStatisticType.ProductStoreDaily
                && row.Date >= targetDate && row.Date < targetDate.AddDays(1))
            .With(SqlWith.UpdLock)
            .FirstAsync();
        var actualProductVersion = SupplierStatisticVersion.GetProductVersion(state);
        if (state == null || (state.Status != SalesStatisticRefreshStatus.Fresh && state.Status != SalesStatisticRefreshStatus.ProvisionalFresh)
            || !state.LastAggregatedAtUtc.HasValue || !state.CompletedAtUtc.HasValue)
            throw new InvalidOperationException($"商品分店每日统计状态已变化，拒绝发布供应商汇总: {targetDate:yyyy-MM-dd}");
        if (expectedFence != null && (
            state.Status != expectedFence.Status
            || state.LastAggregatedAtUtc != expectedFence.LastAggregatedAtUtc
            || state.CompletedAtUtc != expectedFence.CompletedAtUtc
            || state.JobId != expectedFence.JobId
            || state.LastSourceUploadTime != expectedFence.LastSourceUploadTime
            || state.SourceProductVersion != expectedFence.SourceProductVersion))
            throw new InvalidOperationException($"商品分店每日统计状态水位已变化，拒绝发布供应商汇总: {targetDate:yyyy-MM-dd}");

        var currentRows = await context.Db.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row => row.Date >= targetDate && row.Date < targetDate.AddDays(1))
            .With(SqlWith.UpdLock)
            .ToListAsync();
        var currentProductVersion = SupplierStatisticVersion.ComputeProductVersion(currentRows);
        if (!string.Equals(currentProductVersion, expectedProductVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"商品分店每日统计快照已变化，拒绝发布供应商汇总: {targetDate:yyyy-MM-dd}");
        if (actualProductVersion != null && !string.Equals(actualProductVersion, expectedProductVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"商品分店每日统计来源版本已变化，拒绝发布供应商汇总: {targetDate:yyyy-MM-dd}");
        if (actualProductVersion == null)
        {
            var affectedRows = await context.Db.Updateable<SalesStatisticRefreshState>()
                .SetColumns(row => row.SourceProductVersion == expectedProductVersion)
                .Where(row => row.StatisticType == SalesStatisticType.ProductStoreDaily
                    && row.Date >= targetDate && row.Date < targetDate.AddDays(1)
                    && row.Status == state.Status
                    && row.LastAggregatedAtUtc == state.LastAggregatedAtUtc
                    && row.CompletedAtUtc == state.CompletedAtUtc
                    && row.JobId == state.JobId
                    && row.LastSourceUploadTime == state.LastSourceUploadTime
                    && row.SourceProductVersion == null)
                .ExecuteCommandAsync();
            if (affectedRows != 1)
                throw new InvalidOperationException($"商品分店每日统计状态在回填版本时已变化，拒绝发布供应商汇总: {targetDate:yyyy-MM-dd}");
        }
    }

    /// <summary>调用方必须已开启事务；此方法只执行两张派生表和两类状态的成对写入。</summary>
    internal async Task PersistWithinTransactionAsync(
        SqlSugarContext context,
        DateTime targetDate,
        SupplierStoreStatisticBuildResult build,
        string status,
        string productVersion,
        DateTime? sourceWatermark = null,
        IReadOnlyCollection<string>? branchCodes = null,
        IReadOnlyCollection<string>? supplierCodes = null)
    {
        var branches = branchCodes?.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim()).ToList();
        var suppliers = supplierCodes?.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim()).ToList();
        await DeleteAndInsertAustralianAsync(context, targetDate, build.Australian, branches, suppliers);
        await DeleteAndInsertChinaAsync(context, targetDate, build.China, branches, suppliers);
        await SalesStatisticsProductStoreDailyStateSlice.UpsertStatisticStateAsync(
            context, SalesStatisticType.AustralianSupplierStoreSales, targetDate, status, sourceWatermark, null,
            overwriteLastSourceUploadTime: false, sourceProductVersion: productVersion);
        await SalesStatisticsProductStoreDailyStateSlice.UpsertStatisticStateAsync(
            context, SalesStatisticType.ChinaSupplierStoreSales, targetDate, status, sourceWatermark, null,
            overwriteLastSourceUploadTime: false, sourceProductVersion: productVersion);
    }

    private static async Task DeleteAndInsertAustralianAsync(
        SqlSugarContext context,
        DateTime date,
        IReadOnlyCollection<AustralianSupplierStoreSalesDetail> rows,
        IReadOnlyCollection<string>? branches,
        IReadOnlyCollection<string>? suppliers)
    {
        var deleteable = context.Db.Deleteable<AustralianSupplierStoreSalesDetail>()
            .Where(row => row.Date >= date.Date && row.Date < date.Date.AddDays(1));
        if (branches is { Count: > 0 }) deleteable = deleteable.Where(row => branches.Contains(row.BranchCode));
        if (suppliers is { Count: > 0 }) deleteable = deleteable.Where(row => suppliers.Contains(row.SupplierCode));
        await deleteable.ExecuteCommandAsync();
        if (rows.Count > 0)
            context.Db.Fastest<AustralianSupplierStoreSalesDetail>().PageSize(BatchSize).BulkCopy(rows.ToList());
    }

    private static async Task DeleteAndInsertChinaAsync(
        SqlSugarContext context,
        DateTime date,
        IReadOnlyCollection<ChinaSupplierStoreSalesDetail> rows,
        IReadOnlyCollection<string>? branches,
        IReadOnlyCollection<string>? suppliers)
    {
        var deleteable = context.Db.Deleteable<ChinaSupplierStoreSalesDetail>()
            .Where(row => row.Date >= date.Date && row.Date < date.Date.AddDays(1));
        if (branches is { Count: > 0 }) deleteable = deleteable.Where(row => branches.Contains(row.BranchCode));
        if (suppliers is { Count: > 0 }) deleteable = deleteable.Where(row => suppliers.Contains(row.SupplierCode));
        await deleteable.ExecuteCommandAsync();
        if (rows.Count > 0)
            context.Db.Fastest<ChinaSupplierStoreSalesDetail>().PageSize(BatchSize).BulkCopy(rows.ToList());
    }

    private static string ResolveAustralianCode(ProductStoreDailySalesStatistic row)
    {
        var supplier = SalesStatisticsCodeRules.Normalize(row.SupplierCode);
        return supplier == "200"
            ? "200"
            : (string.IsNullOrWhiteSpace(supplier) ? SalesStatisticsCodeRules.UnknownSupplierCode : supplier);
    }
}
