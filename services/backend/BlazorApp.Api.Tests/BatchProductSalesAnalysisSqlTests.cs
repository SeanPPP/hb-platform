using BlazorApp.Api.Services.React;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// Reader 的 SQL Server 实际执行由集成环境覆盖；这些测试锁定聚合行的守恒和 unknown 语义。
/// </summary>
public sealed class BatchProductSalesAnalysisSqlTests
{
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
