using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesCostBackfillTests
{
    internal static ProductStoreDailySalesStatistic Row() => new()
    {
        Date = new DateTime(2026, 9, 6), BranchCode = "1015", SupplierCode = "240", ProductCode = "P1",
        ProductName = "保留商品名称", Barcode = "72750", TotalQuantity = 2, TotalAmount = 9.98m,
        OrderCount = 1, LastSourceUploadTime = new DateTime(2026, 9, 6, 12, 0, 0),
        UpdateTime = new DateTime(2026, 9, 7, 1, 0, 0), CostSource = "Missing",
    };

    [Fact]
    public void 库内直写国内编码的缺口行能配对到仍是200的重建行并给出成本提案()
    {
        var chinaCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CN-A" };
        // 库内行已直写国内供应商编码，成本缺失；成本回填重建的行不解析归属，国内货仍是 200。
        var stored = Row(); stored.SupplierCode = "CN-A";
        var rebuilt = Row(); rebuilt.SupplierCode = "200";
        rebuilt.UnitCostSnapshot = 2m; rebuilt.TotalCost = 4m; rebuilt.CostSource = "StoreRetailPrice";

        var calculated = SalesCostBackfillRules.BuildRebuiltLookup(new[] { rebuilt }, chinaCodes)(stored);
        var proposal = SalesCostBackfillRules.Propose(stored, calculated);

        // 行键里的编码对不上时，这里会得到 SourceMissing 或 SourceFactsDiffer，缺口永远补不上。
        Assert.Equal("VerifiedCostGap", proposal.Reason);
        Assert.Equal(4m, proposal.Cost!.TotalCost);
        Assert.Equal("StoreRetailPrice", proposal.Cost.CostSource);
        // 回填只改成本列，库内行的归属保持不变。
        Assert.Equal("CN-A", stored.SupplierCode);
    }

    [Fact]
    public void 重建行配对只在国内编码族内折叠且族内多行时不配对()
    {
        var chinaCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CN-A", "CN-B" };
        var stored = Row(); stored.SupplierCode = "200";

        // 澳洲供应商的重建行（Row 默认 240）不能配给国内货的缺口行。
        Assert.Null(SalesCostBackfillRules.BuildRebuiltLookup(new[] { Row() }, chinaCodes)(stored));

        // 库内行是澳洲供应商时也不折叠。
        var rebuilt200 = Row(); rebuilt200.SupplierCode = "200";
        Assert.Null(SalesCostBackfillRules.BuildRebuiltLookup(new[] { rebuilt200 }, chinaCodes)(Row()));

        // 精确键仍然优先。
        Assert.Same(rebuilt200, SalesCostBackfillRules.BuildRebuiltLookup(new[] { rebuilt200 }, chinaCodes)(stored));

        // 族内同一商品有两条重建行，无法确定对应关系，不配对。
        var rebuiltA = Row(); rebuiltA.SupplierCode = "CN-A";
        var rebuiltB = Row(); rebuiltB.SupplierCode = "CN-B";
        Assert.Null(SalesCostBackfillRules.BuildRebuiltLookup(new[] { rebuiltA, rebuiltB }, chinaCodes)(stored));

        // 没有国内编码目录时只做精确匹配。
        Assert.Null(SalesCostBackfillRules.BuildRebuiltLookup(new[] { rebuiltA }, null)(stored));
    }

    [Fact]
    public void 已有单位快照优先且成本修复不改销售事实()
    {
        var row = Row(); row.UnitCostSnapshot = 1.94m; row.CostSource = "StoreRetailPrice";
        var before = SalesCostBackfillRules.Image(row);
        var fresh = Row(); fresh.UnitCostSnapshot = 9m; fresh.TotalCost = 18m;
        var proposal = SalesCostBackfillRules.Propose(row, fresh);
        Assert.Equal(3.88m, proposal.Cost!.TotalCost);
        Assert.Equal(6.10m, proposal.Cost.GrossProfit);
        SalesCostBackfillRules.SetCost(row, proposal.Cost);
        Assert.Equal(before with { Cost = SalesCostBackfillRules.Cost(row) }, SalesCostBackfillRules.Image(row));
        Assert.False(SalesCostBackfillRules.NeedsRepair(row));
        Assert.Equal("AlreadyComplete", SalesCostBackfillRules.Propose(row, fresh).Reason);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-2, -10, -4)]
    [InlineData(2, 4, 4)]
    public void 零成本合计净退货与零毛利不是缺口(int quantity, int amount, int cost)
    {
        var row = Row(); row.TotalQuantity = quantity; row.TotalAmount = amount;
        row.TotalCost = cost; row.GrossProfit = amount - cost;
        row.GrossMarginRate = amount > 0 ? (amount - cost) / (decimal)amount : null;
        Assert.False(SalesCostBackfillRules.NeedsRepair(row));
    }

    [Fact]
    public void 混合原价OpenItem允许无单价快照且从总成本补毛利()
    {
        var row = Row(); row.CostSource = "OpenItemOriginalPrice"; row.TotalCost = 6m;
        var proposal = SalesCostBackfillRules.Propose(row, null);
        Assert.Null(proposal.Cost!.UnitCostSnapshot);
        Assert.Equal(3.98m, proposal.Cost.GrossProfit);
    }

    [Fact]
    public void OpenItem旧单价不能阻止按原明细补齐混合成本()
    {
        var row = Row(); row.UnitCostSnapshot = 1m; row.CostSource = "OpenItem";
        var rebuilt = Row(); rebuilt.CostSource = "OpenItem"; rebuilt.TotalCost = 6m;
        var proposal = SalesCostBackfillRules.Propose(row, rebuilt);
        Assert.NotNull(proposal.Cost); Assert.Null(proposal.Cost.UnitCostSnapshot);
        Assert.Equal(6m, proposal.Cost.TotalCost);
    }

    [Fact]
    public void 明确身份冲突不能被旧单位成本掩盖()
    {
        var row = Row(); row.UnitCostSnapshot = 1m;
        var rebuilt = Row(); rebuilt.CostSource = "IdentityConflict";
        Assert.Equal("IdentityConflict", SalesCostBackfillRules.Propose(row, rebuilt).Reason);
        Assert.Null(SalesCostBackfillRules.Propose(row, rebuilt).Cost);
    }

    [Fact]
    public void 原销售事实改变或已有毛利冲突必须输出原因()
    {
        var row = Row(); var rebuilt = Row(); rebuilt.TotalQuantity++; rebuilt.TotalCost = 4m;
        Assert.Equal("SourceFactsDiffer", SalesCostBackfillRules.Propose(row, rebuilt).Reason);
        row.TotalCost = 4m; row.GrossProfit = 1m;
        Assert.Equal("ExistingProfitConflict", SalesCostBackfillRules.Propose(row, null).Reason);
    }

    [Fact]
    public void 回滚比较包含成本销售事实和修改时间()
    {
        var row = Row(); var image = SalesCostBackfillRules.Json(row);
        Assert.True(SalesCostBackfillRules.MatchesAfter(row, image));
        row.UpdateTime = row.UpdateTime.AddSeconds(1);
        Assert.False(SalesCostBackfillRules.MatchesAfter(row, image));
        row = Row(); row.TotalAmount += 1m;
        Assert.False(SalesCostBackfillRules.MatchesAfter(row, image));
        row = Row(); row.UnitCostSnapshot = 1m;
        Assert.False(SalesCostBackfillRules.MatchesAfter(row, image));
    }

    [Fact]
    public void 已有单位总额及毛利率矛盾时输出异常()
    {
        var row = Row(); row.UnitCostSnapshot = 4m; row.TotalCost = 1m;
        Assert.Equal("ExistingCostConflict", SalesCostBackfillRules.Propose(row, null).Reason);
        row = Row(); row.TotalCost = 3.88m; row.GrossMarginRate = 0.1m;
        Assert.Equal("ExistingMarginConflict", SalesCostBackfillRules.Propose(row, null).Reason);
        row.GrossMarginRate = Math.Round(6.1m / 9.98m, 4, MidpointRounding.AwayFromZero);
        Assert.NotNull(SalesCostBackfillRules.Propose(row, null).Cost);
    }
}
