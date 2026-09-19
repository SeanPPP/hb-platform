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

        Assert.DoesNotContain("i.InboundStatus = 2", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("i.InboundDate IS NOT NULL", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(i.InboundDate, i.OrderDate)", sql.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("包含未入库或未确认进货单", sql.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShouldKeepReadOnlyHalfOpenDateWindow()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { Page = 1, PageSize = 50 },
            new[] { "1001", "1002" }
        );
        var combinedSql = sql.Sql;

        Assert.Contains("s.Date >= @PurchaseStartDate", combinedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("s.Date < @NextDate", combinedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("i.EffectivePurchaseDate < CAST(@NextDate AS date)", combinedSql, StringComparison.OrdinalIgnoreCase);
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

        Assert.Contains("@StoreCode0", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("SystemSuggestion = @Suggestion", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("DataCredibility = @DataCredibility", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("ProductCode LIKE @Keyword", sql.Sql, StringComparison.Ordinal);
        Assert.Contains(sql.Parameters, parameter => parameter.ParameterName == "@StoreCode0" && (string)parameter.Value == "1001");
        Assert.Contains(sql.Parameters, parameter => parameter.ParameterName == "@Keyword" && !((string)parameter.Value).Contains("ABC%'_", StringComparison.Ordinal));
        // 保留库排序规则的 LIKE（不区分全半角），不能为提速改成 BIN2。
        Assert.DoesNotContain("BIN2", sql.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShouldPrioritizeDataObservationBeforeReplenishmentActions()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { StoreCode = "1001" },
            null
        ).Sql;

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
    public void Build_ShouldSelectProductImageFromProductMaster()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { StoreCode = "1001" },
            null
        ).Sql;

        Assert.Contains("MAX(NULLIF(p.ProductImage, N'')) AS ImageUrl", sql, StringComparison.Ordinal);
        Assert.Contains("pm.ImageUrl AS ImageUrl", sql, StringComparison.Ordinal);
        Assert.Contains("ImageUrl AS ImageUrl", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShouldKeepSuggestionSummaryCountsIndependentOfSuggestionFilter()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto
            {
                StoreCode = "1001",
                Suggestion = "需要订货",
                DataCredibility = "高",
                Keyword = "milk",
            },
            null
        ).Sql;

        var pageIndex = sql.IndexOf("-- 结果集 1", StringComparison.Ordinal);
        var summaryIndex = sql.IndexOf("-- 结果集 2", StringComparison.Ordinal);
        var lastUpdateIndex = sql.IndexOf("-- 结果集 3", StringComparison.Ordinal);
        Assert.True(pageIndex > 0 && summaryIndex > pageIndex && lastUpdateIndex > summaryIndex, "应按明细、汇总、更新时间顺序返回三个结果集。");

        var materialize = sql[..pageIndex];
        var page = sql[pageIndex..summaryIndex];
        var summary = sql[summaryIndex..lastUpdateIndex];

        // 明细按建议过滤，汇总不过滤：前台的建议计数卡片本身就是筛选入口，
        // 选中一类之后其余卡片不能塌成 0。
        Assert.Contains("SystemSuggestion = @Suggestion", page, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemSuggestion = @Suggestion", materialize, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemSuggestion = @Suggestion", summary, StringComparison.Ordinal);

        // 门店、可信度和关键词在物化阶段过滤，因此同时约束明细与汇总。
        Assert.Contains("DataCredibility = @DataCredibility", materialize, StringComparison.Ordinal);
        Assert.Contains("ProductCode LIKE @Keyword", materialize, StringComparison.Ordinal);
        Assert.Contains("@StoreCode0", materialize, StringComparison.Ordinal);
        Assert.Contains("FROM #FinalRows", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShouldMaterializeOnceAndKeepPurchaseScanIndexFriendly()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { StoreCode = "1002" },
            null
        ).Sql;

        // 日统计与进货各物化一次，避免 CTE 被多处引用时重复计算。
        Assert.Contains("INTO #SalesBase", sql, StringComparison.Ordinal);
        Assert.Contains("INTO #PurchaseBase", sql, StringComparison.Ordinal);
        Assert.Contains("INTO #FinalRows", sql, StringComparison.Ordinal);

        // 进货按发票生效日期与发票门店过滤，才能命中 IX_LSPSA_Invoice_EffectiveDate_Store_Invoice。
        Assert.Contains("i.EffectivePurchaseDate >= CAST(@PurchaseStartDate AS date)", sql, StringComparison.Ordinal);
        Assert.Contains("AND i.StoreCode IN (@StoreCode0)", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(i.InboundDate, i.OrderDate) IS NOT NULL", sql, StringComparison.Ordinal);

        // COALESCE 包住 IsDeleted 会让所有 WHERE IsDeleted = 0 的过滤索引失效。
        Assert.DoesNotContain("COALESCE(d.IsDeleted", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(i.IsDeleted", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(srp.IsDeleted", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(p.IsDeleted", sql, StringComparison.Ordinal);

        // 有门店范围时逐日按聚集主键 (Date, BranchCode) 查找，不再扫描整段日期内所有门店的宽行。
        Assert.Contains("INNER LOOP JOIN [ProductStoreDailySalesStatistic] s", sql, StringComparison.Ordinal);
        Assert.Contains("ON s.Date = salesDays.[Day]", sql, StringComparison.Ordinal);

        // 四分位必须有确定的次序，否则销量并列的商品会随执行计划在建议之间翻转。
        Assert.Contains("ORDER BY m.SalesQty30 DESC, m.ProductCode)", sql, StringComparison.Ordinal);

        // 门店零售价的供应商和价格从未被下游使用，不应再整表聚合。
        Assert.DoesNotContain("StorePriceMaster", sql, StringComparison.Ordinal);
        Assert.False(ProductMovementReportSqlBuilder.ContainsWriteKeyword(sql));
    }

    [Fact]
    public void Build_ShouldKeepRangeScanWhenQueryingAllStores()
    {
        var sql = ProductMovementReportSqlBuilder.Build(new ProductMovementReportQueryDto(), null).Sql;

        // 全部分店本就要读全部行，逐日查找没有收益。
        Assert.DoesNotContain("INNER LOOP JOIN", sql, StringComparison.Ordinal);
        Assert.Contains("FROM [ProductStoreDailySalesStatistic] s", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeQuery_ShouldOnlyAcceptWhitelistedSort()
    {
        var ascending = ProductMovementReportSqlBuilder.NormalizeQuery(
            new ProductMovementReportQueryDto { SortBy = " SalesQty30 ", SortDirection = "ASC" }
        );
        Assert.Equal("salesQty30", ascending.SortBy);
        Assert.Equal("asc", ascending.SortDirection);

        // 只给字段不给方向时，销量默认从高到低。
        var defaultDirection = ProductMovementReportSqlBuilder.NormalizeQuery(
            new ProductMovementReportQueryDto { SortBy = "salesQty30" }
        );
        Assert.Equal("desc", defaultDirection.SortDirection);

        // 白名单外的字段一律丢弃，方向也随之清空，回到默认的建议紧急程度排序。
        var injected = ProductMovementReportSqlBuilder.NormalizeQuery(
            new ProductMovementReportQueryDto { SortBy = "ProductName; DROP TABLE Product", SortDirection = "asc" }
        );
        Assert.Null(injected.SortBy);
        Assert.Null(injected.SortDirection);
    }

    [Theory]
    [InlineData(null, null, "ActionPriority,\n    BranchCode,\n    SalesQty30 DESC,\n    SalesAmount90Aud DESC,\n    ProductCode")]
    [InlineData("salesQty30", "desc", "SalesQty30 DESC,\n    SalesQty90 DESC,\n    SalesAmount90Aud DESC,\n    BranchCode,\n    ProductCode")]
    [InlineData("salesQty30", "asc", "SalesQty30,\n    SalesQty90,\n    SalesAmount90Aud,\n    BranchCode,\n    ProductCode")]
    public void Build_ShouldOrderPageBySelectedSort(string? sortBy, string? sortDirection, string expectedOrder)
    {
        var query = ProductMovementReportSqlBuilder.NormalizeQuery(
            new ProductMovementReportQueryDto { StoreCode = "1003", SortBy = sortBy, SortDirection = sortDirection }
        );
        var sql = ProductMovementReportSqlBuilder.Build(query, null).Sql;

        // 内层决定取哪一页，外层按同一次序输出；两处必须一致，末尾都有门店与商品编码兜底保证翻页稳定。
        Assert.Contains("ORDER BY\n    " + expectedOrder + "\n    OFFSET @Offset ROWS", sql, StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY\n    " + expectedOrder.Replace("\n    ", "\n    pg.", StringComparison.Ordinal).Insert(0, "pg.") + ";",
            sql,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_ShouldSearchItemNumberFromProductMasterBeforeFiltering()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { StoreCode = "1003", Keyword = "WEW1379" },
            null
        ).Sql;

        // 货号只在商品档案里；先物化命中的商品编码，再与编码/名称/条码并列参与范围过滤。
        var keywordProducts = sql.IndexOf("INTO #KeywordProducts", StringComparison.Ordinal);
        var finalRows = sql.IndexOf("INTO #FinalRows", StringComparison.Ordinal);
        Assert.True(keywordProducts > 0 && keywordProducts < finalRows, "货号命中商品必须在物化 #FinalRows 之前取出。");
        Assert.Contains("p.IsDeleted = 0\n    AND p.ItemNumber LIKE @Keyword", sql, StringComparison.Ordinal);
        Assert.Contains("OR ProductCode IN (SELECT ProductCode FROM #KeywordProducts))", sql, StringComparison.Ordinal);
        Assert.False(ProductMovementReportSqlBuilder.ContainsWriteKeyword(sql));

        var withoutKeyword = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { StoreCode = "1003" },
            null
        ).Sql;
        Assert.DoesNotContain("#KeywordProducts", withoutKeyword, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShouldLookupItemNumberOnlyForPagedRows()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { StoreCode = "1003" },
            null
        ).Sql;
        var page = sql[sql.IndexOf("-- 结果集 1", StringComparison.Ordinal)..sql.IndexOf("-- 结果集 2", StringComparison.Ordinal)];

        // 先分页再查货号，避免为全部分店约 30 万行关联商品表。
        var offset = page.IndexOf("OFFSET @Offset ROWS", StringComparison.Ordinal);
        var lookup = page.IndexOf("OUTER APPLY", StringComparison.Ordinal);
        Assert.True(offset > 0 && offset < lookup, "货号查找必须放在分页之后。");
        Assert.Contains("AND p.ProductCode = pg.ProductCode", page, StringComparison.Ordinal);
        Assert.Contains("im.ItemNumber AS ItemNumber", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ShouldUseLastMovementDateForClearanceInsteadOfNullNoSaleDays()
    {
        var sql = ProductMovementReportSqlBuilder.Build(
            new ProductMovementReportQueryDto { StoreCode = "1001" },
            null
        ).Sql;

        Assert.DoesNotContain(
            "COALESCE(c.NoSaleDays, @ClearanceNoSaleDays + 1) >= @ClearanceNoSaleDays",
            sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("WHEN c.LastSaleDate IS NULL THEN c.LastPurchaseDate", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN c.LastPurchaseDate > c.LastSaleDate THEN c.LastPurchaseDate", sql, StringComparison.Ordinal);
    }
}
