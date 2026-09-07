using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>商品分店日统计成本规则的纯领域回归，避免依赖外部数据库。</summary>
public sealed class SalesStatisticsProductStoreDailyCostRulesTests
{
    [Fact]
    public void OpenItem_混合原价按明细逐行求和且不伪造单价()
    {
        var rows = new[]
        {
            OpenItem(2m, price: 25m, subtotal: 50m),
            OpenItem(-1m, price: 10m, subtotal: 10m),
        };

        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            rows, "A", "100", "OPENITEM", [], [], [], null);

        Assert.Equal("OpenItem", result.CostSource);
        Assert.Null(result.UnitCost);
        Assert.Equal(16m, result.TotalCost);
    }

    [Fact]
    public void OpenItem_价格缺失时可由非零原价小计和数量推导且不回退普通成本()
    {
        var rows = new[] { OpenItem(2m, price: null, subtotal: 30m) };
        var productCosts = new Dictionary<string, decimal?> { ["OPENITEM"] = 99m };

        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            rows, "A", "100", "OPENITEM", [], productCosts, [], null);

        Assert.Equal("OpenItem", result.CostSource);
        Assert.Equal(6m, result.UnitCost);
        Assert.Equal(12m, result.TotalCost);
    }

    [Fact]
    public void OpenItem_无价行保留缺口并禁止普通商品成本()
    {
        var rows = new[] { OpenItem(1m, price: null) };
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            rows,
            "A",
            "100",
            "OPENITEM",
            [],
            new Dictionary<string, decimal?> { ["OPENITEM"] = 8m },
            new Dictionary<string, decimal?> { ["OPENITEM"] = 7m },
            null);

        Assert.Equal("OpenItemMissingPrice", result.CostSource);
        Assert.Null(result.UnitCost);
        Assert.Null(result.TotalCost);
    }

    [Fact]
    public void OpenItem_正数单价与原价小计冲突时保留缺口()
    {
        var row = OpenItem(2m, price: 10m, subtotal: 30m);
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { row }, "A", "100", "OPENITEM", [], [], [], null);

        Assert.Equal("OpenItemPriceConflict", result.CostSource);
        Assert.Null(result.UnitCost);
        Assert.Null(result.TotalCost);
    }

    [Fact]
    public void OpenItem_只有正数单价时直接采用原价()
    {
        var row = OpenItem(2m, price: 10m);
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { row }, "A", "100", "OPENITEM", [], [], [], null);

        Assert.Equal("OpenItem", result.CostSource);
        Assert.Equal(4m, result.UnitCost);
        Assert.Equal(8m, result.TotalCost);
    }

    [Fact]
    public void 退货行使用原销售数量推导原价而不是退货数量()
    {
        var row = OpenItem(-1m, price: null, subtotal: 100m);
        row.OriginalSaleQuantity = 10m;
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { row }, "A", "100", "OPENITEM", [], [], [], null);

        Assert.Equal(4m, result.UnitCost);
        Assert.Equal(-4m, result.TotalCost);
    }

    [Fact]
    public void HBSales退货未唯一关联原销售时保留MissingOriginalSale()
    {
        var row = OpenItem( -1m, price: 10m, subtotal: 10m);
        row.IsHBSalesSource = true;
        row.DocumentType = "3";
        row.HBSalesUnitPrice = 10m;
        row.HBSalesOriginalAmount = 10m;
        row.OriginalSaleCostEvidence = false;
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { row }, "A", "100", "OPENITEM", [], [], [], null);

        Assert.Equal("OpenItemMissingOriginalSale", result.CostSource);
        Assert.Null(result.TotalCost);
    }

    [Fact]
    public void 仅货号写入OPENITEM但没有明确LookupCode时不识别为开放商品()
    {
        var row = Ordinary(1m);
        row.ItemNumber = "OPENITEM";
        Assert.False(SalesStatisticsProductStoreDailyDomainRules.IsExplicitOpenItem(row));
    }

    [Fact]
    public void 空商品编码的OPENITEM目录候选必须唯一写回canonical编码()
    {
        Assert.Equal(
            "G006243",
            SalesStatisticsProductStoreDailyDomainRules.ResolveUniqueCanonicalProductCode(
                new[] { "G006243", "G006243" }));
        Assert.Null(
            SalesStatisticsProductStoreDailyDomainRules.ResolveUniqueCanonicalProductCode(
                new[] { "G006243", "OTHER" }));
    }

    [Fact]
    public void 同一分组仅部分明细被OPENITEM确认时返回身份冲突()
    {
        var confirmed = Ordinary(1m);
        confirmed.Barcode = "OPENITEM";
        var unconfirmed = Ordinary(1m);
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { confirmed, unconfirmed },
            "A", "100", "P1", [],
            new Dictionary<string, decimal?> { ["P1"] = 7m },
            [],
            null);

        Assert.Equal("IdentityConflict", result.CostSource);
        Assert.Null(result.UnitCost);
        Assert.Null(result.TotalCost);
    }

    [Fact]
    public void 普通商品允许其它有效分店完全一致正数回退并拒绝冲突回退()
    {
        var costs = new List<StoreCostRow>
        {
            new() { StoreCode = "B", SupplierCode = "100", ProductCode = "P1", PricingUnit = "each", PurchasePrice = 3m },
            new() { StoreCode = "C", SupplierCode = "100", ProductCode = "P1", PricingUnit = "each", PurchasePrice = 3m },
        };
        var productCosts = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        var warehouseCosts = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);

        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { Ordinary(1m, "each") }, "A", "100", "P1", costs, productCosts, warehouseCosts, "each");

        Assert.Equal("StoreRetailPriceFallback", result.CostSource);
        Assert.Equal(3m, result.UnitCost);
        Assert.Equal(3m, result.TotalCost);

        costs.Add(new StoreCostRow
        {
            StoreCode = "D", SupplierCode = "100", ProductCode = "P1", PricingUnit = "each", PurchasePrice = 4m,
        });
        result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { Ordinary(1m, "each") }, "A", "100", "P1", costs, productCosts, warehouseCosts, "each");

        Assert.Equal("StoreRetailPriceConflict", result.CostSource);
        Assert.Null(result.UnitCost);
    }

    [Fact]
    public void 商品和仓库成本优先于跨店回退且单位未知不允许跨店猜测()
    {
        var costs = new List<StoreCostRow>
        {
            new() { StoreCode = "B", SupplierCode = "100", ProductCode = "P1", PricingUnit = "each", PurchasePrice = 3m },
            new() { StoreCode = "C", SupplierCode = "100", ProductCode = "P1", PricingUnit = "each", PurchasePrice = 3m },
        };
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { Ordinary(1m, "each") }, "A", "100", "P1", costs,
            new Dictionary<string, decimal?> { ["P1"] = 7m }, [], "each");
        Assert.Equal("ProductPurchasePrice", result.CostSource);
        Assert.Equal(7m, result.UnitCost);

        result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { Ordinary(1m) }, "A", "100", "P1", costs, [], [], null);
        Assert.Equal("MissingStoreCostUnit", result.CostSource);
    }

    [Fact]
    public void 跨店候选先核验全部单位身份而不是筛掉异单位行()
    {
        var costs = new[]
        {
            new StoreCostRow { StoreCode = "B", SupplierCode = "100", ProductCode = "P1", PricingUnit = "each", PurchasePrice = 3m },
            new StoreCostRow { StoreCode = "C", SupplierCode = "100", ProductCode = "P1", PricingUnit = "box", PurchasePrice = 3m },
        };
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { Ordinary(1m, "each") }, "A", "100", "P1", costs, [], [], "each");

        Assert.Equal("StoreRetailPriceUnitConflict", result.CostSource);
        Assert.Null(result.TotalCost);
    }

    [Fact]
    public void 同一商品来源计价单位冲突时不采用第一行单位成本()
    {
        var rows = new[] { Ordinary(1m, "each"), Ordinary(1m, "box") };
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            rows,
            "A",
            "100",
            "P1",
            new[] { new StoreCostRow
            {
                StoreCode = "A", SupplierCode = "100", ProductCode = "P1", PurchasePrice = 3m,
            } },
            [],
            [],
            "each");

        Assert.Equal("StoreRetailPriceUnitConflict", result.CostSource);
        Assert.Null(result.UnitCost);
        Assert.Null(result.TotalCost);
    }

    [Fact]
    public void 普通商品成本沿用整数销量口径而不是原始小数数量()
    {
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { Ordinary(1.9m) },
            "A", "100", "P1",
            new[] { new StoreCostRow { StoreCode = "A", SupplierCode = "100", ProductCode = "P1", PurchasePrice = 3m } },
            [], [], null);

        Assert.Equal(3m, result.TotalCost);
    }

    [Fact]
    public void 历史当前分店可使用停用但未删除的正数进价()
    {
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { Ordinary(1m) },
            "A", "100", "P1",
            new[] { new StoreCostRow
            {
                StoreCode = "A", SupplierCode = "100", ProductCode = "P1",
                IsActive = false, PurchasePrice = 3m,
            } },
            [], [], null);

        Assert.Equal("StoreRetailPrice", result.CostSource);
        Assert.Equal(3m, result.UnitCost);
        Assert.Equal(3m, result.TotalCost);
    }

    [Fact]
    public void 悉尼业务日跨UTC午夜仍按本地日期判断()
    {
        Assert.Equal(
            new DateTime(2026, 9, 7),
            SalesStatisticsBusinessDate.GetBusinessDate(new DateTimeOffset(2026, 9, 6, 23, 48, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void 悉尼夏令时切换后的UTC时间使用悉尼业务日期()
    {
        // 2026-10-04 悉尼进入夏令时；该 UTC 时间已是悉尼次日凌晨，不能按 UTC 日期。
        Assert.Equal(
            new DateTime(2026, 10, 5),
            SalesStatisticsBusinessDate.GetBusinessDate(new DateTimeOffset(2026, 10, 4, 13, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void 停用跨店价格不能作为有效分店一致回退()
    {
        var result = SalesStatisticsProductStoreDailyDomainRules.ResolveCost(
            new[] { Ordinary(1m, "each") },
            "A", "100", "P1",
            new[] { new StoreCostRow
            {
                StoreCode = "B", SupplierCode = "100", ProductCode = "P1",
                PricingUnit = "each", IsActive = false, PurchasePrice = 3m,
            } },
            [], [], "each");

        Assert.Equal("Missing", result.CostSource);
        Assert.Null(result.UnitCost);
        Assert.Null(result.TotalCost);
    }

    [Fact]
    public void 历史普通商品事实变化按旧单价派生且开放商品不复用旧总成本()
    {
        var date = new DateTime(2026, 9, 7);
        var oldOrdinary = new ProductStoreDailySalesStatistic
        {
            Date = date, BranchCode = "A", SupplierCode = "100", ProductCode = "P1",
            TotalQuantity = 2, TotalAmount = 20m, UnitCostSnapshot = 3m,
            TotalCost = 6m, GrossProfit = 14m, CostSource = "StoreRetailPrice",
        };
        var rebuiltOrdinary = new ProductStoreDailySalesStatistic
        {
            Date = date, BranchCode = "A", SupplierCode = "100", ProductCode = "P1",
            TotalQuantity = 4, TotalAmount = 40m, CostSource = "Missing",
        };
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { rebuiltOrdinary }, new[] { oldOrdinary });
        Assert.Equal(3m, rebuiltOrdinary.UnitCostSnapshot);
        Assert.Equal(12m, rebuiltOrdinary.TotalCost);
        Assert.Equal(28m, rebuiltOrdinary.GrossProfit);

        var oldOpenItem = new ProductStoreDailySalesStatistic
        {
            Date = date, BranchCode = "A", SupplierCode = "100", ProductCode = "OPENITEM",
            TotalQuantity = 2, TotalAmount = 20m, UnitCostSnapshot = 4m,
            TotalCost = 8m, GrossProfit = 12m, CostSource = "OpenItem",
        };
        var rebuiltOpenItem = new ProductStoreDailySalesStatistic
        {
            Date = date, BranchCode = "A", SupplierCode = "100", ProductCode = "OPENITEM",
            TotalQuantity = 3, TotalAmount = 30m, CostSource = "OpenItem",
        };
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { rebuiltOpenItem }, new[] { oldOpenItem });
        Assert.Equal("OpenItem", rebuiltOpenItem.CostSource);
        Assert.Null(rebuiltOpenItem.UnitCostSnapshot);
        Assert.Null(rebuiltOpenItem.TotalCost);
        Assert.Null(rebuiltOpenItem.GrossProfit);
    }

    private static ProductStoreDailySourceRow OpenItem(
        decimal quantity,
        decimal? price,
        decimal? subtotal = null) => new()
        {
            ProductCode = "OPENITEM",
            Barcode = "OPENITEM",
            Price = price,
            Subtotal = subtotal,
            OriginalSaleQuantity = Math.Abs(quantity),
            Quantity = quantity,
        };

    private static ProductStoreDailySourceRow Ordinary(decimal quantity, string? unit = null) => new()
    {
        ProductCode = "P1",
        PricingUnit = unit,
        Quantity = quantity,
    };
}
