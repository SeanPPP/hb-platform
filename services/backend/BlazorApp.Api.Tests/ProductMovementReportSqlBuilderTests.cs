using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ProductMovementReportSqlBuilderTests
{
    [Fact]
    public void Build_ShouldUsePurchaseInvoiceScopeWithoutInboundOnlyFilter()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { StoreCode = "1002", Keyword = "milk" },
            null
        );

        Assert.DoesNotContain("i.InboundStatus = 2", sql.PagedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("i.InboundDate IS NOT NULL", sql.PagedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(i.InboundDate, i.OrderDate)", sql.PagedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("包含未入库或未确认进货单", sql.PagedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShouldKeepReadOnlyHalfOpenDateWindow()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { Page = 1, PageSize = 50 },
            new[] { "1001", "1002" }
        );
        var combinedSql = string.Join("\n", sql.PagedSql, sql.SummarySql, sql.LastUpdateSql);

        Assert.Contains("s.Date >= @PurchaseStartDate", combinedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("s.Date < @NextDate", combinedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(i.InboundDate, i.OrderDate) < @NextDate", combinedSql, StringComparison.OrdinalIgnoreCase);
        Assert.False(ProductMovementReportSqlBuilder.ContainsWriteKeyword(combinedSql));
    }

    [Fact]
    public void Build_ShouldParameterizeStoresAndFilters()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto
            {
                StoreCode = "1001",
                Suggestion = "需要备货",
                DataCredibility = "中",
                Keyword = "ABC%'_",
            },
            null
        );

        Assert.Contains("@StoreCode0", sql.PagedSql, StringComparison.Ordinal);
        Assert.Contains("SystemSuggestion = @Suggestion", sql.PagedSql, StringComparison.Ordinal);
        Assert.Contains("DataCredibility = @DataCredibility", sql.PagedSql, StringComparison.Ordinal);
        Assert.Contains("ProductCode LIKE @Keyword", sql.PagedSql, StringComparison.Ordinal);
        Assert.Contains(sql.Parameters, parameter => parameter.ParameterName == "@StoreCode0" && (string)parameter.Value == "1001");
        Assert.Contains(sql.Parameters, parameter => parameter.ParameterName == "@Keyword" && !((string)parameter.Value).Contains("ABC%'_", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ShouldPrioritizeDataObservationBeforeReplenishmentActions()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { StoreCode = "1001" },
            null
        ).PagedSql;

        var missingCostObservationIndex = sql.IndexOf(
            "WHEN c.SalesQty90 > 0 AND c.MissingCostOrProfitRows90 > 0 THEN N'观察'",
            StringComparison.Ordinal
        );
        var lowMarginObservationIndex = sql.IndexOf(
            "WHEN c.SalesQty90 > 0 AND c.GrossMarginRate90 IS NOT NULL AND c.GrossMarginRate90 < @LowGrossMarginRate THEN N'观察'",
            StringComparison.Ordinal
        );
        var orderActionIndex = sql.IndexOf(
            "WHEN c.FastSalesQuartile = 1 AND c.SalesQty30 > 0 AND c.EstimatedRemainingQty <= 0 THEN N'需要订货'",
            StringComparison.Ordinal
        );

        Assert.True(missingCostObservationIndex >= 0, "成本缺失应先进入观察。");
        Assert.True(lowMarginObservationIndex >= 0, "低毛利应先进入观察。");
        Assert.True(orderActionIndex >= 0, "需要订货规则应存在。");
        Assert.True(missingCostObservationIndex < orderActionIndex, "成本缺失不能被订货/备货建议覆盖。");
        Assert.True(lowMarginObservationIndex < orderActionIndex, "低毛利不能被订货/备货建议覆盖。");
    }

    [Fact]
    public void Build_ShouldUseLastMovementDateForClearanceInsteadOfNullNoSaleDays()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { StoreCode = "1001" },
            null
        ).PagedSql;

        Assert.DoesNotContain(
            "COALESCE(c.NoSaleDays, @ClearanceNoSaleDays + 1) >= @ClearanceNoSaleDays",
            sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("WHEN c.LastSaleDate IS NULL THEN c.LastPurchaseDate", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN c.LastPurchaseDate > c.LastSaleDate THEN c.LastPurchaseDate", sql, StringComparison.Ordinal);
    }
}
