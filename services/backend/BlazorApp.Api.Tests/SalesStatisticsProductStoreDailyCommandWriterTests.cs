using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>商品分店每日统计 writer 的纯回归测试，不依赖数据库或外部来源。</summary>
public sealed class SalesStatisticsProductStoreDailyCommandWriterTests
{
    [Fact]
    public void 历史入口不再要求队列JobId才保留成本快照()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "services/backend/BlazorApp.Api/Features/SalesStatistics/ProductStoreDaily/Infrastructure/" +
            "SalesStatisticsProductStoreDailyCommandWriter.cs"));

        Assert.Contains("if (input.TargetDate.Date < SalesStatisticsBusinessDate.Today())", source);
        Assert.DoesNotContain(
            "expectedJobId.HasValue && input.TargetDate.Date < SalesStatisticsBusinessDate.Today()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void 普通商品历史正数单价缺少旧总成本时补齐派生值()
    {
        var old = Statistic(
            productCode: "P1",
            quantity: 2,
            amount: 20m,
            unitCost: 3m,
            totalCost: null,
            source: "ProductPurchasePrice");
        var rebuilt = Statistic(
            productCode: "P1",
            quantity: 2,
            amount: 20m,
            unitCost: null,
            totalCost: null,
            source: "Missing");

        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { rebuilt }, new[] { old });

        Assert.Equal(3m, rebuilt.UnitCostSnapshot);
        Assert.Equal(6m, rebuilt.TotalCost);
        Assert.Equal(14m, rebuilt.GrossProfit);
        Assert.Equal(0.7m, rebuilt.GrossMarginRate);
    }

    [Fact]
    public void 历史事实比较按落库四位金额并包含订单数()
    {
        var old = Statistic("P1", 2, 10m, 3m, 6m, "ProductPurchasePrice");
        old.GrossProfit = null;
        old.GrossMarginRate = null;
        var sameAfterStorageRounding = Statistic("P1", 2, 10.00004m, 99m, 198m, "ProductPurchasePrice");

        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { sameAfterStorageRounding }, new[] { old });

        Assert.Equal(6m, sameAfterStorageRounding.TotalCost);
        Assert.Equal(4.00004m, sameAfterStorageRounding.GrossProfit);
        Assert.Equal(4.00004m / 10.00004m, sameAfterStorageRounding.GrossMarginRate);

        var changedOrderCount = Statistic("P1", 2, 10m, 99m, 198m, "ProductPurchasePrice");
        changedOrderCount.OrderCount = 2;
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { changedOrderCount }, new[] { old });

        Assert.Equal(3m, changedOrderCount.UnitCostSnapshot);
        Assert.Equal(6m, changedOrderCount.TotalCost);
        Assert.Equal(4m, changedOrderCount.GrossProfit);
    }

    [Fact]
    public void 已有合法零值派生字段时完整保留零值()
    {
        var old = Statistic("P0", 1, 0m, 0m, 0m, "ProductPurchasePrice");
        old.GrossProfit = 0m;
        old.GrossMarginRate = 0m;
        var rebuilt = Statistic("P0", 1, 0m, 8m, 8m, "ProductPurchasePrice");

        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { rebuilt }, new[] { old });

        Assert.Equal(0m, rebuilt.TotalCost);
        Assert.Equal(0m, rebuilt.GrossProfit);
        Assert.Equal(0m, rebuilt.GrossMarginRate);
    }

    [Fact]
    public void OpenItem混合原价有可信新明细时保留逐行重算结果并允许单价为空()
    {
        var old = Statistic(
            productCode: "OPENITEM",
            quantity: 3,
            amount: 30m,
            unitCost: null,
            totalCost: 8m,
            source: "OpenItem");
        var rebuilt = Statistic(
            productCode: "OPENITEM",
            quantity: 3,
            amount: 30m,
            unitCost: null,
            totalCost: 16m,
            source: "OpenItem");
        rebuilt.GrossProfit = 14m;
        rebuilt.GrossMarginRate = 14m / 30m;

        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { rebuilt }, new[] { old });

        Assert.Equal("OpenItem", rebuilt.CostSource);
        Assert.Null(rebuilt.UnitCostSnapshot);
        Assert.Equal(16m, rebuilt.TotalCost);
        Assert.Equal(14m, rebuilt.GrossProfit);
    }

    [Fact]
    public void OpenItem原价证据暂缺且事实未变时保留已审计旧成本_事实变化时留下缺口()
    {
        var old = Statistic(
            productCode: "OPENITEM",
            quantity: 2,
            amount: 20m,
            unitCost: null,
            totalCost: 8m,
            source: "OpenItem");
        old.GrossProfit = 12m;
        old.GrossMarginRate = 0.6m;

        var unchanged = Statistic(
            productCode: "OPENITEM",
            quantity: 2,
            amount: 20m,
            unitCost: null,
            totalCost: null,
            source: "OpenItemMissingOriginalSale");
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { unchanged }, new[] { old });
        Assert.Equal(8m, unchanged.TotalCost);
        Assert.Equal(12m, unchanged.GrossProfit);

        var sourceChanged = Statistic(
            productCode: "OPENITEM",
            quantity: 2,
            amount: 20m,
            unitCost: null,
            totalCost: null,
            source: "OpenItemMissingOriginalSale");
        sourceChanged.LastSourceUploadTime = new DateTime(2025, 1, 2, 0, 0, 1);
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { sourceChanged }, new[] { old });
        Assert.Null(sourceChanged.TotalCost);

        var conflicting = Statistic(
            productCode: "OPENITEM",
            quantity: 2,
            amount: 20m,
            unitCost: null,
            totalCost: null,
            source: "OpenItemPriceConflict");
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { conflicting }, new[] { old });
        Assert.Null(conflicting.TotalCost);
        Assert.Equal("OpenItemPriceConflict", conflicting.CostSource);

        var missingPrice = Statistic(
            productCode: "OPENITEM",
            quantity: 2,
            amount: 20m,
            unitCost: null,
            totalCost: null,
            source: "OpenItemMissingPrice");
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { missingPrice }, new[] { old });
        Assert.Null(missingPrice.TotalCost);
        Assert.Equal("OpenItemMissingPrice", missingPrice.CostSource);

        var identityConflict = Statistic(
            productCode: "OPENITEM",
            quantity: 2,
            amount: 20m,
            unitCost: 99m,
            totalCost: 198m,
            source: "IdentityConflict");
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { identityConflict }, new[] { old });
        Assert.Null(identityConflict.UnitCostSnapshot);
        Assert.Null(identityConflict.TotalCost);
        Assert.Equal("IdentityConflict", identityConflict.CostSource);

        var changed = Statistic(
            productCode: "OPENITEM",
            quantity: 3,
            amount: 30m,
            unitCost: null,
            totalCost: null,
            source: "OpenItem");
        SalesStatisticsProductStoreDailyCommandWriter.PreserveHistoricalCostSnapshots(
            new[] { changed }, new[] { old });
        Assert.Null(changed.UnitCostSnapshot);
        Assert.Null(changed.TotalCost);
        Assert.Null(changed.GrossProfit);
        Assert.Equal("OpenItem", changed.CostSource);
    }

    [Fact]
    public void 成本锁的2025资源先取全局再取日期()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "services/backend/BlazorApp.Api/Features/SalesStatistics/Common/" +
            "SalesStatisticsCostWriteLock.cs"));
        var globalIndex = source.IndexOf("2025-global", StringComparison.Ordinal);
        var dateIndex = source.IndexOf("date:{date.Date:yyyy-MM-dd}", StringComparison.Ordinal);

        Assert.True(globalIndex >= 0);
        Assert.True(dateIndex > globalIndex);
        Assert.Contains("LockOwner = N'Transaction'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void 失败状态写入必须在日期锁内并通过初始状态CAS()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "services/backend/BlazorApp.Api/Features/SalesStatistics/ProductStoreDaily/" +
            "SalesStatisticsProductStoreDailyRefreshSlice.cs"));

        Assert.Contains("SalesStatisticsCostWriteLock.AcquireAsync(context.Db, targetDate)", source);
        Assert.Contains("IsProductStatisticFailureFenceCurrentAsync", source);
        Assert.Contains("SalesStatisticsCostWriteLock.IsBusy(originalException)", source);
        Assert.Contains("仅记录不覆盖新状态", source);
    }

    private static ProductStoreDailySalesStatistic Statistic(
        string productCode,
        int quantity,
        decimal amount,
        decimal? unitCost,
        decimal? totalCost,
        string source) => new()
        {
            Date = new DateTime(2025, 1, 2),
            BranchCode = "A",
            SupplierCode = "S",
            ProductCode = productCode,
            TotalQuantity = quantity,
            TotalAmount = amount,
            OrderCount = 1,
            UnitCostSnapshot = unitCost,
            TotalCost = totalCost,
            GrossProfit = totalCost.HasValue ? amount - totalCost.Value : null,
            GrossMarginRate = totalCost.HasValue && amount > 0m
                ? (amount - totalCost.Value) / amount
                : null,
            CostSource = source,
        };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        // linked worktree 的 .git 是指向主仓库的文件，同样是有效仓库根目录。
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, ".git"))
            && !File.Exists(Path.Combine(directory.FullName, ".git", "HEAD")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}
