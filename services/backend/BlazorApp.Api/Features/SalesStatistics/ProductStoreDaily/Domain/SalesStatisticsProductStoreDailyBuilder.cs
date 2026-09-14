using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Services;

/// <summary>商品分店日统计领域聚合：不访问数据库，也不管理事务。</summary>
internal sealed class SalesStatisticsProductStoreDailyBuilder
{
    internal ProductStoreDailyRefreshBuildResult Build(ProductStoreDailyRefreshInput input)
    {
        var productCostMap = input.ProductCosts.Where(row => !string.IsNullOrWhiteSpace(row.ProductCode))
            .GroupBy(row => row.ProductCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.Select(row => row.PurchasePrice).FirstOrDefault(price => price is > 0));
        var warehouseCostMap = input.WarehouseCosts.Where(row => !string.IsNullOrWhiteSpace(row.ProductCode))
            .GroupBy(row => row.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.Select(row => row.ImportPrice).FirstOrDefault(price => price is > 0));
        // 每个统计商品只需看到自己的分店成本行；先按规范化商品编码建索引，避免每个统计分组重复扫描全量分店价格。
        var storeCostMap = input.StoreCosts
            .Where(row => !string.IsNullOrWhiteSpace(row.ProductCode))
            .GroupBy(row => row.ProductCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StoreCostRow>)group.ToList(),
                StringComparer.OrdinalIgnoreCase
            );
        // POSM 明细必须按订单支付金额分摊；HBSales 与补充退货行继续使用自身金额。
        var resolvedRows = input.RawRows.Select(row => new ProductStoreDailyResolvedRow(
                row,
                SalesStatisticsCodeRules.ResolveBranchCode(
                    row.BranchCode,
                    row.DeviceCode,
                    input.DeviceBranchMap
                ),
                row.IsHBSalesSource || input.SupplementalReturnRows.Contains(row)
                    ? row.ActualAmount
                    : SalesStatisticsProductStoreDailyDomainRules.ResolveStatisticAmount(
                        row.OrderGuid,
                        row.ActualAmount,
                        input.PaymentAmounts,
                        input.DetailAmounts
                    )))
            .Where(row => !string.IsNullOrWhiteSpace(row.BranchCode) && !string.IsNullOrWhiteSpace(row.Row.ProductCode))
            .ToList();
        var diagnostics = BuildDiagnostics(resolvedRows);
        var statistics = resolvedRows.GroupBy(row => new ProductStoreDailyGroupKey(
                row.Row.Date,
                row.BranchCode,
                SalesStatisticsProductStoreDailyDomainRules.ResolveStatisticSupplierCode(
                    row.Row.SupplierCode,
                    null
                ),
                row.Row.ProductCode!.Trim()))
            .Select(group => BuildStatistic(
                group,
                storeCostMap.TryGetValue(group.Key.ProductCode, out var matchingStoreCosts)
                    ? matchingStoreCosts
                    : Array.Empty<StoreCostRow>(),
                productCostMap,
                warehouseCostMap,
                input.LastSourceUploadTime))
            .ToList();
        var returnAdjustments = resolvedRows.Where(row => input.SupplementalReturnRows.Contains(row.Row))
            .GroupBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProductStoreDailyBranchRollup(group.Key, group.Sum(row => row.StatisticAmount), (int)group.Sum(row => row.Row.Quantity)))
            .ToList();
        return new ProductStoreDailyRefreshBuildResult(statistics, diagnostics, returnAdjustments);
    }

    private static ProductStatisticDiagnostics BuildDiagnostics(IReadOnlyList<ProductStoreDailyResolvedRow> resolvedRows)
    {
        var unmatchedRows = resolvedRows.Where(row => string.IsNullOrWhiteSpace(row.Row.SupplierCode)).ToList();
        var diagnostics = new ProductStatisticDiagnostics();
        if (unmatchedRows.Count == 0)
            return diagnostics;

        diagnostics.UnmatchedSupplierAmount = unmatchedRows.Sum(row => row.StatisticAmount);
        diagnostics.UnmatchedSupplierQuantity = (int)unmatchedRows.Sum(row => row.Row.Quantity);
        diagnostics.UnmatchedSupplierProductCount = unmatchedRows.Select(row => row.Row.ProductCode).Distinct().Count();
        diagnostics.BranchDiagnostics = unmatchedRows.GroupBy(row => row.BranchCode)
            .ToDictionary(group => group.Key, group => new ProductStatisticDiagnosticRow
            {
                BranchCode = group.Key,
                UnmatchedSupplierAmount = group.Sum(row => row.StatisticAmount),
                UnmatchedSupplierQuantity = (int)group.Sum(row => row.Row.Quantity),
                UnmatchedSupplierProductCount = group.Select(row => row.Row.ProductCode).Distinct().Count(),
            });
        return diagnostics;
    }

    private static ProductStoreDailySalesStatistic BuildStatistic(
        IGrouping<ProductStoreDailyGroupKey, ProductStoreDailyResolvedRow> group,
        IReadOnlyList<StoreCostRow> storeCosts,
        Dictionary<string, decimal?> productCostMap,
        Dictionary<string, decimal?> warehouseCostMap,
        DateTime? lastSourceUploadTime)
    {
        // HBSales 数量先按商品键累加，再按既有年度口径截断为整数。
        var sourceQuantity = group.Sum(row => row.Row.Quantity);
        var quantity = (int)sourceQuantity;
        var totalAmount = group.Sum(row => row.StatisticAmount);
        var sourceRows = group.Select(row => row.Row).ToList();
        // 成本按来源逐行解析：OpenItem 使用原明细价格，普通商品才进入成本表优先级。
        var cost = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            sourceRows,
            group.Key.BranchCode,
            group.Key.SupplierCode,
            group.Key.ProductCode,
            storeCosts,
            productCostMap,
            warehouseCostMap,
            sourceRows.Select(row => row.PricingUnit)
                .FirstOrDefault(unit => !string.IsNullOrWhiteSpace(unit))
        );
        var unitCost = cost.UnitCost;
        var totalCost = cost.TotalCost;
        var grossProfit = totalCost.HasValue ? totalAmount - totalCost.Value : (decimal?)null;
        return new ProductStoreDailySalesStatistic
        {
            Date = group.Key.Date,
            BranchCode = group.Key.BranchCode,
            SupplierCode = group.Key.SupplierCode,
            ProductCode = group.Key.ProductCode,
            ProductName = group.Select(row => row.Row.ProductName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
            Barcode = group.Select(row => row.Row.Barcode).FirstOrDefault(barcode => !string.IsNullOrWhiteSpace(barcode)),
            TotalQuantity = quantity,
            TotalAmount = totalAmount,
            OrderCount = group.Select(row => row.Row.OrderGuid).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Count(),
            UnitCostSnapshot = unitCost,
            TotalCost = totalCost,
            GrossProfit = grossProfit,
            GrossMarginRate = totalAmount > 0m && grossProfit.HasValue ? grossProfit.Value / totalAmount : null,
            CostSource = cost.CostSource,
            LastSourceUploadTime = group.SelectMany(row => new[] { row.Row.OrderLastUploadTime, row.Row.DetailLastUploadTime })
                .Where(value => value.HasValue).Select(value => value!.Value)
                .DefaultIfEmpty(lastSourceUploadTime ?? DateTime.MinValue).Max(),
            UpdateTime = DateTime.Now,
        };
    }
}

internal sealed record ProductStoreDailyRefreshInput(
    DateTime TargetDate,
    IReadOnlyList<ProductStoreDailySourceRow> RawRows,
    HashSet<ProductStoreDailySourceRow> SupplementalReturnRows,
    Dictionary<string, decimal> PaymentAmounts,
    Dictionary<string, decimal> DetailAmounts,
    Dictionary<string, string> DeviceBranchMap,
    IReadOnlyList<StoreCostRow> StoreCosts,
    IReadOnlyList<ProductCostRow> ProductCosts,
    IReadOnlyList<WarehouseCostRow> WarehouseCosts,
    DateTime? LastSourceUploadTime);

internal sealed record ProductStoreDailyResolvedRow(ProductStoreDailySourceRow Row, string BranchCode, decimal StatisticAmount);
internal sealed record ProductStoreDailyGroupKey(DateTime Date, string BranchCode, string SupplierCode, string ProductCode);
internal sealed record ProductStoreDailyRefreshBuildResult(
    List<ProductStoreDailySalesStatistic> Statistics,
    ProductStatisticDiagnostics Diagnostics,
    IReadOnlyList<ProductStoreDailyBranchRollup> SupplementalReturnAdjustments);
