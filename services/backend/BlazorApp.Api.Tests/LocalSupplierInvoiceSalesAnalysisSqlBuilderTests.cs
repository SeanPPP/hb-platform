using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

public class LocalSupplierInvoiceSalesAnalysisSqlBuilderTests
{
    [Fact]
    public void BuildHeader_ShouldJoinRealLocalSupplierTable()
    {
        var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildHeader("invoice-guid-001");

        Assert.Contains("[LocalSupplier] sup", sql.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[HBLocalSupplier]", sql.Sql, StringComparison.Ordinal);
        Assert.False(LocalSupplierInvoiceSalesAnalysisSqlBuilder.ContainsWriteKeyword(sql.Sql));
    }

    [Fact]
    public void Build_ShouldScopeByInvoiceAndCompareSalesWindows()
    {
        var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.Build("invoice-guid-001");

        Assert.Contains(
            sql.Parameters,
            parameter =>
                parameter.ParameterName == "@InvoiceGuid"
                && (string)parameter.Value == "invoice-guid-001"
        );
        Assert.Contains("[StoreLocalSupplierInvoiceDetails]", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("[ProductStoreDailySalesStatistic]", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("CurrentProducts AS", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "CAST(COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) AS date) AS AnalysisDate",
            sql.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "MAX(COALESCE(pi.InboundDate, pi.OrderDate)) AS PreviousPurchaseDate",
            sql.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("pi.InvoiceGUID <> @InvoiceGuid", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "NULLIF(pd.ProductCode, N'') = cp.ProductCode",
            sql.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "NULLIF(p.ProductImage, N'') AS ProductImage",
            sql.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("cd.ProductImage AS ProductImage", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NULLIF(d.ProductImage", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM CurrentProducts cp", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON pp.StoreCode = cp.StoreCode", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON pp.StoreCode = cd.StoreCode", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "LEFT JOIN [StoreRetailPrice] psrp",
            sql.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("NULLIF(psrp.ProductCode", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "s.Date >= DATEADD(day, 1, cp.AnalysisDate) AND s.Date < DATEADD(day, 31, cp.AnalysisDate)",
            sql.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "s.Date >= DATEADD(day, 1, cp.AnalysisDate) AND s.Date < DATEADD(day, 61, cp.AnalysisDate)",
            sql.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "s.Date >= DATEADD(day, 1, cp.AnalysisDate) AND s.Date < DATEADD(day, 91, cp.AnalysisDate)",
            sql.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "s.Date < DATEADD(day, 1, cp.AnalysisDate) THEN COALESCE(s.TotalQuantity, 0)",
            sql.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("SalesQty30", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("SalesQty60", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("SalesQty90", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("SalesSincePreviousPurchase30", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("SalesSincePreviousPurchase60", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("SalesSincePreviousPurchase90", sql.Sql, StringComparison.Ordinal);
        Assert.False(LocalSupplierInvoiceSalesAnalysisSqlBuilder.ContainsWriteKeyword(sql.Sql));
    }

    [Fact]
    public void BuildPurchaseSalesAnalysis_ShouldUseOrderDateParametersAndSupplierPriority()
    {
        var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysis(
            new LocalSupplierPurchaseSalesAnalysisQueryDto
            {
                StoreCode = "1001",
                SupplierCode = "200",
                OrderDateStart = new DateTime(2026, 6, 1),
                OrderDateEnd = new DateTime(2026, 6, 30),
                Keyword = "ABC%'_",
                Page = 2,
                PageSize = 200,
                SortBy = "salesQty60",
                SortOrder = "asc",
            },
            null
        );

        Assert.Contains("h.OrderDate >= @OrderDateStart", sql.PagedSql, StringComparison.Ordinal);
        Assert.Contains("h.OrderDate < @OrderDateEndExclusive", sql.PagedSql, StringComparison.Ordinal);
        Assert.Contains(
            "COALESCE(NULLIF(p.LocalSupplierCode, N''), NULLIF(srp.SupplierCode, N'')) = @SupplierCode",
            sql.PagedSql,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "NULLIF(COALESCE(NULLIF(p.LocalSupplierCode, N''), NULLIF(srp.SupplierCode, N'')), N'') IS NOT NULL",
            sql.PagedSql,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "NULLIF(p.ProductImage, N'') AS ProductImage",
            sql.PagedSql,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("NULLIF(d.ProductImage", sql.PagedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sql.Parameters, p => p.ParameterName == "@OrderDateStart");
        Assert.Contains(sql.Parameters, p => p.ParameterName == "@OrderDateEndExclusive");
        Assert.Contains(
            sql.Parameters,
            p => p.ParameterName == "@StoreCode0" && (string)p.Value == "1001"
        );
        Assert.Contains(
            sql.Parameters,
            p => p.ParameterName == "@SupplierCode" && (string)p.Value == "200"
        );
        Assert.Contains(
            sql.Parameters,
            p =>
                p.ParameterName == "@Keyword"
                && ((string)p.Value).Contains("\\%", StringComparison.Ordinal)
                && ((string)p.Value).Contains("\\_", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void BuildPurchaseSalesAnalysis_ShouldAggregatePurchasesByDateAndPickLatestTwoRows()
    {
        var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysis(
            new LocalSupplierPurchaseSalesAnalysisQueryDto
            {
                StoreCode = "1001",
                SupplierCode = "200",
                Page = 1,
                PageSize = 100,
                SortBy = "latestPurchaseDate",
            },
            new[] { "1001", "1002" }
        );

        Assert.Contains("SUM(COALESCE(d.Quantity, 0)) AS PurchaseQty", sql.PagedSql, StringComparison.Ordinal);
        Assert.Contains("fi.PurchaseDate AS PurchaseDate", sql.PagedSql, StringComparison.Ordinal);
        Assert.Contains(
            "ROW_NUMBER() OVER (\n            PARTITION BY pda.StoreCode, pda.ProductCode\n            ORDER BY pda.PurchaseDate DESC\n        ) AS PurchaseRank",
            sql.PagedSql,
            StringComparison.Ordinal
        );
        Assert.Contains("WHERE rp.PurchaseRank = 1", sql.PagedSql, StringComparison.Ordinal);
        Assert.Contains("WHERE rp.PurchaseRank = 2", sql.PagedSql, StringComparison.Ordinal);
        Assert.Contains(
            "AND h.StoreCode IN (@StoreCode0)",
            sql.PagedSql,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("COUNT(1) OVER()", sql.PagedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT(1) AS TotalCount", sql.SummarySql, StringComparison.Ordinal);
        Assert.Contains(
            "MAX(SalesStatisticLastUpdate) AS SalesStatisticLastUpdate",
            sql.SummarySql,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void BuildPurchaseSalesAnalysis_ShouldUseSameDaySalesWindowAndHalfOpenPreviousRange()
    {
        var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysis(
            new LocalSupplierPurchaseSalesAnalysisQueryDto
            {
                StoreCode = "1001",
                SupplierCode = "200",
                Page = 1,
                PageSize = 50,
            },
            null
        );

        Assert.Contains(
            "s.Date >= lp.LatestPurchaseDate AND s.Date < DATEADD(day, 30, lp.LatestPurchaseDate)",
            sql.PagedSql,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "s.Date >= lp.LatestPurchaseDate AND s.Date < DATEADD(day, 60, lp.LatestPurchaseDate)",
            sql.PagedSql,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "s.Date >= lp.LatestPurchaseDate AND s.Date < DATEADD(day, 90, lp.LatestPurchaseDate)",
            sql.PagedSql,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "s.Date >= pp.PreviousPurchaseDate AND s.Date < lp.LatestPurchaseDate",
            sql.PagedSql,
            StringComparison.Ordinal
        );
        Assert.False(LocalSupplierInvoiceSalesAnalysisSqlBuilder.ContainsWriteKeyword(sql.PagedSql));
    }

    [Fact]
    public void NormalizePurchaseSalesAnalysisQuery_ShouldWhitelistSortFieldAndRestrictPageSize()
    {
        var normalized =
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.NormalizePurchaseSalesAnalysisQuery(
                new LocalSupplierPurchaseSalesAnalysisQueryDto
                {
                    Page = 0,
                    PageSize = 70,
                    SortBy = "drop table",
                    SortOrder = "weird",
                }
            );

        Assert.Equal(1, normalized.Page);
        Assert.Equal(100, normalized.PageSize);
        Assert.Equal("latestPurchaseDate", normalized.SortBy);
        Assert.Equal("desc", normalized.SortOrder);
        Assert.Equal(DateTime.Today.AddDays(-180), normalized.OrderDateStart);
        Assert.Equal(DateTime.Today, normalized.OrderDateEnd);
    }

    [Fact]
    public void NormalizePurchaseSalesAnalysisQuery_ShouldDefaultOrderDateRangeFromReferenceDate()
    {
        var normalized =
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.NormalizePurchaseSalesAnalysisQuery(
                new LocalSupplierPurchaseSalesAnalysisQueryDto(),
                new DateTime(2026, 6, 25)
            );

        Assert.Equal(new DateTime(2025, 12, 27), normalized.OrderDateStart);
        Assert.Equal(new DateTime(2026, 6, 25), normalized.OrderDateEnd);
    }

    [Fact]
    public void ValidatePurchaseSalesAnalysisQuery_ShouldRejectOverMaxDateRange()
    {
        var normalized =
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.NormalizePurchaseSalesAnalysisQuery(
                new LocalSupplierPurchaseSalesAnalysisQueryDto
                {
                    StoreCode = "1001",
                    SupplierCode = "200",
                    OrderDateStart = new DateTime(2025, 1, 1),
                    OrderDateEnd = new DateTime(2026, 6, 25),
                }
            );

        var validation =
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.ValidatePurchaseSalesAnalysisQuery(
                normalized
            );

        Assert.False(validation.IsValid);
        Assert.Throws<ArgumentException>(() =>
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysis(
                normalized,
                null
            )
        );
    }

    [Fact]
    public void ValidatePurchaseSalesAnalysisQuery_ShouldRejectMissingStore()
    {
        var normalized =
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.NormalizePurchaseSalesAnalysisQuery(
                new LocalSupplierPurchaseSalesAnalysisQueryDto
                {
                    SupplierCode = "200",
                    Page = 1,
                    PageSize = 100,
                }
            );

        var validation =
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.ValidatePurchaseSalesAnalysisQuery(
                normalized
            );

        Assert.False(validation.IsValid);
        Assert.Equal("分店不能为空。", validation.Message);
    }

    [Fact]
    public void ValidatePurchaseSalesAnalysisQuery_ShouldRejectMissingSupplier()
    {
        var normalized =
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.NormalizePurchaseSalesAnalysisQuery(
                new LocalSupplierPurchaseSalesAnalysisQueryDto
                {
                    StoreCode = "1001",
                    Page = 1,
                    PageSize = 100,
                }
            );

        var validation =
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.ValidatePurchaseSalesAnalysisQuery(
                normalized
            );

        Assert.False(validation.IsValid);
        Assert.Equal("供应商不能为空。", validation.Message);
    }

    [Fact]
    public void BuildPurchaseSalesAnalysis_ShouldIntersectRequestedStoreWithScope()
    {
        var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysis(
            new LocalSupplierPurchaseSalesAnalysisQueryDto
            {
                StoreCode = "9999",
                SupplierCode = "200",
                Page = 1,
                PageSize = 100,
            },
            new[] { "1001" }
        );

        Assert.Contains("AND 1 = 0", sql.PagedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("@StoreCode0", sql.PagedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPurchaseSalesAnalysis_ShouldKeepWhitelistedSortAndContainsWriteKeywordFalse()
    {
        var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysis(
            new LocalSupplierPurchaseSalesAnalysisQueryDto
            {
                StoreCode = "1001",
                SupplierCode = "200",
                Page = 1,
                PageSize = 50,
                SortBy = "salesBetweenPurchases",
                SortOrder = "asc",
            },
            null
        );

        Assert.Contains(
            "ORDER BY\n    SalesBetweenPurchases ASC, StoreCode ASC, ProductCode ASC",
            sql.PagedSql,
            StringComparison.Ordinal
        );
        Assert.False(LocalSupplierInvoiceSalesAnalysisSqlBuilder.ContainsWriteKeyword(sql.PagedSql));
    }

    [Fact]
    public void BuildPurchaseSalesAnalysisStoreOptions_ShouldUseInvoiceDataAndScopeStores()
    {
        var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysisStoreOptions(
            new[] { "1001", "1002" }
        );

        Assert.Contains("FROM [StoreLocalSupplierInvoice] h", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN [Store] st", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("AND h.StoreCode IN (@StoreCode0, @StoreCode1)", sql.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(s.IsActive", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sql.Parameters, p => p.ParameterName == "@StoreCode0" && (string)p.Value == "1001");
        Assert.False(LocalSupplierInvoiceSalesAnalysisSqlBuilder.ContainsWriteKeyword(sql.Sql));
    }

    [Fact]
    public void BuildPurchaseSalesAnalysisSupplierOptions_ShouldMatchAnalysisSupplierFallback()
    {
        var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysisSupplierOptions(
            "1001",
            null
        );

        Assert.Contains("FROM [StoreLocalSupplierInvoice] h", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("[StoreRetailPrice] srp", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("[LocalSupplier] sup", sql.Sql, StringComparison.Ordinal);
        Assert.Contains(
            "COALESCE(NULLIF(p.LocalSupplierCode, N''), NULLIF(srp.SupplierCode, N'')) AS SupplierCode",
            sql.Sql,
            StringComparison.Ordinal
        );
        Assert.Contains("AND h.StoreCode IN (@StoreCode0)", sql.Sql, StringComparison.Ordinal);
        Assert.Contains(sql.Parameters, p => p.ParameterName == "@StoreCode0" && (string)p.Value == "1001");
        Assert.False(LocalSupplierInvoiceSalesAnalysisSqlBuilder.ContainsWriteKeyword(sql.Sql));
    }

    [Fact]
    public void Build_ShouldRejectBlankInvoiceGuid()
    {
        Assert.Throws<ArgumentException>(() =>
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.Build(" ")
        );
    }
}
