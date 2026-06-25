using System.Text;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class ProductMovementReportService : IProductMovementReportService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<ProductMovementReportService> _logger;

        public ProductMovementReportService(
            SqlSugarContext context,
            ILogger<ProductMovementReportService> logger
        )
        {
            _db = context.Db;
            _logger = logger;
        }

        public async Task<ProductMovementReportResponseDto> GetReportAsync(
            ProductMovementReportQueryDto query,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var normalized = ProductMovementReportSqlBuilder.NormalizeQuery(query);
            var sql = ProductMovementReportSqlBuilder.Build(normalized, scopedStoreCodes);

            _logger.LogInformation(
                "查询商品经营分析报表 StoreScope={StoreScope}, Suggestion={Suggestion}, Credibility={Credibility}, Page={Page}, PageSize={PageSize}",
                scopedStoreCodes == null ? "ALL" : string.Join(",", scopedStoreCodes),
                normalized.Suggestion,
                normalized.DataCredibility,
                normalized.Page,
                normalized.PageSize
            );

            var rows = await _db.Ado.SqlQueryAsync<ProductMovementReportSqlRow>(
                sql.PagedSql,
                sql.Parameters.ToArray()
            );
            var summaryRows = await _db.Ado.SqlQueryAsync<ProductMovementReportSummarySqlRow>(
                sql.SummarySql,
                sql.Parameters.ToArray()
            );
            var lastUpdateRows = await _db.Ado.SqlQueryAsync<ProductMovementReportLastUpdateSqlRow>(
                sql.LastUpdateSql,
                sql.Parameters.ToArray()
            );

            var total = rows.FirstOrDefault()?.TotalCount ?? 0;
            return new ProductMovementReportResponseDto
            {
                Items = rows.Cast<ProductMovementReportRowDto>().ToList(),
                Total = total,
                Page = normalized.Page,
                PageSize = normalized.PageSize,
                SuggestionSummary = summaryRows
                    .Where(item => item.SummaryType == "Suggestion")
                    .Select(item => new ProductMovementReportSummaryDto
                    {
                        Key = item.Key,
                        Count = item.Count,
                    })
                    .ToList(),
                CredibilitySummary = summaryRows
                    .Where(item => item.SummaryType == "Credibility")
                    .Select(item => new ProductMovementReportSummaryDto
                    {
                        Key = item.Key,
                        Count = item.Count,
                    })
                    .ToList(),
                SalesStatisticLastUpdate =
                    rows.FirstOrDefault()?.SalesStatisticLastUpdate
                    ?? lastUpdateRows.FirstOrDefault()?.SalesStatisticLastUpdate,
            };
        }

        public async Task<List<ProductMovementReportStoreOptionDto>> GetStoreOptionsAsync(
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var parameters = new List<SugarParameter>();
            var storeParameterNames = ProductMovementReportSqlBuilder.AddStoreParametersForStoreOptions(
                parameters,
                scopedStoreCodes
            );
            var storeFilter = ProductMovementReportSqlBuilder.BuildStoreFilterForStoreOptions(
                "s.StoreCode",
                storeParameterNames
            );

            // 使用报表自己的门店选项接口，避免店长/仓库经理必须额外拥有 Stores.View 权限。
            var sql =
                "SELECT\n"
                + "    COALESCE(NULLIF(s.StoreName, N''), s.StoreCode) AS Label,\n"
                + "    s.StoreCode AS Value\n"
                + "FROM [Store] s\n"
                + "WHERE\n"
                + "    COALESCE(s.IsActive, 0) = 1\n"
                + "    AND COALESCE(s.IsDeleted, 0) = 0\n"
                + "    AND NULLIF(s.StoreCode, N'') IS NOT NULL\n"
                + storeFilter
                + "\nORDER BY\n"
                + "    s.StoreCode";

            return await _db.Ado.SqlQueryAsync<ProductMovementReportStoreOptionDto>(
                sql,
                parameters.ToArray()
            );
        }

        private sealed class ProductMovementReportSqlRow : ProductMovementReportRowDto
        {
            public int TotalCount { get; set; }
        }

        private sealed class ProductMovementReportSummarySqlRow
        {
            public string SummaryType { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        private sealed class ProductMovementReportLastUpdateSqlRow
        {
            public DateTime? SalesStatisticLastUpdate { get; set; }
        }
    }

    public static class ProductMovementReportSqlBuilder
    {
        private const int FastSalesDays = 30;
        private const int StableSalesDays = 90;
        private const int PurchaseDays = 180;
        private const int LowCoverDays = 14;
        private const int ClearanceNoSaleDays = 45;
        private const int StockUpCoverDays = 30;
        private const decimal StockUpGrossMarginRate = 0.3500m;
        private const decimal LowGrossMarginRate = 0.1500m;
        private const decimal GrowthRateForStockUp = 0.2000m;

        public static ProductMovementReportQueryDto NormalizeQuery(ProductMovementReportQueryDto query)
        {
            return new ProductMovementReportQueryDto
            {
                StoreCode = NormalizeText(query.StoreCode),
                AsOfDate = (query.AsOfDate ?? DateTime.Today).Date,
                Suggestion = NormalizeText(query.Suggestion),
                DataCredibility = NormalizeText(query.DataCredibility),
                Keyword = NormalizeText(query.Keyword),
                Page = query.Page <= 0 ? 1 : query.Page,
                PageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, 200),
            };
        }

        public static ProductMovementReportSqlBuildResult Build(
            ProductMovementReportQueryDto query,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var asOfDate = (query.AsOfDate ?? DateTime.Today).Date;
            var parameters = new List<SugarParameter>
            {
                new("@AsOfDate", asOfDate),
                new("@FastSalesDays", FastSalesDays),
                new("@StableSalesDays", StableSalesDays),
                new("@PurchaseDays", PurchaseDays),
                new("@LowCoverDays", LowCoverDays),
                new("@ClearanceNoSaleDays", ClearanceNoSaleDays),
                new("@StockUpCoverDays", StockUpCoverDays),
                new("@StockUpGrossMarginRate", StockUpGrossMarginRate),
                new("@LowGrossMarginRate", LowGrossMarginRate),
                new("@GrowthRateForStockUp", GrowthRateForStockUp),
                new("@FastStartDate", asOfDate.AddDays(-FastSalesDays + 1)),
                new("@StableStartDate", asOfDate.AddDays(-StableSalesDays + 1)),
                new("@PurchaseStartDate", asOfDate.AddDays(-PurchaseDays + 1)),
                new("@NextDate", asOfDate.AddDays(1)),
                new("@PreviousSalesDays", StableSalesDays - FastSalesDays),
                new("@Offset", (query.Page - 1) * query.PageSize),
                new("@PageSize", query.PageSize),
            };

            if (!string.IsNullOrWhiteSpace(query.Suggestion))
            {
                parameters.Add(new SugarParameter("@Suggestion", query.Suggestion));
            }
            if (!string.IsNullOrWhiteSpace(query.DataCredibility))
            {
                parameters.Add(new SugarParameter("@DataCredibility", query.DataCredibility));
            }
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                parameters.Add(new SugarParameter("@Keyword", "%" + EscapeLikeValue(query.Keyword) + "%"));
            }

            var storeCodes = ResolveStoreCodes(query.StoreCode, scopedStoreCodes);
            var storeParameterNames = AddStoreParameters(parameters, storeCodes);
            var salesStoreFilter = BuildStoreFilter("s.BranchCode", storeParameterNames);
            var purchaseStoreFilter = BuildStoreFilter(
                "COALESCE(NULLIF(d.StoreCode, N''), NULLIF(i.StoreCode, N''), NULLIF(srp.StoreCode, N''))",
                storeParameterNames
            );
            var storePriceFilter = BuildStoreFilter("srp.StoreCode", storeParameterNames);
            var metricsStoreFilter = BuildStoreFilter("u.BranchCode", storeParameterNames);
            var filteredWhere = BuildFilteredWhere(query);
            var baseCte = BuildBaseCte(
                salesStoreFilter,
                purchaseStoreFilter,
                storePriceFilter,
                metricsStoreFilter
            );

            return new ProductMovementReportSqlBuildResult
            {
                Parameters = parameters,
                PagedSql =
                    baseCte
                    + """

SELECT
    BranchCode AS StoreCode,
    StoreName AS StoreName,
    ProductCode AS ProductCode,
    ProductName AS ProductName,
    Barcode AS Barcode,
    SalesQty30 AS SalesQty30,
    SalesQty90 AS SalesQty90,
    CAST(DailySalesQty30 AS decimal(18, 2)) AS DailySalesQty30,
    CAST(SalesAmount90Aud AS decimal(18, 2)) AS SalesAmount90Aud,
    CAST(GrossProfit90Aud AS decimal(18, 2)) AS GrossProfit90Aud,
    CAST(GrossMarginRate90 AS decimal(18, 4)) AS GrossMarginRate90,
    LastSaleDate AS LastSaleDate,
    NoSaleDays AS NoSaleDays,
    CAST(PurchaseQty180 AS decimal(18, 2)) AS PurchaseQty180,
    SalesQty180 AS SalesQty180,
    CAST(EstimatedRemainingQty AS decimal(18, 2)) AS EstimatedRemainingQty,
    CAST(EstimatedCoverDays AS decimal(18, 2)) AS EstimatedCoverDays,
    DataCredibility AS DataCredibility,
    DataExceptionFlag AS DataExceptionFlag,
    SystemSuggestion AS SystemSuggestion,
    StoreManagerAction AS StoreManagerAction,
    SalesStatLastUpdate AS SalesStatisticLastUpdate,
    COUNT(1) OVER() AS TotalCount
FROM FinalRows
"""
                    + filteredWhere
                    + """

ORDER BY
    ActionPriority,
    BranchCode,
    SalesQty30 DESC,
    SalesAmount90Aud DESC,
    ProductCode
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
""",
                SummarySql =
                    baseCte
                    + """

, FilteredRows AS (
    SELECT * FROM FinalRows
"""
                    + filteredWhere
                    + """

)
SELECT N'Suggestion' AS SummaryType, SystemSuggestion AS [Key], COUNT(1) AS [Count]
FROM FilteredRows
GROUP BY SystemSuggestion
UNION ALL
SELECT N'Credibility' AS SummaryType, DataCredibility AS [Key], COUNT(1) AS [Count]
FROM FilteredRows
GROUP BY DataCredibility
""",
                LastUpdateSql =
                    """
SELECT MAX(s.UpdateTime) AS SalesStatisticLastUpdate
FROM [ProductStoreDailySalesStatistic] s
WHERE
    s.Date >= @PurchaseStartDate
    AND s.Date < @NextDate
"""
                    + "\n"
                    + salesStoreFilter,
            };
        }

        public static bool ContainsWriteKeyword(string sql)
        {
            var upper = sql.ToUpperInvariant();
            var unsafeWords = new[] { " INSERT ", " UPDATE ", " DELETE ", " MERGE ", " CREATE ", " ALTER ", " DROP ", " TRUNCATE ", " EXEC " };
            return unsafeWords.Any(upper.Contains);
        }

        private static string BuildBaseCte(
            string salesStoreFilter,
            string purchaseStoreFilter,
            string storePriceFilter,
            string metricsStoreFilter
        )
        {
            return $$"""
WITH SalesBase AS (
    SELECT
        s.BranchCode,
        s.ProductCode,
        MAX(NULLIF(s.ProductName, N'')) AS ProductName,
        MAX(NULLIF(s.Barcode, N'')) AS Barcode,
        SUM(CASE WHEN s.Date >= @FastStartDate THEN s.TotalQuantity ELSE 0 END) AS SalesQty30,
        SUM(CASE WHEN s.Date >= @StableStartDate THEN s.TotalQuantity ELSE 0 END) AS SalesQty90,
        SUM(s.TotalQuantity) AS SalesQty180,
        SUM(CASE WHEN s.Date >= @StableStartDate THEN s.TotalAmount ELSE 0 END) AS SalesAmount90Aud,
        SUM(CASE WHEN s.Date >= @StableStartDate THEN COALESCE(s.GrossProfit, 0) ELSE 0 END) AS GrossProfit90Aud,
        SUM(CASE WHEN s.Date >= @StableStartDate THEN COALESCE(s.TotalCost, 0) ELSE 0 END) AS TotalCost90Aud,
        SUM(CASE WHEN s.Date >= @StableStartDate AND s.TotalCost IS NULL THEN 1 ELSE 0 END) AS MissingCostRows90,
        SUM(CASE WHEN s.Date >= @StableStartDate AND s.GrossProfit IS NULL THEN 1 ELSE 0 END) AS MissingGrossProfitRows90,
        SUM(CASE WHEN s.Date >= @StableStartDate AND s.Date < @FastStartDate THEN s.TotalQuantity ELSE 0 END) AS PreviousSalesQty,
        MAX(CASE WHEN s.TotalQuantity > 0 THEN s.Date END) AS LastSaleDate,
        MAX(s.UpdateTime) AS RowSalesStatLastUpdate
    FROM [ProductStoreDailySalesStatistic] s
    WHERE
        s.Date >= @PurchaseStartDate
        AND s.Date < @NextDate
        {{salesStoreFilter}}
        AND NULLIF(s.BranchCode, N'') IS NOT NULL
        AND NULLIF(s.ProductCode, N'') IS NOT NULL
    GROUP BY
        s.BranchCode,
        s.ProductCode
),
PurchaseBase AS (
    SELECT
        COALESCE(NULLIF(d.StoreCode, N''), NULLIF(i.StoreCode, N''), NULLIF(srp.StoreCode, N'')) AS BranchCode,
        COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N'')) AS ProductCode,
        MAX(NULLIF(d.ProductName, N'')) AS ProductName,
        MAX(NULLIF(d.Barcode, N'')) AS Barcode,
        SUM(COALESCE(d.Quantity, 0)) AS PurchaseQty180,
        SUM(COALESCE(d.Amount, COALESCE(d.Quantity, 0) * COALESCE(d.PurchasePrice, 0))) AS PurchaseAmount180Aud,
        SUM(CASE WHEN COALESCE(i.InboundStatus, -1) <> 2 OR i.InboundDate IS NULL THEN 1 ELSE 0 END) AS UnconfirmedPurchaseRows180,
        MAX(COALESCE(i.InboundDate, i.OrderDate)) AS LastPurchaseDate
    FROM [StoreLocalSupplierInvoiceDetails] d
    INNER JOIN [StoreLocalSupplierInvoice] i
        ON i.InvoiceGUID = d.InvoiceGUID
        AND COALESCE(i.IsDeleted, 0) = 0
    LEFT JOIN [StoreRetailPrice] srp
        ON srp.UUID = d.StoreProductCode
        AND COALESCE(srp.IsDeleted, 0) = 0
    WHERE
        COALESCE(d.IsDeleted, 0) = 0
        -- 进货数据按进货单口径统计；用户已要求不限制为已入库单据。
        AND COALESCE(i.InboundDate, i.OrderDate) >= @PurchaseStartDate
        AND COALESCE(i.InboundDate, i.OrderDate) < @NextDate
        {{purchaseStoreFilter}}
        AND COALESCE(NULLIF(d.StoreCode, N''), NULLIF(i.StoreCode, N''), NULLIF(srp.StoreCode, N'')) IS NOT NULL
        AND COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N'')) IS NOT NULL
    GROUP BY
        COALESCE(NULLIF(d.StoreCode, N''), NULLIF(i.StoreCode, N''), NULLIF(srp.StoreCode, N'')),
        COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N''))
),
ProductUniverse AS (
    SELECT BranchCode, ProductCode FROM SalesBase
    UNION
    SELECT BranchCode, ProductCode FROM PurchaseBase
),
ProductMaster AS (
    SELECT
        p.ProductCode,
        MAX(NULLIF(p.ProductName, N'')) AS ProductName,
        MAX(NULLIF(p.Barcode, N'')) AS Barcode
    FROM [Product] p
    WHERE
        COALESCE(p.IsDeleted, 0) = 0
        AND NULLIF(p.ProductCode, N'') IS NOT NULL
    GROUP BY p.ProductCode
),
StorePriceMaster AS (
    SELECT
        srp.StoreCode AS BranchCode,
        srp.ProductCode,
        MAX(NULLIF(srp.SupplierCode, N'')) AS SupplierCode,
        MAX(srp.PurchasePrice) AS CurrentStorePurchasePriceAud,
        MAX(srp.StoreRetailPriceValue) AS CurrentStoreRetailPriceAud
    FROM [StoreRetailPrice] srp
    WHERE
        COALESCE(srp.IsDeleted, 0) = 0
        AND srp.IsActive = 1
        {{storePriceFilter}}
        AND NULLIF(srp.StoreCode, N'') IS NOT NULL
        AND NULLIF(srp.ProductCode, N'') IS NOT NULL
    GROUP BY
        srp.StoreCode,
        srp.ProductCode
),
GlobalSalesStat AS (
    SELECT MAX(s.UpdateTime) AS SalesStatLastUpdate
    FROM [ProductStoreDailySalesStatistic] s
    WHERE
        s.Date >= @PurchaseStartDate
        AND s.Date < @NextDate
        {{salesStoreFilter}}
),
Metrics AS (
    SELECT
        u.BranchCode,
        st.StoreName,
        u.ProductCode,
        COALESCE(sb.ProductName, pb.ProductName, pm.ProductName) AS ProductName,
        COALESCE(sb.Barcode, pb.Barcode, pm.Barcode) AS Barcode,
        spm.SupplierCode,
        COALESCE(sb.SalesQty30, 0) AS SalesQty30,
        COALESCE(sb.SalesQty90, 0) AS SalesQty90,
        CAST(COALESCE(sb.SalesQty30, 0) AS decimal(18, 4)) / NULLIF(@FastSalesDays, 0) AS DailySalesQty30,
        COALESCE(sb.SalesAmount90Aud, 0) AS SalesAmount90Aud,
        CASE
            WHEN COALESCE(sb.SalesQty90, 0) > 0
                AND (COALESCE(sb.MissingCostRows90, 0) > 0 OR COALESCE(sb.MissingGrossProfitRows90, 0) > 0)
                THEN NULL
            ELSE sb.GrossProfit90Aud
        END AS GrossProfit90Aud,
        CASE
            WHEN COALESCE(sb.SalesAmount90Aud, 0) > 0
                AND NOT (COALESCE(sb.MissingCostRows90, 0) > 0 OR COALESCE(sb.MissingGrossProfitRows90, 0) > 0)
                THEN sb.GrossProfit90Aud / NULLIF(sb.SalesAmount90Aud, 0)
            ELSE NULL
        END AS GrossMarginRate90,
        sb.LastSaleDate,
        CASE
            WHEN sb.LastSaleDate IS NULL THEN NULL
            ELSE DATEDIFF(day, sb.LastSaleDate, @AsOfDate)
        END AS NoSaleDays,
        COALESCE(pb.PurchaseQty180, 0) AS PurchaseQty180,
        COALESCE(sb.SalesQty180, 0) AS SalesQty180,
        COALESCE(pb.PurchaseQty180, 0) - COALESCE(sb.SalesQty180, 0) AS EstimatedRemainingQty,
        CASE
            WHEN COALESCE(sb.SalesQty30, 0) <= 0 THEN NULL
            ELSE (COALESCE(pb.PurchaseQty180, 0) - COALESCE(sb.SalesQty180, 0))
                / NULLIF(CAST(COALESCE(sb.SalesQty30, 0) AS decimal(18, 4)) / NULLIF(@FastSalesDays, 0), 0)
        END AS EstimatedCoverDays,
        CASE
            WHEN @PreviousSalesDays <= 0 THEN NULL
            ELSE CAST(COALESCE(sb.PreviousSalesQty, 0) AS decimal(18, 4)) / @PreviousSalesDays
        END AS PreviousDailySalesQty,
        COALESCE(sb.MissingCostRows90, 0) + COALESCE(sb.MissingGrossProfitRows90, 0) AS MissingCostOrProfitRows90,
        COALESCE(pb.UnconfirmedPurchaseRows180, 0) AS UnconfirmedPurchaseRows180,
        pb.LastPurchaseDate,
        COALESCE(sb.RowSalesStatLastUpdate, gss.SalesStatLastUpdate) AS SalesStatLastUpdate
    FROM ProductUniverse u
    LEFT JOIN SalesBase sb
        ON sb.BranchCode = u.BranchCode
        AND sb.ProductCode = u.ProductCode
    LEFT JOIN PurchaseBase pb
        ON pb.BranchCode = u.BranchCode
        AND pb.ProductCode = u.ProductCode
    LEFT JOIN ProductMaster pm
        ON pm.ProductCode = u.ProductCode
    LEFT JOIN StorePriceMaster spm
        ON spm.BranchCode = u.BranchCode
        AND spm.ProductCode = u.ProductCode
    LEFT JOIN [Store] st
        ON st.StoreCode = u.BranchCode
        AND COALESCE(st.IsDeleted, 0) = 0
    CROSS JOIN GlobalSalesStat gss
    WHERE
        1 = 1
        {{metricsStoreFilter}}
),
Ranked AS (
    SELECT
        m.*,
        CASE
            WHEN m.SalesQty30 > 0 THEN NTILE(4) OVER (PARTITION BY m.BranchCode ORDER BY m.SalesQty30 DESC)
            ELSE 4
        END AS FastSalesQuartile
    FROM Metrics m
),
Classified AS (
    SELECT
        r.*,
        CASE
            WHEN r.PurchaseQty180 = 0 AND r.SalesQty180 > 0 THEN N'低'
            WHEN r.EstimatedRemainingQty < 0 THEN N'低'
            WHEN r.ProductName IS NULL OR r.Barcode IS NULL THEN N'低'
            WHEN r.UnconfirmedPurchaseRows180 > 0 THEN N'中'
            WHEN r.SalesQty180 > 0 AND r.MissingCostOrProfitRows90 > 0 THEN N'中'
            WHEN r.PurchaseQty180 > 0 AND r.SalesQty180 > 0 AND r.EstimatedRemainingQty >= 0 THEN N'高'
            ELSE N'中'
        END AS DataCredibility,
        COALESCE(
            NULLIF(
                CONCAT_WS(
                    N'；',
                    CASE WHEN r.PurchaseQty180 = 0 AND r.SalesQty180 > 0 THEN N'无进货有销售，需核对期初库存/进货记录/调拨' END,
                    CASE WHEN r.EstimatedRemainingQty < 0 THEN N'销售大于进货，需核对期初库存/进货记录/调拨' END,
                    CASE WHEN r.SalesQty180 = 0 AND r.PurchaseQty180 > 0 THEN N'有进货无销售，需检查陈列/价格/库存' END,
                    CASE WHEN r.UnconfirmedPurchaseRows180 > 0 THEN N'包含未入库或未确认进货单，需核对是否实际到货' END,
                    CASE WHEN r.SalesQty90 > 0 AND r.MissingCostOrProfitRows90 > 0 THEN N'成本或毛利缺失，毛利判断不完整' END,
                    CASE WHEN r.ProductName IS NULL THEN N'商品名称缺失' END,
                    CASE WHEN r.Barcode IS NULL THEN N'条码缺失' END
                ),
                N''
            ),
            N'正常'
        ) AS DataExceptionFlag
    FROM Ranked r
),
	FinalRows AS (
	    SELECT
	        c.*,
	        CASE
	            WHEN c.DataCredibility = N'低' THEN N'观察'
	            WHEN c.SalesQty90 > 0 AND c.MissingCostOrProfitRows90 > 0 THEN N'观察'
	            WHEN c.SalesQty90 > 0 AND c.GrossMarginRate90 IS NOT NULL AND c.GrossMarginRate90 < @LowGrossMarginRate THEN N'观察'
	            WHEN c.EstimatedRemainingQty > 0
	                AND DATEDIFF(
	                    day,
	                    CASE
	                        WHEN c.LastSaleDate IS NULL THEN c.LastPurchaseDate
	                        WHEN c.LastPurchaseDate IS NULL THEN c.LastSaleDate
	                        WHEN c.LastPurchaseDate > c.LastSaleDate THEN c.LastPurchaseDate
	                        ELSE c.LastSaleDate
	                    END,
	                    @AsOfDate
	                ) >= @ClearanceNoSaleDays
	                THEN N'需要清仓'
	            WHEN c.FastSalesQuartile = 1 AND c.SalesQty30 > 0 AND c.EstimatedRemainingQty <= 0 THEN N'需要订货'
	            WHEN c.FastSalesQuartile = 1 AND c.SalesQty30 > 0 AND c.EstimatedCoverDays <= @LowCoverDays THEN N'需要备货'
	            WHEN c.FastSalesQuartile = 1
	                AND c.SalesQty30 > 0
                AND c.GrossMarginRate90 >= @StockUpGrossMarginRate
                AND c.EstimatedCoverDays <= @StockUpCoverDays
                AND c.PreviousDailySalesQty > 0
	                AND c.DailySalesQty30 >= c.PreviousDailySalesQty * (1 + @GrowthRateForStockUp)
	                THEN N'值得囤货'
	            WHEN c.FastSalesQuartile = 1 AND c.SalesQty90 > 0 THEN N'好卖'
	            ELSE N'正常'
	        END AS SystemSuggestion,
	        CASE
	            WHEN c.DataCredibility = N'低'
	                THEN N'数据或毛利异常，请先核对商品、成本、进货记录。'
	            WHEN c.SalesQty90 > 0 AND (c.GrossMarginRate90 < @LowGrossMarginRate OR c.MissingCostOrProfitRows90 > 0)
	                THEN N'数据或毛利异常，请先核对商品、成本、进货记录。'
	            WHEN c.EstimatedRemainingQty > 0
	                AND DATEDIFF(
	                    day,
	                    CASE
	                        WHEN c.LastSaleDate IS NULL THEN c.LastPurchaseDate
	                        WHEN c.LastPurchaseDate IS NULL THEN c.LastSaleDate
	                        WHEN c.LastPurchaseDate > c.LastSaleDate THEN c.LastPurchaseDate
	                        ELSE c.LastSaleDate
	                    END,
	                    @AsOfDate
	                ) >= @ClearanceNoSaleDays
	                THEN N'长期不动销，请检查陈列、价格和库存，考虑 markdown / clearance。'
	            WHEN c.FastSalesQuartile = 1 AND c.SalesQty30 > 0 AND c.EstimatedRemainingQty <= 0
	                THEN N'估算剩余量不足，请核对货架、后仓和进货单到货情况；不足再向总部/供应商补进。'
            WHEN c.FastSalesQuartile = 1 AND c.SalesQty30 > 0 AND c.EstimatedCoverDays <= @LowCoverDays
                THEN N'请检查货架和后仓；有货先上架，无货再订货。'
            WHEN c.FastSalesQuartile = 1
                AND c.SalesQty30 > 0
                AND c.GrossMarginRate90 >= @StockUpGrossMarginRate
                AND c.EstimatedCoverDays <= @StockUpCoverDays
                AND c.PreviousDailySalesQty > 0
	                AND c.DailySalesQty30 >= c.PreviousDailySalesQty * (1 + @GrowthRateForStockUp)
	                THEN N'热销且毛利较好，建议保持安全库存。'
	            WHEN c.FastSalesQuartile = 1 AND c.SalesQty90 > 0
	                THEN N'商品动销较好，请保持关注，避免断货。'
	            ELSE N'暂无特殊动作，按正常陈列和订货节奏处理。'
	        END AS StoreManagerAction,
	        CASE
	            WHEN c.DataCredibility = N'低' THEN 6
	            WHEN c.SalesQty90 > 0 AND (c.GrossMarginRate90 < @LowGrossMarginRate OR c.MissingCostOrProfitRows90 > 0) THEN 6
	            WHEN c.EstimatedRemainingQty > 0
	                AND DATEDIFF(
	                    day,
	                    CASE
	                        WHEN c.LastSaleDate IS NULL THEN c.LastPurchaseDate
	                        WHEN c.LastPurchaseDate IS NULL THEN c.LastSaleDate
	                        WHEN c.LastPurchaseDate > c.LastSaleDate THEN c.LastPurchaseDate
	                        ELSE c.LastSaleDate
	                    END,
	                    @AsOfDate
	                ) >= @ClearanceNoSaleDays
	                THEN 4
	            WHEN c.FastSalesQuartile = 1 AND c.SalesQty30 > 0 AND c.EstimatedRemainingQty <= 0 THEN 1
	            WHEN c.FastSalesQuartile = 1 AND c.SalesQty30 > 0 AND c.EstimatedCoverDays <= @LowCoverDays THEN 2
	            WHEN c.FastSalesQuartile = 1
                AND c.SalesQty30 > 0
                AND c.GrossMarginRate90 >= @StockUpGrossMarginRate
                AND c.EstimatedCoverDays <= @StockUpCoverDays
                AND c.PreviousDailySalesQty > 0
	                AND c.DailySalesQty30 >= c.PreviousDailySalesQty * (1 + @GrowthRateForStockUp)
	                THEN 3
	            WHEN c.FastSalesQuartile = 1 AND c.SalesQty90 > 0 THEN 5
	            ELSE 7
	        END AS ActionPriority
	    FROM Classified c
	)
""";
        }

        private static string BuildFilteredWhere(ProductMovementReportQueryDto query)
        {
            var clauses = new List<string>();
            if (!string.IsNullOrWhiteSpace(query.Suggestion))
            {
                clauses.Add("SystemSuggestion = @Suggestion");
            }
            if (!string.IsNullOrWhiteSpace(query.DataCredibility))
            {
                clauses.Add("DataCredibility = @DataCredibility");
            }
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                clauses.Add(
                    "(ProductCode LIKE @Keyword OR ProductName LIKE @Keyword OR Barcode LIKE @Keyword)"
                );
            }

            if (clauses.Count == 0)
            {
                return "\nWHERE 1 = 1\n";
            }

            return "\nWHERE " + string.Join("\n    AND ", clauses) + "\n";
        }

        private static IReadOnlyList<string>? ResolveStoreCodes(
            string? storeCode,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            if (!string.IsNullOrWhiteSpace(storeCode))
            {
                return new[] { storeCode.Trim() };
            }

            return scopedStoreCodes;
        }

        public static IReadOnlyList<string> AddStoreParametersForStoreOptions(
            List<SugarParameter> parameters,
            IReadOnlyList<string>? storeCodes
        )
        {
            return AddStoreParameters(parameters, storeCodes);
        }

        public static string BuildStoreFilterForStoreOptions(
            string expression,
            IReadOnlyList<string> parameterNames
        )
        {
            return BuildStoreFilter(expression, parameterNames);
        }

        private static IReadOnlyList<string> AddStoreParameters(
            List<SugarParameter> parameters,
            IReadOnlyList<string>? storeCodes
        )
        {
            if (storeCodes == null)
            {
                return Array.Empty<string>();
            }

            var parameterNames = new List<string>();
            var normalizedCodes = storeCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < normalizedCodes.Count; i++)
            {
                var parameterName = "@StoreCode" + i;
                parameterNames.Add(parameterName);
                parameters.Add(new SugarParameter(parameterName, normalizedCodes[i]));
            }

            if (parameterNames.Count == 0)
            {
                parameterNames.Add("__NO_STORE__");
            }

            return parameterNames;
        }

        private static string BuildStoreFilter(string expression, IReadOnlyList<string> parameterNames)
        {
            if (parameterNames.Count == 0)
            {
                return string.Empty;
            }

            if (parameterNames.Count == 1 && parameterNames[0] == "__NO_STORE__")
            {
                return "AND 1 = 0";
            }

            return $"AND {expression} IN ({string.Join(", ", parameterNames)})";
        }

        private static string? NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string EscapeLikeValue(string value)
        {
            return value
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]");
        }
    }

    public sealed class ProductMovementReportSqlBuildResult
    {
        public string PagedSql { get; set; } = string.Empty;
        public string SummarySql { get; set; } = string.Empty;
        public string LastUpdateSql { get; set; } = string.Empty;
        public List<SugarParameter> Parameters { get; set; } = new();
    }
}
