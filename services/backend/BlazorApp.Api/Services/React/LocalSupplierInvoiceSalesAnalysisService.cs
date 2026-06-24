using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class LocalSupplierInvoiceSalesAnalysisService
        : ILocalSupplierInvoiceSalesAnalysisService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<LocalSupplierInvoiceSalesAnalysisService> _logger;

        public LocalSupplierInvoiceSalesAnalysisService(
            SqlSugarContext context,
            ILogger<LocalSupplierInvoiceSalesAnalysisService> logger
        )
        {
            _db = context.Db;
            _logger = logger;
        }

        public async Task<ApiResponse<LocalSupplierInvoiceSalesAnalysisResponseDto>> GetAnalysisAsync(
            string invoiceGuid
        )
        {
            try
            {
                var headerSql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildHeader(invoiceGuid);
                var headerRows = await _db.Ado.SqlQueryAsync<LocalSupplierInvoiceSalesAnalysisHeaderRow>(
                    headerSql.Sql,
                    headerSql.Parameters.ToArray()
                );
                var header = headerRows.FirstOrDefault();
                if (header == null)
                {
                    return ApiResponse<LocalSupplierInvoiceSalesAnalysisResponseDto>.Error(
                        "进货单不存在",
                        "NOT_FOUND"
                    );
                }

                var detailSql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.Build(invoiceGuid);
                var rows = await _db.Ado.SqlQueryAsync<LocalSupplierInvoiceSalesAnalysisItemRow>(
                    detailSql.Sql,
                    detailSql.Parameters.ToArray()
                );

                return ApiResponse<LocalSupplierInvoiceSalesAnalysisResponseDto>.OK(
                    new LocalSupplierInvoiceSalesAnalysisResponseDto
                    {
                        InvoiceGUID = header.InvoiceGUID,
                        InvoiceNo = header.InvoiceNo,
                        StoreCode = header.StoreCode,
                        StoreName = header.StoreName,
                        SupplierCode = header.SupplierCode,
                        SupplierName = header.SupplierName,
                        OrderDate = header.OrderDate,
                        InboundDate = header.InboundDate,
                        AnalysisDate = header.AnalysisDate,
                        Items = rows.Cast<LocalSupplierInvoiceSalesAnalysisItemDto>().ToList(),
                        SalesStatisticLastUpdate = rows
                            .Select(item => item.SalesStatisticLastUpdate)
                            .Where(value => value.HasValue)
                            .DefaultIfEmpty()
                            .Max(),
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分店进货单销量分析查询失败 InvoiceGuid={InvoiceGuid}", invoiceGuid);
                return ApiResponse<LocalSupplierInvoiceSalesAnalysisResponseDto>.Error(
                    "分店进货单销量分析查询失败",
                    "QUERY_ERROR"
                );
            }
        }

        private sealed class LocalSupplierInvoiceSalesAnalysisHeaderRow
        {
            public string InvoiceGUID { get; set; } = string.Empty;
            public string? InvoiceNo { get; set; }
            public string? StoreCode { get; set; }
            public string? StoreName { get; set; }
            public string? SupplierCode { get; set; }
            public string? SupplierName { get; set; }
            public DateTime? OrderDate { get; set; }
            public DateTime? InboundDate { get; set; }
            public DateTime? AnalysisDate { get; set; }
        }

        private sealed class LocalSupplierInvoiceSalesAnalysisItemRow
            : LocalSupplierInvoiceSalesAnalysisItemDto { }
    }

    public static class LocalSupplierInvoiceSalesAnalysisSqlBuilder
    {
        public static LocalSupplierInvoiceSalesAnalysisSqlBuildResult BuildHeader(string invoiceGuid)
        {
            var parameters = BuildParameters(invoiceGuid);
            var sql =
                "SELECT TOP 1\n"
                + "    h.InvoiceGUID AS InvoiceGUID,\n"
                + "    h.InvoiceNo AS InvoiceNo,\n"
                + "    h.StoreCode AS StoreCode,\n"
                + "    st.StoreName AS StoreName,\n"
                + "    h.SupplierCode AS SupplierCode,\n"
                + "    sup.Name AS SupplierName,\n"
                + "    h.OrderDate AS OrderDate,\n"
                + "    h.InboundDate AS InboundDate,\n"
                + "    CAST(COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) AS date) AS AnalysisDate\n"
                + "FROM [StoreLocalSupplierInvoice] h\n"
                + "LEFT JOIN [Store] st\n"
                + "    ON st.StoreCode = h.StoreCode\n"
                + "    AND COALESCE(st.IsDeleted, 0) = 0\n"
                + "LEFT JOIN [LocalSupplier] sup\n"
                + "    ON sup.LocalSupplierCode = h.SupplierCode\n"
                + "    AND COALESCE(sup.IsDeleted, 0) = 0\n"
                + "WHERE\n"
                + "    h.InvoiceGUID = @InvoiceGuid\n"
                + "    AND COALESCE(h.IsDeleted, 0) = 0";

            return new LocalSupplierInvoiceSalesAnalysisSqlBuildResult
            {
                Sql = sql,
                Parameters = parameters,
            };
        }

        public static LocalSupplierInvoiceSalesAnalysisSqlBuildResult Build(string invoiceGuid)
        {
            var parameters = BuildParameters(invoiceGuid);
            var sql =
                "WITH CurrentDetails AS (\n"
                + "    SELECT\n"
                + "        d.DetailGUID,\n"
                + "        d.InvoiceGUID,\n"
                + "        COALESCE(NULLIF(d.StoreCode, N''), NULLIF(h.StoreCode, N''), NULLIF(srp.StoreCode, N'')) AS StoreCode,\n"
                + "        -- 本次进货后销量以入库日优先；未入库时回退到订单日。\n"
                + "        CAST(COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) AS date) AS AnalysisDate,\n"
                + "        COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N'')) AS ProductCode,\n"
                + "        NULLIF(d.ItemNumber, N'') AS ItemNumber,\n"
                + "        COALESCE(NULLIF(d.Barcode, N''), NULLIF(p.Barcode, N'')) AS Barcode,\n"
                + "        COALESCE(NULLIF(d.ProductName, N''), NULLIF(p.ProductName, N'')) AS ProductName,\n"
                + "        -- 进货明细的 ProductImage 是代码层忽略字段，实际图片只从 Product 表读取。\n"
                + "        NULLIF(p.ProductImage, N'') AS ProductImage,\n"
                + "        NULLIF(d.Specification, N'') AS Specification,\n"
                + "        NULLIF(d.Unit, N'') AS Unit,\n"
                + "        d.Quantity,\n"
                + "        d.PurchasePrice,\n"
                + "        d.RetailPrice,\n"
                + "        d.Amount\n"
                + "    FROM [StoreLocalSupplierInvoiceDetails] d\n"
                + "    INNER JOIN [StoreLocalSupplierInvoice] h\n"
                + "        ON h.InvoiceGUID = d.InvoiceGUID\n"
                + "        AND COALESCE(h.IsDeleted, 0) = 0\n"
                + "    LEFT JOIN [StoreRetailPrice] srp\n"
                + "        ON srp.UUID = d.StoreProductCode\n"
                + "        AND COALESCE(srp.IsDeleted, 0) = 0\n"
                + "    LEFT JOIN [Product] p\n"
                + "        ON p.ProductCode = COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N''))\n"
                + "        AND COALESCE(p.IsDeleted, 0) = 0\n"
                + "    WHERE\n"
                + "        d.InvoiceGUID = @InvoiceGuid\n"
                + "        AND COALESCE(d.IsDeleted, 0) = 0\n"
                + "),\n"
                + "CurrentProducts AS (\n"
                + "    SELECT\n"
                + "        cd.StoreCode,\n"
                + "        cd.ProductCode,\n"
                + "        MIN(cd.AnalysisDate) AS AnalysisDate\n"
                + "    FROM CurrentDetails cd\n"
                + "    WHERE\n"
                + "        cd.StoreCode IS NOT NULL\n"
                + "        AND cd.ProductCode IS NOT NULL\n"
                + "    GROUP BY\n"
                + "        cd.StoreCode,\n"
                + "        cd.ProductCode\n"
                + "),\n"
                + "PreviousPurchase AS (\n"
                + "    SELECT\n"
                + "        cp.StoreCode,\n"
                + "        cp.ProductCode,\n"
                + "        MAX(COALESCE(pi.InboundDate, pi.OrderDate)) AS PreviousPurchaseDate\n"
                + "    FROM CurrentProducts cp\n"
                + "    INNER JOIN [StoreLocalSupplierInvoice] pi\n"
                + "        ON pi.StoreCode = cp.StoreCode\n"
                + "        AND COALESCE(pi.IsDeleted, 0) = 0\n"
                + "        AND COALESCE(pi.InboundDate, pi.OrderDate) IS NOT NULL\n"
                + "        AND CAST(COALESCE(pi.InboundDate, pi.OrderDate) AS date) < cp.AnalysisDate\n"
                + "        AND pi.InvoiceGUID <> @InvoiceGuid\n"
                + "    INNER JOIN [StoreLocalSupplierInvoiceDetails] pd\n"
                + "        ON pd.InvoiceGUID = pi.InvoiceGUID\n"
                + "        AND COALESCE(pd.IsDeleted, 0) = 0\n"
                + "        -- 历史明细已有商品编码，直接匹配可避开 400 万级分店价格表回填。\n"
                + "        AND NULLIF(pd.ProductCode, N'') = cp.ProductCode\n"
                + "    GROUP BY\n"
                + "        cp.StoreCode,\n"
                + "        cp.ProductCode\n"
                + "),\n"
                + "SalesMetrics AS (\n"
                + "    SELECT\n"
                + "        cp.StoreCode,\n"
                + "        cp.ProductCode,\n"
                + "        SUM(CASE WHEN s.Date >= DATEADD(day, 1, cp.AnalysisDate) AND s.Date < DATEADD(day, 31, cp.AnalysisDate) THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesQty30,\n"
                + "        SUM(CASE WHEN s.Date >= DATEADD(day, 1, cp.AnalysisDate) AND s.Date < DATEADD(day, 61, cp.AnalysisDate) THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesQty60,\n"
                + "        SUM(CASE WHEN s.Date >= DATEADD(day, 1, cp.AnalysisDate) AND s.Date < DATEADD(day, 91, cp.AnalysisDate) THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesQty90,\n"
                + "        SUM(CASE WHEN pp.PreviousPurchaseDate IS NOT NULL AND s.Date >= CAST(pp.PreviousPurchaseDate AS date) AND s.Date < DATEADD(day, 1, cp.AnalysisDate) THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesSincePreviousPurchase,\n"
                + "        SUM(CASE WHEN pp.PreviousPurchaseDate IS NOT NULL AND s.Date >= CASE WHEN DATEADD(day, -29, cp.AnalysisDate) > CAST(pp.PreviousPurchaseDate AS date) THEN DATEADD(day, -29, cp.AnalysisDate) ELSE CAST(pp.PreviousPurchaseDate AS date) END AND s.Date < DATEADD(day, 1, cp.AnalysisDate) THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesSincePreviousPurchase30,\n"
                + "        SUM(CASE WHEN pp.PreviousPurchaseDate IS NOT NULL AND s.Date >= CASE WHEN DATEADD(day, -59, cp.AnalysisDate) > CAST(pp.PreviousPurchaseDate AS date) THEN DATEADD(day, -59, cp.AnalysisDate) ELSE CAST(pp.PreviousPurchaseDate AS date) END AND s.Date < DATEADD(day, 1, cp.AnalysisDate) THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesSincePreviousPurchase60,\n"
                + "        SUM(CASE WHEN pp.PreviousPurchaseDate IS NOT NULL AND s.Date >= CASE WHEN DATEADD(day, -89, cp.AnalysisDate) > CAST(pp.PreviousPurchaseDate AS date) THEN DATEADD(day, -89, cp.AnalysisDate) ELSE CAST(pp.PreviousPurchaseDate AS date) END AND s.Date < DATEADD(day, 1, cp.AnalysisDate) THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesSincePreviousPurchase90,\n"
                + "        MAX(s.UpdateTime) AS SalesStatisticLastUpdate\n"
                + "    FROM CurrentProducts cp\n"
                + "    LEFT JOIN PreviousPurchase pp\n"
                + "        ON pp.StoreCode = cp.StoreCode\n"
                + "        AND pp.ProductCode = cp.ProductCode\n"
                + "    LEFT JOIN [ProductStoreDailySalesStatistic] s\n"
                + "        ON s.BranchCode = cp.StoreCode\n"
                + "        AND s.ProductCode = cp.ProductCode\n"
                + "        -- 日销售统计只有日期粒度：进货后30/60/90统计本次进货次日起未来窗口，历史区间仍截止到本次进货日。\n"
                + "        AND (\n"
                + "            (s.Date >= DATEADD(day, 1, cp.AnalysisDate) AND s.Date < DATEADD(day, 91, cp.AnalysisDate))\n"
                + "            OR (pp.PreviousPurchaseDate IS NOT NULL AND s.Date >= CAST(pp.PreviousPurchaseDate AS date) AND s.Date < DATEADD(day, 1, cp.AnalysisDate))\n"
                + "        )\n"
                + "    GROUP BY\n"
                + "        cp.StoreCode,\n"
                + "        cp.ProductCode\n"
                + ")\n"
                + "SELECT\n"
                + "    cd.DetailGUID AS DetailGUID,\n"
                + "    cd.ProductCode AS ProductCode,\n"
                + "    cd.ItemNumber AS ItemNumber,\n"
                + "    cd.Barcode AS Barcode,\n"
                + "    cd.ProductName AS ProductName,\n"
                + "    cd.ProductImage AS ProductImage,\n"
                + "    cd.Specification AS Specification,\n"
                + "    cd.Unit AS Unit,\n"
                + "    cd.Quantity AS Quantity,\n"
                + "    cd.PurchasePrice AS PurchasePrice,\n"
                + "    cd.RetailPrice AS RetailPrice,\n"
                + "    cd.Amount AS Amount,\n"
                + "    COALESCE(sm.SalesQty30, 0) AS SalesQty30,\n"
                + "    COALESCE(sm.SalesQty60, 0) AS SalesQty60,\n"
                + "    COALESCE(sm.SalesQty90, 0) AS SalesQty90,\n"
                + "    pp.PreviousPurchaseDate AS PreviousPurchaseDate,\n"
                + "    CASE WHEN pp.PreviousPurchaseDate IS NULL THEN NULL ELSE DATEDIFF(day, pp.PreviousPurchaseDate, cd.AnalysisDate) END AS PreviousToCurrentDays,\n"
                + "    CASE WHEN pp.PreviousPurchaseDate IS NULL THEN NULL ELSE COALESCE(sm.SalesSincePreviousPurchase, 0) END AS SalesSincePreviousPurchase,\n"
                + "    CASE WHEN pp.PreviousPurchaseDate IS NULL THEN NULL ELSE COALESCE(sm.SalesSincePreviousPurchase30, 0) END AS SalesSincePreviousPurchase30,\n"
                + "    CASE WHEN pp.PreviousPurchaseDate IS NULL THEN NULL ELSE COALESCE(sm.SalesSincePreviousPurchase60, 0) END AS SalesSincePreviousPurchase60,\n"
                + "    CASE WHEN pp.PreviousPurchaseDate IS NULL THEN NULL ELSE COALESCE(sm.SalesSincePreviousPurchase90, 0) END AS SalesSincePreviousPurchase90,\n"
                + "    sm.SalesStatisticLastUpdate AS SalesStatisticLastUpdate\n"
                + "FROM CurrentDetails cd\n"
                + "LEFT JOIN PreviousPurchase pp\n"
                + "    ON pp.StoreCode = cd.StoreCode\n"
                + "    AND pp.ProductCode = cd.ProductCode\n"
                + "LEFT JOIN SalesMetrics sm\n"
                + "    ON sm.StoreCode = cd.StoreCode\n"
                + "    AND sm.ProductCode = cd.ProductCode\n"
                + "ORDER BY\n"
                + "    cd.ProductCode,\n"
                + "    cd.DetailGUID";

            return new LocalSupplierInvoiceSalesAnalysisSqlBuildResult
            {
                Sql = sql,
                Parameters = parameters,
            };
        }

        public static bool ContainsWriteKeyword(string sql)
        {
            var upper = sql.ToUpperInvariant();
            var unsafeWords = new[]
            {
                " INSERT ",
                " UPDATE ",
                " DELETE ",
                " MERGE ",
                " CREATE ",
                " ALTER ",
                " DROP ",
                " TRUNCATE ",
                " EXEC ",
            };
            return unsafeWords.Any(upper.Contains);
        }

        private static List<SugarParameter> BuildParameters(string invoiceGuid)
        {
            if (string.IsNullOrWhiteSpace(invoiceGuid))
            {
                throw new ArgumentException("进货单 GUID 不能为空。", nameof(invoiceGuid));
            }

            return new List<SugarParameter> { new("@InvoiceGuid", invoiceGuid.Trim()) };
        }
    }

    public sealed class LocalSupplierInvoiceSalesAnalysisSqlBuildResult
    {
        public string Sql { get; set; } = string.Empty;
        public List<SugarParameter> Parameters { get; set; } = new();
    }
}
