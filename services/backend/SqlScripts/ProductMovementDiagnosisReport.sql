-- =====================================================
-- 店长商品经营分析只读报表（澳洲门店 / AUD）
-- 用途：用销售统计 + 进货记录判断好卖、备货、订货、清仓和观察商品。
--
-- 重要说明：
-- 1. 本脚本只读，不写入、不创建、不修改任何数据库对象。
-- 2. 当前系统没有可靠的门店货架库存/后仓库存字段。
-- 3. “估算剩余量”只是经营预警口径，不是盘点库存、财务库存，也不是自动订货依据。
-- 4. 店长动作必须结合现场货架、后仓和实际到货情况确认。
-- =====================================================

SET NOCOUNT ON;

DECLARE @AsOfDate date = CAST(GETDATE() AS date);
DECLARE @FastSalesDays int = 30;
DECLARE @StableSalesDays int = 90;
DECLARE @PurchaseDays int = 180;
DECLARE @LowCoverDays int = 14;
DECLARE @ClearanceNoSaleDays int = 45;

-- 可选过滤：单店验证时填门店代码；限制输出时填行数。NULL 表示不过滤/不限制。
DECLARE @StoreCode nvarchar(50) = NULL;
DECLARE @TopRows int = 1000;

-- 经营阈值：如门店策略变化，可只调整这里，不改主体查询。
DECLARE @StockUpCoverDays int = 30;
DECLARE @StockUpGrossMarginRate decimal(9, 4) = 0.3500;
DECLARE @LowGrossMarginRate decimal(9, 4) = 0.1500;
DECLARE @GrowthRateForStockUp decimal(9, 4) = 0.2000;

DECLARE @FastStartDate date = DATEADD(day, -@FastSalesDays + 1, @AsOfDate);
DECLARE @StableStartDate date = DATEADD(day, -@StableSalesDays + 1, @AsOfDate);
DECLARE @PurchaseStartDate date = DATEADD(day, -@PurchaseDays + 1, @AsOfDate);
DECLARE @NextDate date = DATEADD(day, 1, @AsOfDate);
DECLARE @PreviousSalesDays int = @StableSalesDays - @FastSalesDays;

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
        AND (@StoreCode IS NULL OR s.BranchCode = @StoreCode)
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
        -- 进货数据按进货单口径统计；不限制为已入库单据。
        AND COALESCE(i.InboundDate, i.OrderDate) >= @PurchaseStartDate
        AND COALESCE(i.InboundDate, i.OrderDate) < @NextDate
        AND (
            @StoreCode IS NULL
            OR COALESCE(NULLIF(d.StoreCode, N''), NULLIF(i.StoreCode, N''), NULLIF(srp.StoreCode, N'')) = @StoreCode
        )
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
        AND (@StoreCode IS NULL OR srp.StoreCode = @StoreCode)
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
        AND (@StoreCode IS NULL OR s.BranchCode = @StoreCode)
),
Metrics AS (
    SELECT
        u.BranchCode,
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
    CROSS JOIN GlobalSalesStat gss
    WHERE
        (@StoreCode IS NULL OR u.BranchCode = @StoreCode)
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
                THEN N'先核对货架、后仓和在途；到货不足再向总部/供应商补进。'
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
SELECT
    TOP (ISNULL(@TopRows, 2147483647))
    BranchCode AS [分店代码],
    ProductCode AS [商品编码],
    ProductName AS [商品名称],
    Barcode AS [条码],
    SalesQty30 AS [近30天销量],
    SalesQty90 AS [近90天销量],
    CAST(DailySalesQty30 AS decimal(18, 2)) AS [近30天日均销量],
    CAST(SalesAmount90Aud AS decimal(18, 2)) AS [近90天销售额AUD],
    CAST(GrossProfit90Aud AS decimal(18, 2)) AS [近90天毛利AUD],
    CAST(GrossMarginRate90 AS decimal(18, 4)) AS [近90天毛利率],
    LastSaleDate AS [最近销售日期],
    NoSaleDays AS [连续无销售天数],
    CAST(PurchaseQty180 AS decimal(18, 2)) AS [近180天进货数量],
    SalesQty180 AS [近180天销售数量],
    CAST(EstimatedRemainingQty AS decimal(18, 2)) AS [估算剩余量(非库存)],
    CAST(EstimatedCoverDays AS decimal(18, 2)) AS [估算可卖天数],
    DataCredibility AS [数据可信度],
    DataExceptionFlag AS [数据异常标记],
    N'估算剩余量=近180天进货单数量-近180天销售；不是货架/后仓/财务库存。' AS [口径说明],
    SystemSuggestion AS [系统建议],
    StoreManagerAction AS [店长动作],
    SalesStatLastUpdate AS [销售统计最后更新时间]
FROM FinalRows
ORDER BY
    ActionPriority,
    BranchCode,
    SalesQty30 DESC,
    SalesAmount90Aud DESC,
    ProductCode;
