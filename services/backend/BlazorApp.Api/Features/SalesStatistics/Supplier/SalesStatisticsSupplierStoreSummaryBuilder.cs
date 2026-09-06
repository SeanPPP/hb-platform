using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Services;

/// <summary>
/// 从已完成的商品分店日快照生成两张供应商分店汇总表。
/// 该类只做纯聚合，不查询成本，也不执行写入。
/// </summary>
internal static class SalesStatisticsSupplierStoreSummaryBuilder
{
    internal static SupplierStoreStatisticBuildResult Build(
        IEnumerable<ProductStoreDailySalesStatistic> productStatistics,
        IReadOnlyDictionary<string, PosmProductSupplierMapping> mappingsByProduct,
        IReadOnlyDictionary<string, string> localSupplierNames,
        IReadOnlyDictionary<string, string> chinaSupplierNames,
        DateTime updateTime,
        IReadOnlySet<string>? chinaSupplierCodes = null,
        IReadOnlyCollection<string>? branchCodes = null,
        IReadOnlyCollection<string>? supplierCodes = null)
    {
        var branches = branchCodes is null
            ? null
            : branchCodes.Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suppliers = supplierCodes is null
            ? null
            : supplierCodes.Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = productStatistics
            .Where(row => !string.IsNullOrWhiteSpace(row.BranchCode))
            .Where(row => branches is null || branches.Contains(row.BranchCode.Trim()))
            .Select(row => Resolve(row, mappingsByProduct, chinaSupplierCodes))
            .ToList();

        var australian = BuildAustralian(rows, localSupplierNames, updateTime, suppliers);
        var china = BuildChina(rows, chinaSupplierNames, updateTime, suppliers);
        return new SupplierStoreStatisticBuildResult(australian, china);
    }

    private static List<AustralianSupplierStoreSalesDetail> BuildAustralian(
        IEnumerable<ResolvedSupplierRow> rows,
        IReadOnlyDictionary<string, string> names,
        DateTime updateTime,
        IReadOnlySet<string>? supplierFilter)
    {
        return rows.Where(row => supplierFilter is null || supplierFilter.Contains(row.AustralianSupplierCode))
            .GroupBy(row => new { row.Statistic.Date, BranchCode = row.Statistic.BranchCode.Trim(), SupplierCode = row.AustralianSupplierCode })
            .Select(group =>
            {
                var source = group.Select(item => item.Statistic).ToList();
                var supplierCode = group.Key.SupplierCode;
                return new AustralianSupplierStoreSalesDetail
                {
                    Date = group.Key.Date,
                    BranchCode = group.Key.BranchCode,
                    SupplierCode = supplierCode,
                    SupplierName = names.TryGetValue(supplierCode, out var name)
                        ? name
                        : supplierCode == SalesStatisticsCodeRules.UnknownSupplierCode ? "未匹配供应商" : supplierCode,
                    TotalAmount = source.Sum(row => row.TotalAmount),
                    TotalQuantity = source.Sum(row => row.TotalQuantity),
                    OrderCount = source.Sum(row => row.OrderCount),
                    TotalCost = source.All(row => row.TotalCost.HasValue)
                        ? source.Sum(row => row.TotalCost!.Value)
                        : null,
                    GrossProfit = source.All(row => row.GrossProfit.HasValue)
                        ? source.Sum(row => row.GrossProfit!.Value)
                        : null,
                    StatisticRowCount = source.Count,
                    CostedRowCount = source.Count(row => row.TotalCost.HasValue),
                    GrossProfitRowCount = source.Count(row => row.GrossProfit.HasValue),
                    UpdateTime = updateTime,
                };
            }).ToList();
    }

    private static List<ChinaSupplierStoreSalesDetail> BuildChina(
        IEnumerable<ResolvedSupplierRow> rows,
        IReadOnlyDictionary<string, string> names,
        DateTime updateTime,
        IReadOnlySet<string>? supplierFilter)
    {
        return rows.Where(row => row.ChinaSupplierCode is not null)
            .Where(row => supplierFilter is null || supplierFilter.Contains(row.ChinaSupplierCode!))
            .GroupBy(row => new { row.Statistic.Date, BranchCode = row.Statistic.BranchCode.Trim(), SupplierCode = row.ChinaSupplierCode! })
            .Select(group =>
            {
                var source = group.Select(item => item.Statistic).ToList();
                var supplierCode = group.Key.SupplierCode;
                return new ChinaSupplierStoreSalesDetail
                {
                    Date = group.Key.Date,
                    BranchCode = group.Key.BranchCode,
                    SupplierCode = supplierCode,
                    SupplierName = names.TryGetValue(supplierCode, out var name) ? name : supplierCode,
                    TotalAmount = source.Sum(row => row.TotalAmount),
                    TotalQuantity = source.Sum(row => row.TotalQuantity),
                    OrderCount = source.Sum(row => row.OrderCount),
                    TotalCost = source.All(row => row.TotalCost.HasValue)
                        ? source.Sum(row => row.TotalCost!.Value)
                        : null,
                    GrossProfit = source.All(row => row.GrossProfit.HasValue)
                        ? source.Sum(row => row.GrossProfit!.Value)
                        : null,
                    StatisticRowCount = source.Count,
                    CostedRowCount = source.Count(row => row.TotalCost.HasValue),
                    GrossProfitRowCount = source.Count(row => row.GrossProfit.HasValue),
                    UpdateTime = updateTime,
                };
            }).ToList();
    }

    private static ResolvedSupplierRow Resolve(
        ProductStoreDailySalesStatistic statistic,
        IReadOnlyDictionary<string, PosmProductSupplierMapping> mappingsByProduct,
        IReadOnlySet<string>? chinaSupplierCodes)
    {
        var supplierCode = SalesStatisticsCodeRules.Normalize(statistic.SupplierCode);
        var productCode = SalesStatisticsCodeRules.Normalize(statistic.ProductCode);
        mappingsByProduct.TryGetValue(productCode, out var mapping);
        var chinaCode = SalesStatisticsCodeRules.Normalize(mapping?.ChinaSupplierCode);
        var isLegacyChina = supplierCode == "200";
        var isDirectChina = chinaSupplierCodes?.Contains(supplierCode) == true;

        return new ResolvedSupplierRow(
            statistic,
            isLegacyChina || isDirectChina
                ? "200"
                : (string.IsNullOrWhiteSpace(supplierCode) ? SalesStatisticsCodeRules.UnknownSupplierCode : supplierCode),
            isLegacyChina
                ? (!string.IsNullOrWhiteSpace(chinaCode) ? chinaCode : null)
                : (isDirectChina ? supplierCode : null));
    }

    private sealed record ResolvedSupplierRow(
        ProductStoreDailySalesStatistic Statistic,
        string AustralianSupplierCode,
        string? ChinaSupplierCode);
}

internal sealed record SupplierStoreStatisticBuildResult(
    List<AustralianSupplierStoreSalesDetail> Australian,
    List<ChinaSupplierStoreSalesDetail> China);
