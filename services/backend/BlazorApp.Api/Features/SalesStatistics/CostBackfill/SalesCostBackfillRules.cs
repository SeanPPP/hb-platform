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

    /// <summary>
    /// 为库内缺口行找到对应的重建行。库内行的 SupplierCode 可能是直写的国内供应商编码，
    /// 而成本回填重建的行不解析归属（国内货仍是 200），反过来也可能；行键里的 SupplierCode 对不上，
    /// 提案就会因「来源缺失」或「来源事实不一致」丢掉。国内编码族内改按「日期 + 分店 + 商品」配对，
    /// 并把重建行的编码对齐到库内行：重建行只用于取成本，回填只改成本列，不改归属。
    /// 族内同一商品有多条重建行时无法确定对应关系，不配对。
    /// </summary>
    internal static Func<ProductStoreDailySalesStatistic, ProductStoreDailySalesStatistic?> BuildRebuiltLookup(
        IEnumerable<ProductStoreDailySalesStatistic> rebuilt,
        IReadOnlySet<string>? chinaSupplierCodes)
    {
        var rebuiltRows = rebuilt.ToList();
        var byKey = rebuiltRows.ToDictionary(Key);
        var familyRows = chinaSupplierCodes == null
            ? new Dictionary<(DateTime, string, string), ProductStoreDailySalesStatistic>()
            : rebuiltRows
                .Where(row => ChinaSupplierCodeFamily.IsFamilyCode(row.SupplierCode, chinaSupplierCodes))
                .GroupBy(row => (row.Date.Date, row.BranchCode, row.ProductCode))
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());

        return stored =>
        {
            if (byKey.TryGetValue(Key(stored), out var exact))
                return exact;
            if (chinaSupplierCodes == null
                || !ChinaSupplierCodeFamily.IsFamilyCode(stored.SupplierCode, chinaSupplierCodes)
                || !familyRows.TryGetValue((stored.Date.Date, stored.BranchCode, stored.ProductCode), out var family))
                return null;
            family.SupplierCode = stored.SupplierCode;
            return family;
        };
    }

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
