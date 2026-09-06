using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesStatisticsSupplierStoreSummaryTests
{
    [Fact]
    public void 商品快照汇总保留成本缺口并区分三类行数()
    {
        var date = new DateTime(2026, 9, 6);
        var rows = new[]
        {
            NewRow(date, "A", "100", "P1", 10m, 4, 2, 8m, 12m),
            NewRow(date, "A", "100", "P2", 5m, 2, 1, null, null),
        };

        var result = SalesStatisticsSupplierStoreSummaryBuilder.Build(
            rows,
            new Dictionary<string, PosmProductSupplierMapping>(),
            new Dictionary<string, string> { ["100"] = "本地供应商" },
            new Dictionary<string, string>(),
            date,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var summary = Assert.Single(result.Australian);
        Assert.Equal(15m, summary.TotalAmount);
        Assert.Equal(6, summary.TotalQuantity);
        Assert.Equal(3, summary.OrderCount);
        Assert.Equal(2, summary.StatisticRowCount);
        Assert.Equal(1, summary.CostedRowCount);
        Assert.Equal(1, summary.GrossProfitRowCount);
        Assert.Null(summary.TotalCost);
        Assert.Null(summary.GrossProfit);
    }

    [Fact]
    public void 中国旧200与直接码都映射到中国供应商且澳洲归200()
    {
        var date = new DateTime(2026, 9, 6);
        var rows = new[]
        {
            NewRow(date, "A", "200", "P1", 10m, 1, 1, 3m, 7m),
            NewRow(date, "A", "CN-01", "P1", 20m, 2, 1, 6m, 14m),
        };
        var mappings = new Dictionary<string, PosmProductSupplierMapping>(StringComparer.OrdinalIgnoreCase)
        {
            ["P1"] = new PosmProductSupplierMapping
            {
                ProductCode = "P1",
                LocalSupplierCode = "200",
                ChinaSupplierCode = "CN-01",
            },
        };

        var result = SalesStatisticsSupplierStoreSummaryBuilder.Build(
            rows, mappings,
            new Dictionary<string, string> { ["200"] = "国内归属" },
            new Dictionary<string, string> { ["CN-01"] = "中国供应商" },
            date,
            new HashSet<string>(new[] { "CN-01" }, StringComparer.OrdinalIgnoreCase));

        var australian = Assert.Single(result.Australian);
        Assert.Equal("200", australian.SupplierCode);
        Assert.Equal(30m, australian.TotalAmount);
        var china = Assert.Single(result.China);
        Assert.Equal("CN-01", china.SupplierCode);
        Assert.Equal(30m, china.TotalAmount);
        Assert.Equal(2, china.StatisticRowCount);
    }

    [Fact]
    public void 商品版本由快照内容决定且状态helper只读取已发布版本()
    {
        var row = NewRow(new DateTime(2026, 9, 6), "A", "100", "P1", 1m, 1, 1, 1m, 0m);
        var version = SupplierStatisticVersion.ComputeProductVersion(new[] { row });
        Assert.Equal(64, version.Length);
        Assert.Equal(version, SupplierStatisticVersion.GetProductVersion(
            new SalesStatisticRefreshState { SourceProductVersion = version }));
        Assert.Null(SupplierStatisticVersion.GetProductVersion(new SalesStatisticRefreshState()));

        row.TotalAmount = 2m;
        Assert.NotEqual(version, SupplierStatisticVersion.ComputeProductVersion(new[] { row }));
    }

    [Fact]
    public void 历史队列恢复不按当前进价重新定价并保留缺失成本()
    {
        var date = new DateTime(2026, 8, 5);
        var old = NewRow(date, "A", "100", "P1", 20m, 2, 1, 6m, 14m);
        old.UnitCostSnapshot = 3m;
        old.GrossMarginRate = .7m;
        old.CostSource = "StoreCost";
        var unchanged = NewRow(date, "A", "100", "P1", 20m, 2, 1, 18m, 2m);
        unchanged.UnitCostSnapshot = 9m;
        var newHistorical = NewRow(date, "A", "100", "P2", 10m, 1, 1, 9m, 1m);
        newHistorical.UnitCostSnapshot = 9m;
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { unchanged, newHistorical }, new[] { old });
        Assert.Equal(3m, unchanged.UnitCostSnapshot);
        Assert.Equal(6m, unchanged.TotalCost);
        Assert.Equal(14m, unchanged.GrossProfit);
        Assert.Equal(.7m, unchanged.GrossMarginRate);
        Assert.Null(newHistorical.UnitCostSnapshot);
        Assert.Null(newHistorical.TotalCost);
        Assert.Null(newHistorical.GrossProfit);

        var corrected = NewRow(date, "A", "100", "P1", 30m, 3, 1, 27m, 3m);
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(new[] { corrected }, new[] { old });
        Assert.Equal(9m, corrected.TotalCost);
        Assert.Equal(21m, corrected.GrossProfit);
    }

    private static ProductStoreDailySalesStatistic NewRow(
        DateTime date,
        string branch,
        string supplier,
        string product,
        decimal amount,
        int quantity,
        int orderCount,
        decimal? totalCost,
        decimal? grossProfit) => new()
        {
            Date = date,
            BranchCode = branch,
            SupplierCode = supplier,
            ProductCode = product,
            TotalAmount = amount,
            TotalQuantity = quantity,
            OrderCount = orderCount,
            TotalCost = totalCost,
            GrossProfit = grossProfit,
        };
}
