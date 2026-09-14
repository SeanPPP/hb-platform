using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// Reader 的 SQL Server 实际执行由集成环境覆盖；这些测试锁定聚合行的守恒和 unknown 语义。
/// </summary>
public sealed class BatchProductSalesAnalysisSqlTests
{
    [Fact]
    public void SourceVersion_支付或别名规则变动必须改变日版本()
    {
        var day = new DateTime(2026, 1, 2);
        var orders = new Posm2025DailyTableSignature(1, day, day, "orders");
        var details = new Posm2025DailyTableSignature(1, day, day, "details");
        var returns = new Posm2025DailyTableSignature(0, null, null, "returns");
        var first = new Posm2025DailySnapshotSignature(
            day, orders, details, new Posm2025DailyTableSignature(1, day, day, "payment-15"), returns);
        var changedPayment = first with
        {
            Payments = new Posm2025DailyTableSignature(1, day, day, "payment-16")
        };

        var current = BatchProductSalesDiscountSnapshotSourceReader.BuildSourceVersion(
            first, null, "aliases-A");
        Assert.NotEqual(current, BatchProductSalesDiscountSnapshotSourceReader.BuildSourceVersion(
            changedPayment, null, "aliases-A"));
        Assert.NotEqual(current, BatchProductSalesDiscountSnapshotSourceReader.BuildSourceVersion(
            first, null, "aliases-B"));
    }

    [Fact]
    public void ToAggregateRow_同类来源行在SQL结果合并后保持有符号净量()
    {
        var row = BatchProductSalesAnalysisFactReader.ToAggregateRow(
            new DateTime(2026, 9, 1), "S1", "P1", 1,
            [
                new() { Quantity = 3m, SalesAmount = 24m, ReturnQuantity = 0m, DiscountPriceMin = 8m, DiscountPriceMax = 8m },
                new() { Quantity = -1m, SalesAmount = -8m, ReturnQuantity = 1m, DiscountPriceMin = 8m, DiscountPriceMax = 8m },
            ]);

        Assert.Equal(2m, row.Quantity);
        Assert.Equal(2m, row.DiscountQuantity);
        Assert.Equal(1m, row.ReturnQuantity);
        Assert.Equal(16m, row.SalesAmount);
        Assert.Equal(8m, row.DiscountPriceMin);
    }

    [Fact]
    public void Metrics_未知正负抵消仍然不是complete()
    {
        var row = BatchProductSalesAnalysisFactReader.ToAggregateRow(
            new DateTime(2026, 9, 1), "S1", "P1", 2,
            [new() { Quantity = 0m, SalesAmount = 0m, UnknownRowCount = 2 }]);

        Assert.Equal(0m, row.UnknownQuantity);
        Assert.Equal("unknown", row.Metrics.DiscountStatus);
    }

    [Fact]
    public void Metrics_已知类别与未知抵消行合并仍明确partial()
    {
        var unknown = BatchProductSalesAnalysisFactReader.ToAggregateRow(
            new DateTime(2026, 9, 1), "S1", "P1", 2,
            [new() { UnknownRowCount = 2 }]);
        var known = BatchProductSalesAnalysisFactReader.ToAggregateRow(
            new DateTime(2026, 9, 1), "S1", "P1", 0,
            [new() { Quantity = 2m, SalesAmount = 20m }]);

        var merged = new BatchProductSalesAggregateRow
        {
            Quantity = unknown.Quantity + known.Quantity,
            RegularQuantity = known.RegularQuantity,
            UnknownQuantity = unknown.UnknownQuantity,
            UnknownRowCount = unknown.UnknownRowCount + known.UnknownRowCount,
        };

        Assert.Equal("partial", merged.Metrics.DiscountStatus);
    }

