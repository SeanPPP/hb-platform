using BlazorApp.Api.Services.React;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductReportPagingSqlTests
{
    [Fact]
    public void Sql_UsesJsonParametersAndOneStablePagedUnion()
    {
        var sql = SalesDashboardReactService.BuildProductReportPagingSql(includeCompare: true);

        Assert.Contains("OPENJSON(@Branches)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OPENJSON(@LocalSuppliers)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OPENJSON(@ChinaProductMap)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL SELECT * FROM CompareSource", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY a.[CurrentSalesAmount] DESC, a.[CompareSalesAmount] DESC, a.[ProductCode] ASC", sql);
        Assert.Contains("Windowed AS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT(*) OVER ()", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ROW_NUMBER() OVER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INTO #ProductReportAggregates", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM #ProductReportAggregates AS a", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OPTION (RECOMPILE)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP TABLE #ProductReportAggregates", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN CATCH", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("windowed.[TotalCount] <= @PageOffset", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY page.[RowNumber] ASC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Counted AS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PageRows AS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INTO #ProductReportPage", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INNER JOIN #ProductReportPage AS requested", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN Aggregated AS detail", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "INTO #ProductReportAggregates"));
        Assert.DoesNotContain("IN ('", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sql_WithoutCompareDoesNotReferenceCompareParameters()
    {
        var sql = SalesDashboardReactService.BuildProductReportPagingSql(includeCompare: false);

        Assert.DoesNotContain("@CompareStart", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CompareSource", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UNION ALL SELECT * FROM CompareSource", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChinaSql_FiltersDirectAndLegacyRowsWithinTheSamePeriodScan()
    {
        var sql = SalesDashboardReactService.BuildProductReportPagingSql(includeCompare: true);

        Assert.DoesNotContain("CurrentLegacySource AS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CompareLegacySource AS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("s.[SupplierCode] = N'200'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM #ProductReportAggregates AS a", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SearchLiteralEscapesSqlLikeCharacters()
    {
        Assert.Equal(@"a\%\_b\[c", SalesDashboardReactService.EscapeLikeValue(@"a%_b[c"));
    }

    [Fact]
    public void MappingKeepsTotalForAnOutOfRangePageAndAppliesGrossProfitCompleteness()
    {
        var rows = new[]
        {
            new SalesDashboardReactService.ProductReportPagingSqlRow
            {
                HasData = false,
                TotalCount = 21,
            },
        };

        var page = SalesDashboardReactService.MapProductReportPagingRows(rows, pageIndex: 2, pageSize: 20);

        Assert.Equal(21, page.Total);
        Assert.Equal(2, page.PageIndex);
        Assert.Empty(page.Data);

        var mapped = SalesDashboardReactService.ToSalesProductDetailWithDiscount(
            new SalesDashboardReactService.ProductReportPagingSqlRow
            {
                HasData = true,
                ProductCode = "P-1",
                CurrentQuantity = 2,
                CurrentSalesAmount = 10m,
                CurrentGrossProfit = 3m,
                CurrentStatisticRowCount = 2,
                CurrentCostedRowCount = 1,
                CurrentGrossProfitRowCount = 1,
                CompareQuantity = 1,
                CompareSalesAmount = 5m,
                CompareGrossProfit = 2m,
                CompareStatisticRowCount = 1,
                CompareCostedRowCount = 1,
                CompareGrossProfitRowCount = 1,
            }
        );

        Assert.Null(mapped.GrossProfit);
        Assert.Equal(2m, mapped.GrossProfitLY);
        Assert.Null(mapped.GrossMarginRate);
        Assert.Equal(.4m, mapped.GrossMarginRateLY);
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
    }
}
