using BlazorApp.Api.Services.React;
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
    public void Build_ShouldRejectBlankInvoiceGuid()
    {
        Assert.Throws<ArgumentException>(() =>
            LocalSupplierInvoiceSalesAnalysisSqlBuilder.Build(" ")
        );
    }
}
