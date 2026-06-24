using System.Text;
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

        public async Task<ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>> GetPurchaseSalesAnalysisAsync(
            LocalSupplierPurchaseSalesAnalysisQueryDto query,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            try
            {
                var normalized = LocalSupplierInvoiceSalesAnalysisSqlBuilder.NormalizePurchaseSalesAnalysisQuery(
                    query
                );
                var validation =
                    LocalSupplierInvoiceSalesAnalysisSqlBuilder.ValidatePurchaseSalesAnalysisQuery(
                        normalized
                    );
                if (!validation.IsValid)
                {
                    return ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>.Error(
                        validation.Message ?? "进货订单日期范围无效",
                        "VALIDATION_ERROR"
                    );
                }

                var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysis(
                    normalized,
                    scopedStoreCodes
                );

                _logger.LogInformation(
                    "查询分店供应商进货销量分析 StoreScope={StoreScope}, StoreCode={StoreCode}, SupplierCode={SupplierCode}, SortBy={SortBy}, SortOrder={SortOrder}, Page={Page}, PageSize={PageSize}",
                    scopedStoreCodes == null ? "ALL" : string.Join(",", scopedStoreCodes),
                    normalized.StoreCode ?? "ALL",
                    normalized.SupplierCode ?? "ALL",
                    normalized.SortBy,
                    normalized.SortOrder,
                    normalized.Page,
                    normalized.PageSize
                );

                var summaryRows =
                    await _db.Ado.SqlQueryAsync<LocalSupplierPurchaseSalesAnalysisSummaryRow>(
                        sql.SummarySql,
                        sql.Parameters.ToArray()
                    );
                var summary = summaryRows.FirstOrDefault();
                var rows = await _db.Ado.SqlQueryAsync<LocalSupplierPurchaseSalesAnalysisSqlRow>(
                    sql.PagedSql,
                    sql.Parameters.ToArray()
                );

                return ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>.OK(
                    new LocalSupplierPurchaseSalesAnalysisResponseDto
                    {
                        Items = rows.Cast<LocalSupplierPurchaseSalesAnalysisRowDto>().ToList(),
                        Total = summary?.TotalCount ?? 0,
                        Page = normalized.Page,
                        PageSize = normalized.PageSize,
                        SalesStatisticLastUpdate = summary?.SalesStatisticLastUpdate,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分店供应商进货销量分析查询失败");
                return ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>.Error(
                    "分店供应商进货销量分析查询失败",
                    "QUERY_ERROR"
                );
            }
        }

        public async Task<List<LocalSupplierPurchaseSalesAnalysisStoreOptionDto>> GetStoreOptionsAsync(
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysisStoreOptions(
                scopedStoreCodes
            );

            return await _db.Ado.SqlQueryAsync<LocalSupplierPurchaseSalesAnalysisStoreOptionDto>(
                sql.Sql,
                sql.Parameters.ToArray()
            );
        }

        public async Task<List<LocalSupplierPurchaseSalesAnalysisSupplierOptionDto>> GetSupplierOptionsAsync(
            IReadOnlyList<string>? scopedStoreCodes,
            string? storeCode
        )
        {
            var sql =
                LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysisSupplierOptions(
                    storeCode,
                    scopedStoreCodes
                );

            return await _db.Ado.SqlQueryAsync<LocalSupplierPurchaseSalesAnalysisSupplierOptionDto>(
                sql.Sql,
                sql.Parameters.ToArray()
            );
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

        private sealed class LocalSupplierPurchaseSalesAnalysisSqlRow
            : LocalSupplierPurchaseSalesAnalysisRowDto
        {
            public int TotalCount { get; set; }
        }

        private sealed class LocalSupplierPurchaseSalesAnalysisSummaryRow
        {
            public int TotalCount { get; set; }
            public DateTime? SalesStatisticLastUpdate { get; set; }
        }
    }

    public static class LocalSupplierInvoiceSalesAnalysisSqlBuilder
    {
        public const int DefaultPurchaseOrderDateRangeDays = 180;
        public const int MaxPurchaseOrderDateRangeDays = 366;

        private static readonly HashSet<int> AllowedPageSizes = new() { 50, 100, 200 };

        private static readonly Dictionary<string, string> PurchaseSalesSortColumns =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["itemNumber"] = "ItemNumber",
                ["productName"] = "ProductName",
                ["latestPurchaseDate"] = "LatestPurchaseDate",
                ["previousPurchaseDate"] = "PreviousPurchaseDate",
                ["purchaseIntervalDays"] = "PurchaseIntervalDays",
                ["salesBetweenPurchases"] = "SalesBetweenPurchases",
                ["salesQty30"] = "SalesQty30",
                ["salesQty60"] = "SalesQty60",
                ["salesQty90"] = "SalesQty90",
            };

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

        public static LocalSupplierPurchaseSalesAnalysisQueryDto NormalizePurchaseSalesAnalysisQuery(
            LocalSupplierPurchaseSalesAnalysisQueryDto query,
            DateTime? referenceDate = null
        )
        {
            var normalizedSortBy = NormalizeText(query.SortBy);
            if (
                string.IsNullOrWhiteSpace(normalizedSortBy)
                || !PurchaseSalesSortColumns.ContainsKey(normalizedSortBy)
            )
            {
                normalizedSortBy = "latestPurchaseDate";
            }

            var normalizedSortOrder = string.Equals(
                NormalizeText(query.SortOrder),
                "asc",
                StringComparison.OrdinalIgnoreCase
            )
                ? "asc"
                : "desc";

            var pageSize = AllowedPageSizes.Contains(query.PageSize) ? query.PageSize : 100;
            var today = (referenceDate ?? DateTime.Today).Date;
            var orderDateEnd = query.OrderDateEnd?.Date ?? today;
            var orderDateStart = query.OrderDateStart?.Date
                ?? orderDateEnd.AddDays(-DefaultPurchaseOrderDateRangeDays);

            return new LocalSupplierPurchaseSalesAnalysisQueryDto
            {
                StoreCode = NormalizeText(query.StoreCode),
                SupplierCode = NormalizeText(query.SupplierCode),
                OrderDateStart = orderDateStart,
                OrderDateEnd = orderDateEnd,
                Keyword = NormalizeText(query.Keyword),
                SortBy = normalizedSortBy,
                SortOrder = normalizedSortOrder,
                Page = query.Page <= 0 ? 1 : query.Page,
                PageSize = pageSize,
            };
        }

        public static (bool IsValid, string? Message) ValidatePurchaseSalesAnalysisQuery(
            LocalSupplierPurchaseSalesAnalysisQueryDto query
        )
        {
            if (string.IsNullOrWhiteSpace(query.StoreCode))
            {
                return (false, "分店不能为空。");
            }

            if (string.IsNullOrWhiteSpace(query.SupplierCode))
            {
                return (false, "供应商不能为空。");
            }

            if (!query.OrderDateStart.HasValue || !query.OrderDateEnd.HasValue)
            {
                return (false, "进货订单日期范围不能为空。");
            }

            if (query.OrderDateStart.Value.Date > query.OrderDateEnd.Value.Date)
            {
                return (false, "进货订单开始日期不能晚于结束日期。");
            }

            var rangeDays = (query.OrderDateEnd.Value.Date - query.OrderDateStart.Value.Date).TotalDays;
            if (rangeDays > MaxPurchaseOrderDateRangeDays)
            {
                return (
                    false,
                    $"进货订单日期范围不能超过 {MaxPurchaseOrderDateRangeDays} 天。"
                );
            }

            return (true, null);
        }

        public static LocalSupplierPurchaseSalesAnalysisSqlBuildResult BuildPurchaseSalesAnalysis(
            LocalSupplierPurchaseSalesAnalysisQueryDto query,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var normalized = NormalizePurchaseSalesAnalysisQuery(query);
            var validation = ValidatePurchaseSalesAnalysisQuery(normalized);
            if (!validation.IsValid)
            {
                throw new ArgumentException(validation.Message, nameof(query));
            }

            var parameters = new List<SugarParameter>
            {
                new("@Offset", (normalized.Page - 1) * normalized.PageSize),
                new("@PageSize", normalized.PageSize),
            };

            if (normalized.OrderDateStart.HasValue)
            {
                parameters.Add(new SugarParameter("@OrderDateStart", normalized.OrderDateStart.Value));
            }

            if (normalized.OrderDateEnd.HasValue)
            {
                parameters.Add(
                    new SugarParameter("@OrderDateEndExclusive", normalized.OrderDateEnd.Value.AddDays(1))
                );
            }

            if (!string.IsNullOrWhiteSpace(normalized.SupplierCode))
            {
                parameters.Add(new SugarParameter("@SupplierCode", normalized.SupplierCode));
            }

            if (!string.IsNullOrWhiteSpace(normalized.Keyword))
            {
                parameters.Add(
                    new SugarParameter(
                        "@Keyword",
                        "%" + EscapeLikeValue(normalized.Keyword) + "%"
                    )
                );
            }

            var storeCodes = ResolveStoreCodes(normalized.StoreCode, scopedStoreCodes);
            var storeParameterNames = AddStoreParameters(parameters, storeCodes);
            var invoiceStoreFilter = BuildStoreFilter("h.StoreCode", storeParameterNames);
            var invoiceDateFilter = BuildInvoiceDateFilter(normalized);
            var productFilter = BuildProductFilter(normalized);
            var orderBy = BuildPurchaseSalesOrderBy(normalized);

            var coreSql =
                $$"""
WITH FilteredInvoices AS (
    SELECT
        h.InvoiceGUID,
        h.StoreCode,
        h.OrderDate,
        CAST(COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) AS date) AS PurchaseDate
    FROM [StoreLocalSupplierInvoice] h
    WHERE
        COALESCE(h.IsDeleted, 0) = 0
        AND CAST(COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) AS date) IS NOT NULL{{invoiceStoreFilter}}{{invoiceDateFilter}}
),
PurchaseDailyAggregation AS (
    SELECT
        fi.StoreCode AS StoreCode,
        COALESCE(NULLIF(st.StoreName, N''), fi.StoreCode) AS StoreName,
        COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N'')) AS ProductCode,
        COALESCE(NULLIF(d.ItemNumber, N''), NULLIF(p.ItemNumber, N'')) AS ItemNumber,
        COALESCE(NULLIF(d.Barcode, N''), NULLIF(p.Barcode, N'')) AS Barcode,
        COALESCE(NULLIF(d.ProductName, N''), NULLIF(p.ProductName, N'')) AS ProductName,
        NULLIF(p.ProductImage, N'') AS ProductImage,
        COALESCE(NULLIF(p.LocalSupplierCode, N''), NULLIF(srp.SupplierCode, N'')) AS SupplierCode,
        NULLIF(sup.Name, N'') AS SupplierName,
        fi.PurchaseDate AS PurchaseDate,
        SUM(COALESCE(d.Quantity, 0)) AS PurchaseQty
    FROM FilteredInvoices fi
    INNER JOIN [StoreLocalSupplierInvoiceDetails] d
        ON d.InvoiceGUID = fi.InvoiceGUID
        AND COALESCE(d.IsDeleted, 0) = 0
    LEFT JOIN [StoreRetailPrice] srp
        ON srp.UUID = d.StoreProductCode
        AND COALESCE(srp.IsDeleted, 0) = 0
    LEFT JOIN [Product] p
        ON p.ProductCode = COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N''))
        AND COALESCE(p.IsDeleted, 0) = 0
    LEFT JOIN [Store] st
        ON st.StoreCode = fi.StoreCode
        AND COALESCE(st.IsDeleted, 0) = 0
    LEFT JOIN [LocalSupplier] sup
        ON sup.LocalSupplierCode = COALESCE(NULLIF(p.LocalSupplierCode, N''), NULLIF(srp.SupplierCode, N''))
        AND COALESCE(sup.IsDeleted, 0) = 0
    WHERE
        NULLIF(fi.StoreCode, N'') IS NOT NULL
        AND NULLIF(COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N'')), N'') IS NOT NULL{{productFilter}}
        AND NULLIF(COALESCE(NULLIF(p.LocalSupplierCode, N''), NULLIF(srp.SupplierCode, N'')), N'') IS NOT NULL
    GROUP BY
        fi.StoreCode,
        COALESCE(NULLIF(st.StoreName, N''), fi.StoreCode),
        COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N'')),
        COALESCE(NULLIF(d.ItemNumber, N''), NULLIF(p.ItemNumber, N'')),
        COALESCE(NULLIF(d.Barcode, N''), NULLIF(p.Barcode, N'')),
        COALESCE(NULLIF(d.ProductName, N''), NULLIF(p.ProductName, N'')),
        NULLIF(p.ProductImage, N''),
        COALESCE(NULLIF(p.LocalSupplierCode, N''), NULLIF(srp.SupplierCode, N'')),
        NULLIF(sup.Name, N''),
        fi.PurchaseDate
),
RankedPurchases AS (
    SELECT
        pda.StoreCode,
        pda.StoreName,
        pda.ProductCode,
        pda.ItemNumber,
        pda.Barcode,
        pda.ProductName,
        pda.ProductImage,
        pda.SupplierCode,
        pda.SupplierName,
        pda.PurchaseDate,
        pda.PurchaseQty,
        ROW_NUMBER() OVER (
            PARTITION BY pda.StoreCode, pda.ProductCode
            ORDER BY pda.PurchaseDate DESC
        ) AS PurchaseRank
    FROM PurchaseDailyAggregation pda
),
LatestPurchases AS (
    SELECT
        rp.StoreCode,
        rp.StoreName,
        rp.ProductCode,
        rp.ItemNumber,
        rp.Barcode,
        rp.ProductName,
        rp.ProductImage,
        rp.SupplierCode,
        rp.SupplierName,
        rp.PurchaseDate AS LatestPurchaseDate,
        rp.PurchaseQty AS LatestPurchaseQty
    FROM RankedPurchases rp
    WHERE rp.PurchaseRank = 1
),
PreviousPurchases AS (
    SELECT
        rp.StoreCode,
        rp.ProductCode,
        rp.PurchaseDate AS PreviousPurchaseDate,
        rp.PurchaseQty AS PreviousPurchaseQty
    FROM RankedPurchases rp
    WHERE rp.PurchaseRank = 2
),
SalesMetrics AS (
    SELECT
        lp.StoreCode,
        lp.ProductCode,
        SUM(CASE WHEN s.Date >= lp.LatestPurchaseDate AND s.Date < DATEADD(day, 30, lp.LatestPurchaseDate) THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesQty30,
        SUM(CASE WHEN s.Date >= lp.LatestPurchaseDate AND s.Date < DATEADD(day, 60, lp.LatestPurchaseDate) THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesQty60,
        SUM(CASE WHEN s.Date >= lp.LatestPurchaseDate AND s.Date < DATEADD(day, 90, lp.LatestPurchaseDate) THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesQty90,
        SUM(CASE WHEN pp.PreviousPurchaseDate IS NOT NULL AND s.Date >= pp.PreviousPurchaseDate AND s.Date < lp.LatestPurchaseDate THEN COALESCE(s.TotalQuantity, 0) ELSE 0 END) AS SalesBetweenPurchases,
        MAX(s.UpdateTime) AS SalesStatisticLastUpdate
    FROM LatestPurchases lp
    LEFT JOIN PreviousPurchases pp
        ON pp.StoreCode = lp.StoreCode
        AND pp.ProductCode = lp.ProductCode
    LEFT JOIN [ProductStoreDailySalesStatistic] s
        ON s.BranchCode = lp.StoreCode
        AND s.ProductCode = lp.ProductCode
        AND (
            (s.Date >= lp.LatestPurchaseDate AND s.Date < DATEADD(day, 90, lp.LatestPurchaseDate))
            OR (pp.PreviousPurchaseDate IS NOT NULL AND s.Date >= pp.PreviousPurchaseDate AND s.Date < lp.LatestPurchaseDate)
        )
    GROUP BY
        lp.StoreCode,
        lp.ProductCode
),
FinalRows AS (
    SELECT
        lp.StoreCode AS StoreCode,
        lp.StoreName AS StoreName,
        lp.ProductCode AS ProductCode,
        lp.ItemNumber AS ItemNumber,
        lp.Barcode AS Barcode,
        lp.ProductName AS ProductName,
        lp.ProductImage AS ProductImage,
        lp.SupplierCode AS SupplierCode,
        lp.SupplierName AS SupplierName,
        lp.LatestPurchaseDate AS LatestPurchaseDate,
        CAST(lp.LatestPurchaseQty AS decimal(18, 2)) AS LatestPurchaseQty,
        pp.PreviousPurchaseDate AS PreviousPurchaseDate,
        CAST(pp.PreviousPurchaseQty AS decimal(18, 2)) AS PreviousPurchaseQty,
        CASE
            WHEN pp.PreviousPurchaseDate IS NULL THEN NULL
            ELSE DATEDIFF(day, pp.PreviousPurchaseDate, lp.LatestPurchaseDate)
        END AS PurchaseIntervalDays,
        CASE
            WHEN pp.PreviousPurchaseDate IS NULL THEN NULL
            ELSE COALESCE(sm.SalesBetweenPurchases, 0)
        END AS SalesBetweenPurchases,
        COALESCE(sm.SalesQty30, 0) AS SalesQty30,
        COALESCE(sm.SalesQty60, 0) AS SalesQty60,
        COALESCE(sm.SalesQty90, 0) AS SalesQty90,
        sm.SalesStatisticLastUpdate AS SalesStatisticLastUpdate
    FROM LatestPurchases lp
    LEFT JOIN PreviousPurchases pp
        ON pp.StoreCode = lp.StoreCode
        AND pp.ProductCode = lp.ProductCode
    LEFT JOIN SalesMetrics sm
        ON sm.StoreCode = lp.StoreCode
        AND sm.ProductCode = lp.ProductCode
)
""";

            var pagedSql =
                coreSql
                + $$"""
SELECT
    StoreCode,
    StoreName,
    ProductCode,
    ItemNumber,
    Barcode,
    ProductName,
    ProductImage,
    SupplierCode,
    SupplierName,
    LatestPurchaseDate,
    LatestPurchaseQty,
    PreviousPurchaseDate,
    PreviousPurchaseQty,
    PurchaseIntervalDays,
    SalesBetweenPurchases,
    SalesQty30,
    SalesQty60,
    SalesQty90,
    SalesStatisticLastUpdate
FROM FinalRows
ORDER BY
    {{orderBy}}
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
""";

            var summarySql =
                coreSql
                + """
SELECT
    COUNT(1) AS TotalCount,
    MAX(SalesStatisticLastUpdate) AS SalesStatisticLastUpdate
FROM FinalRows
""";

            return new LocalSupplierPurchaseSalesAnalysisSqlBuildResult
            {
                PagedSql = pagedSql,
                SummarySql = summarySql,
                Parameters = parameters,
            };
        }

        public static LocalSupplierInvoiceSalesAnalysisSqlBuildResult BuildPurchaseSalesAnalysisStoreOptions(
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var parameters = new List<SugarParameter>();
            var storeParameterNames = AddStoreParameters(parameters, scopedStoreCodes);
            var storeFilter = BuildStoreFilter("h.StoreCode", storeParameterNames);

            // 下拉候选从报表真实进货数据取，避免主数据 Active 标记缺失时页面无可选分店。
            var sql =
                $$"""
SELECT
    COALESCE(NULLIF(st.StoreName, N''), source.StoreCode) AS Label,
    source.StoreCode AS Value
FROM (
    SELECT DISTINCT
        h.StoreCode
    FROM [StoreLocalSupplierInvoice] h
    WHERE
        COALESCE(h.IsDeleted, 0) = 0
        AND NULLIF(h.StoreCode, N'') IS NOT NULL{{storeFilter}}
) source
LEFT JOIN [Store] st
    ON st.StoreCode = source.StoreCode
    AND COALESCE(st.IsDeleted, 0) = 0
ORDER BY
    source.StoreCode
""";

            return new LocalSupplierInvoiceSalesAnalysisSqlBuildResult
            {
                Sql = sql,
                Parameters = parameters,
            };
        }

        public static LocalSupplierInvoiceSalesAnalysisSqlBuildResult BuildPurchaseSalesAnalysisSupplierOptions(
            string? storeCode,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var parameters = new List<SugarParameter>();
            var storeCodes = ResolveStoreCodes(storeCode, scopedStoreCodes);
            var storeParameterNames = AddStoreParameters(parameters, storeCodes);
            var storeFilter = BuildStoreFilter("h.StoreCode", storeParameterNames);

            // 供应商候选和主查询保持同一口径：商品主供应商优先，分店价格表供应商兜底。
            var sql =
                $$"""
SELECT
    COALESCE(NULLIF(sup.Name, N''), source.SupplierCode) AS Label,
    source.SupplierCode AS Value
FROM (
    SELECT DISTINCT
        COALESCE(NULLIF(p.LocalSupplierCode, N''), NULLIF(srp.SupplierCode, N'')) AS SupplierCode
    FROM [StoreLocalSupplierInvoice] h
    INNER JOIN [StoreLocalSupplierInvoiceDetails] d
        ON d.InvoiceGUID = h.InvoiceGUID
        AND COALESCE(d.IsDeleted, 0) = 0
    LEFT JOIN [StoreRetailPrice] srp
        ON srp.UUID = d.StoreProductCode
        AND COALESCE(srp.IsDeleted, 0) = 0
    LEFT JOIN [Product] p
        ON p.ProductCode = COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N''))
        AND COALESCE(p.IsDeleted, 0) = 0
    WHERE
        COALESCE(h.IsDeleted, 0) = 0
        AND NULLIF(h.StoreCode, N'') IS NOT NULL{{storeFilter}}
        AND NULLIF(COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N'')), N'') IS NOT NULL
        AND NULLIF(COALESCE(NULLIF(p.LocalSupplierCode, N''), NULLIF(srp.SupplierCode, N'')), N'') IS NOT NULL
) source
LEFT JOIN [LocalSupplier] sup
    ON sup.LocalSupplierCode = source.SupplierCode
    AND COALESCE(sup.IsDeleted, 0) = 0
ORDER BY
    Label,
    Value
""";

            return new LocalSupplierInvoiceSalesAnalysisSqlBuildResult
            {
                Sql = sql,
                Parameters = parameters,
            };
        }

        public static IReadOnlyList<string>? AddStoreParametersForStoreOptions(
            List<SugarParameter> parameters,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            return AddStoreParameters(parameters, scopedStoreCodes);
        }

        public static string BuildStoreFilterForStoreOptions(
            string column,
            IReadOnlyList<string>? storeParameterNames
        )
        {
            return BuildStoreFilter(column, storeParameterNames);
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

        private static IReadOnlyList<string>? ResolveStoreCodes(
            string? requestedStoreCode,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var normalizedScopedStores = scopedStoreCodes?
                .Select(NormalizeText)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var normalizedRequestedStore = NormalizeText(requestedStoreCode);
            if (!string.IsNullOrWhiteSpace(normalizedRequestedStore))
            {
                if (normalizedScopedStores == null)
                {
                    return new[] { normalizedRequestedStore };
                }

                // service/builder 层再做一次门店 scope 交集，避免未来复用时绕过 controller 校验。
                return normalizedScopedStores.Contains(
                    normalizedRequestedStore,
                    StringComparer.OrdinalIgnoreCase
                )
                    ? new[] { normalizedRequestedStore }
                    : Array.Empty<string>();
            }

            if (normalizedScopedStores == null)
            {
                return null;
            }

            return normalizedScopedStores;
        }

        private static IReadOnlyList<string>? AddStoreParameters(
            List<SugarParameter> parameters,
            IReadOnlyList<string>? storeCodes
        )
        {
            if (storeCodes == null)
            {
                return null;
            }

            if (storeCodes.Count == 0)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>(storeCodes.Count);
            for (var index = 0; index < storeCodes.Count; index++)
            {
                var parameterName = "@StoreCode" + index;
                names.Add(parameterName);
                parameters.Add(new SugarParameter(parameterName, storeCodes[index]));
            }

            return names;
        }

        private static string BuildStoreFilter(
            string column,
            IReadOnlyList<string>? storeParameterNames
        )
        {
            if (storeParameterNames == null)
            {
                return string.Empty;
            }

            if (storeParameterNames.Count == 0)
            {
                return "\n        AND 1 = 0";
            }

            return
                "\n        AND "
                + column
                + " IN ("
                + string.Join(", ", storeParameterNames)
                + ")";
        }

        private static string BuildInvoiceDateFilter(LocalSupplierPurchaseSalesAnalysisQueryDto query)
        {
            var builder = new StringBuilder();

            if (query.OrderDateStart.HasValue)
            {
                builder.Append("\n        AND h.OrderDate >= @OrderDateStart");
            }

            if (query.OrderDateEnd.HasValue)
            {
                builder.Append("\n        AND h.OrderDate < @OrderDateEndExclusive");
            }

            return builder.ToString();
        }

        private static string BuildProductFilter(LocalSupplierPurchaseSalesAnalysisQueryDto query)
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(query.SupplierCode))
            {
                // 供应商筛选必须优先取商品主供应商，再回退到分店价格表供应商。
                builder.Append(
                    "\n        AND COALESCE(NULLIF(p.LocalSupplierCode, N''), NULLIF(srp.SupplierCode, N'')) = @SupplierCode"
                );
            }

            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                builder.Append(
                    "\n        AND (\n"
                        + "            COALESCE(NULLIF(d.ProductCode, N''), NULLIF(srp.ProductCode, N'')) LIKE @Keyword ESCAPE '\\'\n"
                        + "            OR COALESCE(NULLIF(d.ItemNumber, N''), NULLIF(p.ItemNumber, N'')) LIKE @Keyword ESCAPE '\\'\n"
                        + "            OR COALESCE(NULLIF(d.ProductName, N''), NULLIF(p.ProductName, N'')) LIKE @Keyword ESCAPE '\\'\n"
                        + "            OR COALESCE(NULLIF(d.Barcode, N''), NULLIF(p.Barcode, N'')) LIKE @Keyword ESCAPE '\\'\n"
                        + "        )"
                );
            }

            return builder.ToString();
        }

        private static string BuildPurchaseSalesOrderBy(
            LocalSupplierPurchaseSalesAnalysisQueryDto query
        )
        {
            var sortBy = NormalizeText(query.SortBy);
            if (
                string.IsNullOrWhiteSpace(sortBy)
                || !PurchaseSalesSortColumns.TryGetValue(sortBy, out var column)
            )
            {
                column = PurchaseSalesSortColumns["latestPurchaseDate"];
            }

            var direction = string.Equals(query.SortOrder, "asc", StringComparison.OrdinalIgnoreCase)
                ? "ASC"
                : "DESC";

            return $"{column} {direction}, StoreCode ASC, ProductCode ASC";
        }

        private static string EscapeLikeValue(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal)
                .Replace("[", "\\[", StringComparison.Ordinal);
        }

        private static List<SugarParameter> BuildParameters(string invoiceGuid)
        {
            if (string.IsNullOrWhiteSpace(invoiceGuid))
            {
                throw new ArgumentException("进货单 GUID 不能为空。", nameof(invoiceGuid));
            }

            return new List<SugarParameter> { new("@InvoiceGuid", invoiceGuid.Trim()) };
        }

        private static string? NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public sealed class LocalSupplierInvoiceSalesAnalysisSqlBuildResult
    {
        public string Sql { get; set; } = string.Empty;
        public List<SugarParameter> Parameters { get; set; } = new();
    }

    public sealed class LocalSupplierPurchaseSalesAnalysisSqlBuildResult
    {
        public string PagedSql { get; set; } = string.Empty;
        public string SummarySql { get; set; } = string.Empty;
        public List<SugarParameter> Parameters { get; set; } = new();
    }
}