    [Fact]
    public void CanonicalizeSupplierGroups_同供应商跨来源在供应商组末尾四舍五入且类别金额守恒()
    {
        var day = new DateTime(2025, 6, 11);

        var rows = BatchProductSalesAnalysisFactReader.CanonicalizeSupplierGroups(
        [
            new() { Date = day, BranchCode = "S1", ProductCode = "P1", SupplierCode = "SUP-1", DiscountKind = 0, Quantity = 1m, SalesAmount = 1.00004m },
            new() { Date = day, BranchCode = "S1", ProductCode = "P1", SupplierCode = "SUP-1", DiscountKind = 1, Quantity = 1m, SalesAmount = 1.00004m },
        ]);

        Assert.Equal(2.0001m, rows.Sum(row => row.SalesAmount));
        Assert.Equal(2m, rows.Sum(row => row.Quantity));
        // 类别各自四舍五入后，由稳定残差承载者补回 canonical supplier 总额。
        Assert.Equal(1.0001m, Assert.Single(rows, row => row.RegularQuantity != 0m).SalesAmount);
        Assert.Equal(1.0000m, Assert.Single(rows, row => row.DiscountQuantity != 0m).SalesAmount);
    }

    [Fact]
    public void CanonicalizeSupplierGroups_不同供应商必须分别四舍五入()
    {
        var day = new DateTime(2025, 6, 11);

        var rows = BatchProductSalesAnalysisFactReader.CanonicalizeSupplierGroups(
        [
            new() { Date = day, BranchCode = "S1", ProductCode = "P1", SupplierCode = "SUP-1", DiscountKind = 0, Quantity = 1m, SalesAmount = 0.00004m },
            new() { Date = day, BranchCode = "S1", ProductCode = "P1", SupplierCode = "SUP-2", DiscountKind = 0, Quantity = 1m, SalesAmount = 0.00004m },
        ]);

        Assert.Equal(2m, Assert.Single(rows).Quantity);
        Assert.Equal(0m, Assert.Single(rows).SalesAmount);
    }

    [Fact]
    public void CanonicalizeSupplierGroups_供应商组数量截断残差必须标为unknown()
    {
        var day = new DateTime(2025, 6, 11);

        var rows = BatchProductSalesAnalysisFactReader.CanonicalizeSupplierGroups(
        [new() { Date = day, BranchCode = "S1", ProductCode = "P1", SupplierCode = "SUP-1", DiscountKind = 0, Quantity = 1.5m, SalesAmount = 10m }]);

        var regular = Assert.Single(rows, row => row.RegularQuantity != 0m);
        var unknown = Assert.Single(rows, row => row.UnknownQuantity != 0m);
        Assert.Equal(1.5m, regular.RegularQuantity);
        Assert.Equal(-0.5m, unknown.UnknownQuantity);
        Assert.Equal(1, unknown.UnknownRowCount);
        Assert.Equal(1m, rows.Sum(row => row.Quantity));
        Assert.Equal("partial", rows.Aggregate(new BatchProductSalesAggregateRow(), (sum, row) => new BatchProductSalesAggregateRow
        {
            Quantity = sum.Quantity + row.Quantity,
            RegularQuantity = sum.RegularQuantity + row.RegularQuantity,
            DiscountQuantity = sum.DiscountQuantity + row.DiscountQuantity,
            UnknownQuantity = sum.UnknownQuantity + row.UnknownQuantity,
            UnknownRowCount = sum.UnknownRowCount + row.UnknownRowCount,
        }).Metrics.DiscountStatus);
    }

    [Theory]
    [InlineData(1d, "1", 1d)]
    [InlineData(-1d, "1", -1d)]
    [InlineData(1d, "3", -1d)]
    [InlineData(-1d, "3", 1d)]
    [InlineData(2d, "4", -2d)]
    public void ApplyHBSalesDocumentSign_只对类型三四反向且保留源符号(double source, string type, double expected)
    {
        Assert.Equal((decimal)expected,
            BatchProductSalesAnalysisFactReader.ApplyHBSalesDocumentSign((decimal)source, type));
    }
}
