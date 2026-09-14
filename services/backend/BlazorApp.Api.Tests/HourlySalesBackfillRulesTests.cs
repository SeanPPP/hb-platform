using BlazorApp.Api.Services;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class HourlySalesBackfillRulesTests
{
    private static readonly DateTime Day = new(2025, 9, 15);

    [Fact]
    public void Build_来源订单键隔离且All只汇总分店行()
    {
        var result = HourlySalesBackfillRules.Build(Day,
        [
            new("POSM", "B1", 9, "same", 10m, 1, true),
            new("POSM", "B1", 9, "same", 2m, 0, false),
            new("HBSales", "B1", 9, "same", 5m, 2, true),
            new("POSM", "B2", 9, "other", 7m, 3, true),
        ], new Dictionary<string, HourlySalesBackfillDailyTarget>
        {
            ["B1"] = new(17m, 3, 2, "Glendale"),
            ["B2"] = new(7m, 3, 1),
        }, ["POSM", "HBSales"], [Success("POSM", 3), Success("HBSales", 1)]);

        Assert.True(result.Valid);
        var branch = Assert.Single(result.Rows, x => x.BranchCode == "B1");
        Assert.Equal(17m, branch.TotalAmount);
        Assert.Equal(2, branch.OrderCount);
        Assert.Equal("Glendale", branch.BranchName);
        var all = Assert.Single(result.Rows, x => x.BranchCode == "ALL");
        Assert.Equal(24m, all.TotalAmount);
        Assert.Equal(3, all.OrderCount);
    }

    [Fact]
    public void Build_HBSales缺真实时间时阻断而非落入零点()
    {
        var result = HourlySalesBackfillRules.Build(Day,
            [new("HBSales", "B1", null, "1", 10m, 1, true)],
            new Dictionary<string, HourlySalesBackfillDailyTarget> { ["B1"] = new(10m, 1, 1) },
            ["HBSales"], [Success("HBSales", 1)]);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, x => x.Code == "missing-hour");
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Build_同来源同订单跨小时与跨来源重叠均隔离待审计()
    {
        var result = HourlySalesBackfillRules.Build(Day,
        [
            new("HBSales", "B1", 9, "A", 10m, 1, true),
            new("HBSales", "B1", 10, "A", 5m, 1, true),
            new("POSM", "B1", 11, "Z", 4m, 1, true, "shared-business-key"),
            new("HBSales", "B1", 11, "Y", 4m, 1, true, "shared-business-key"),
        ], new Dictionary<string, HourlySalesBackfillDailyTarget>(),
            ["POSM", "HBSales"], [Success("POSM", 1), Success("HBSales", 3)]);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, x => x.Code == "order-crosses-hours");
        Assert.Contains(result.Issues, x => x.Code == "cross-source-overlap");
    }

    [Fact]
    public void Build_金额或订单与日统计不一致时不得认证()
    {
        var result = HourlySalesBackfillRules.Build(Day,
            [new("POSM", "B1", 9, "1", 10m, 1, true)],
            new Dictionary<string, HourlySalesBackfillDailyTarget> { ["B1"] = new(11m, 1, 2) },
            ["POSM"], [Success("POSM", 1)]);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, x => x.Code == "daily-reconciliation");
    }

    [Fact]
    public void Build_任何必需来源不可用时不得把它当作空销售认证()
    {
        var result = HourlySalesBackfillRules.Build(Day, [],
            new Dictionary<string, HourlySalesBackfillDailyTarget>(), ["POSM", "HBSales"],
            [Empty("POSM"), new("HBSales", HourlySalesBackfillSourceState.Unavailable, 0, "w", "h")]);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, x => x.Code == "source-unavailable");
    }

    [Fact]
    public void Build_缺少必需来源状态时不得认证()
    {
        var result = HourlySalesBackfillRules.Build(Day, [],
            new Dictionary<string, HourlySalesBackfillDailyTarget>(), ["POSM", "HBSales"], [Empty("POSM")]);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, x => x.Code == "source-contract");
    }

    [Fact]
    public void Build_来源声称Empty但有行时不得认证()
    {
        var result = HourlySalesBackfillRules.Build(Day,
            [new("POSM", "B1", 9, "1", 10m, 1, true)],
            new Dictionary<string, HourlySalesBackfillDailyTarget>(), ["POSM"], [Empty("POSM")]);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, x => x.Code == "source-contract");
    }

    [Fact]
    public void Build_候选包含未登记来源时不得参与聚合()
    {
        var result = HourlySalesBackfillRules.Build(Day,
            [new("UNKNOWN", "B1", 9, "1", 10m, 1, true)],
            new Dictionary<string, HourlySalesBackfillDailyTarget>(),
            ["POSM"], [Empty("POSM")]);

        Assert.False(result.Valid);
        Assert.Contains(result.Issues, issue => issue.Code == "source-contract");
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Build_两个必需来源均明确Empty且日表为零时可认证真实零()
    {
        var result = HourlySalesBackfillRules.Build(Day, [],
            new Dictionary<string, HourlySalesBackfillDailyTarget>(), ["POSM", "HBSales"],
            [Empty("POSM"), Empty("HBSales")]);

        Assert.True(result.Valid);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void SourceHash_相同多重集不同查询顺序保持一致()
    {
        HourlySalesBackfillSourceRow[] rows =
        [
            new("POSM", "B2", 10, "2", 5m, 2, true),
            new("POSM", "B1", 9, "1", 3m, 1, false),
            new("POSM", "B1", 9, "1", 3m, 1, false),
        ];

        Assert.Equal(HourlySalesBackfillService.SourceHash(rows),
            HourlySalesBackfillService.SourceHash(rows.Reverse()));
    }

    [Fact]
    public void SnapshotHash_日目标变化但来源行不变时必须变化()
    {
        HourlySalesBackfillSourceRow[] rows = [new("POSM", "B1", 9, "1", 3m, 1, true)];
        var original = HourlySalesBackfillService.SnapshotHash(rows,
            new Dictionary<string, HourlySalesBackfillDailyTarget> { ["B1"] = new(3m, 1, 1) },
            ["POSM"], [Success("POSM", 1)]);
        var changed = HourlySalesBackfillService.SnapshotHash(rows,
            new Dictionary<string, HourlySalesBackfillDailyTarget> { ["B1"] = new(4m, 1, 1) },
            ["POSM"], [Success("POSM", 1)]);

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void SnapshotHash_来源状态变化但来源行不变时必须变化()
    {
        HourlySalesBackfillSourceRow[] rows = [new("POSM", "B1", 9, "1", 3m, 1, true)];
        var targets = new Dictionary<string, HourlySalesBackfillDailyTarget> { ["B1"] = new(3m, 1, 1) };
        var original = HourlySalesBackfillService.SnapshotHash(rows, targets,
            ["POSM"], [Success("POSM", 1)]);
        var changed = HourlySalesBackfillService.SnapshotHash(rows, targets,
            ["POSM"], [new("POSM", HourlySalesBackfillSourceState.Success, 1, "new-watermark", "hash")]);

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void HistoricalSettlementWatermark_跨日审批同一目标日保持稳定()
    {
        var target = new DateTime(2025, 9, 15);

        var preview = HourlySalesBackfillService.HistoricalSettlementWatermark(
            target, new DateTime(2026, 9, 14));
        var apply = HourlySalesBackfillService.HistoricalSettlementWatermark(
            target, new DateTime(2026, 9, 15));

        Assert.Equal(preview, apply);
        Assert.Contains("date:2025-09-15", preview);
    }

    private static HourlySalesBackfillSourceStatus Success(string source, int count) =>
        new(source, HourlySalesBackfillSourceState.Success, count, "watermark", "hash");
    private static HourlySalesBackfillSourceStatus Empty(string source) =>
        new(source, HourlySalesBackfillSourceState.Empty, 0, "watermark", "hash");
}
