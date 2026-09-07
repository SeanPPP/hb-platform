using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services;

/// <summary>回填只修复缺口；不通过重建销售事实修复成本。</summary>
internal static class SalesCostBackfillRules
{
    internal const string Version = "cost-gap-v1";
    internal sealed record CostImage(decimal? UnitCostSnapshot, decimal? TotalCost,
        decimal? GrossProfit, decimal? GrossMarginRate, string CostSource);
    internal sealed record RowImage(DateTime Date, string BranchCode, string SupplierCode,
        string ProductCode, string? ProductName, string? Barcode, int TotalQuantity,
        decimal TotalAmount, int OrderCount, DateTime? LastSourceUploadTime, DateTime UpdateTime,
        CostImage Cost);

    internal static string Key(ProductStoreDailySalesStatistic row) =>
        $"{row.Date:yyyyMMdd}|{row.BranchCode}|{row.SupplierCode}|{row.ProductCode}";
    internal static CostImage Cost(ProductStoreDailySalesStatistic row) =>
        new(row.UnitCostSnapshot, row.TotalCost, row.GrossProfit, row.GrossMarginRate, row.CostSource);
    internal static RowImage Image(ProductStoreDailySalesStatistic row) => new(row.Date, row.BranchCode,
        row.SupplierCode, row.ProductCode, row.ProductName, row.Barcode, row.TotalQuantity,
        row.TotalAmount, row.OrderCount, row.LastSourceUploadTime, row.UpdateTime, Cost(row));
    internal static string Json(ProductStoreDailySalesStatistic row) => JsonSerializer.Serialize(Image(row));
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static bool NeedsRepair(ProductStoreDailySalesStatistic row) =>
        !row.TotalCost.HasValue || !row.GrossProfit.HasValue
        || (row.TotalAmount > 0 && !row.GrossMarginRate.HasValue);
    internal static bool SameSales(ProductStoreDailySalesStatistic before, ProductStoreDailySalesStatistic after) =>
        Key(before) == Key(after) && before.TotalQuantity == after.TotalQuantity
        && before.TotalAmount == Math.Round(after.TotalAmount, 4, MidpointRounding.AwayFromZero)
        && before.OrderCount == after.OrderCount;

    internal static (CostImage? Cost, string Reason) Propose(ProductStoreDailySalesStatistic old,
        ProductStoreDailySalesStatistic? rebuilt)
    {
        if (!NeedsRepair(old)) return (null, "AlreadyComplete");
        if (string.Equals(rebuilt?.CostSource, "IdentityConflict", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rebuilt?.CostSource, "OpenItemIdentityConflict", StringComparison.OrdinalIgnoreCase))
            return (null, "IdentityConflict");
        decimal? unit = old.UnitCostSnapshot;
        decimal? total = old.TotalCost;
        string source = old.CostSource;
        var openItem = source.StartsWith("OpenItem", StringComparison.OrdinalIgnoreCase)
            || rebuilt?.CostSource.StartsWith("OpenItem", StringComparison.OrdinalIgnoreCase) == true;
        if (!openItem && total.HasValue && unit is > 0
            && total != Math.Round(unit.Value * old.TotalQuantity, 4, MidpointRounding.AwayFromZero))
            return (null, "ExistingCostConflict");
        // 已有金额或历史单价是更强证据；不能用今天的进价覆盖历史快照。
        if (!total.HasValue && unit is > 0 && !openItem)
        {
            total = unit.Value * old.TotalQuantity;
            if (string.IsNullOrWhiteSpace(source) || source == "Missing") source = "HistoricalUnitSnapshot";
        }
        if (!total.HasValue)
        {
            if (rebuilt == null) return (null, "SourceMissing");
            if (!SameSales(old, rebuilt)) return (null, "SourceFactsDiffer");
            if (!rebuilt.TotalCost.HasValue) return (null, "CostEvidenceMissing:" + rebuilt.CostSource);
            if (!openItem && unit.HasValue && unit > 0 && rebuilt.UnitCostSnapshot != unit)
                return (null, "ExistingUnitCostConflict");
            unit = rebuilt.UnitCostSnapshot;
            total = Math.Round(rebuilt.TotalCost.Value, 4, MidpointRounding.AwayFromZero);
            source = rebuilt.CostSource;
        }
        var profit = old.TotalAmount - total.Value;
        // 已有非空派生金额若互相矛盾，不能当作缺口静默覆盖。
        if (old.GrossProfit.HasValue && old.GrossProfit != profit)
            return (null, "ExistingProfitConflict");
        var margin = old.TotalAmount > 0 ? profit / old.TotalAmount : (decimal?)null;
        if (margin.HasValue && old.GrossMarginRate.HasValue
            && Math.Round(old.GrossMarginRate.Value, 4, MidpointRounding.AwayFromZero)
                != Math.Round(margin.Value, 4, MidpointRounding.AwayFromZero))
            return (null, "ExistingMarginConflict");
        if (old.GrossMarginRate.HasValue) margin = old.GrossMarginRate;
        return (new(unit, total, old.GrossProfit ?? profit, margin, source), "VerifiedCostGap");
    }

    internal static void SetCost(ProductStoreDailySalesStatistic row, CostImage cost)
    {
        row.UnitCostSnapshot = cost.UnitCostSnapshot;
        row.TotalCost = cost.TotalCost;
        row.GrossProfit = cost.GrossProfit;
        row.GrossMarginRate = cost.GrossMarginRate;
        row.CostSource = cost.CostSource;
    }

    internal static bool MatchesAfter(ProductStoreDailySalesStatistic row, string afterJson) =>
        Image(row) == JsonSerializer.Deserialize<RowImage>(afterJson);
}
