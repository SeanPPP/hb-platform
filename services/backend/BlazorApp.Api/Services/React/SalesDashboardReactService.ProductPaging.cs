using System.Text.Json;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public partial class SalesDashboardReactService
{
    /// <summary>
    /// SQL Server 商品报表读取结果。无数据页仍返回一行 HasData=false，保证越界页可以得到准确 Total。
    /// </summary>
    internal sealed class ProductReportPagingSqlRow
    {
        public bool HasData { get; set; }
        public int TotalCount { get; set; }
        public string? ProductCode { get; set; }
        public string? CurrentProductName { get; set; }
        public string? CompareProductName { get; set; }
        public int CurrentQuantity { get; set; }
        public decimal CurrentSalesAmount { get; set; }
        public int CurrentOrderCount { get; set; }
        public decimal? CurrentGrossProfit { get; set; }
        public int CurrentStatisticRowCount { get; set; }
        public int CurrentCostedRowCount { get; set; }
        public int CurrentGrossProfitRowCount { get; set; }
        public int CompareQuantity { get; set; }
        public decimal CompareSalesAmount { get; set; }
        public int CompareOrderCount { get; set; }
        public decimal? CompareGrossProfit { get; set; }
        public int CompareStatisticRowCount { get; set; }
        public int CompareCostedRowCount { get; set; }
        public int CompareGrossProfitRowCount { get; set; }
        public string? ItemNumber { get; set; }
        public string? ProductImage { get; set; }
    }

    /// <summary>
    /// 在 SQL Server 中一次完成两期商品聚合和分页。输入集合全部通过 JSON 参数传递，避免大 IN 参数和分批往返。
    /// </summary>
    internal async Task<PagedSalesProductDetailWithDiscountDto> GetEnhancedSalesProductDetailsSqlServerAsync(
        DateRangeDto dateRange,
        List<string>? branchCodes,
        List<string>? localSupplierCodes,
        List<string>? chinaSupplierCodes,
        int pageIndex,
        int pageSize,
        string? productSearch,
        bool chinaSupplierScope
    )
    {
        ValidateDateRange(dateRange);
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 100);

        if (_context.Db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            throw new InvalidOperationException("商品分页 SQL 优化仅支持 SQL Server。");
        }

        var normalizedBranches = NormalizeCodes(branchCodes);
        if (branchCodes is not null && normalizedBranches.Count == 0)
        {
            return CreateEmptyProductPagingResult(pageIndex, pageSize);
        }
        var normalizedLocal = NormalizeCodes(chinaSupplierScope ? null : localSupplierCodes);
        var normalizedChina = NormalizeCodes(chinaSupplierCodes);

        // 中国范围优先于遗留本地供应商参数；映射和供应商集合各读取一次，随后在同一 SQL 中复用。
        var chinaProductMap = normalizedChina.Any()
            ? await GetChinaSupplierProductMapAsync(normalizedChina)
            : chinaSupplierScope
                ? await GetChinaSupplierProductMapAsync()
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (chinaSupplierScope && !normalizedChina.Any())
        {
            normalizedChina = (await GetChinaSupplierCodeSetAsync(chinaProductMap.Values)).ToList();
            if (!normalizedChina.Any())
            {
                return CreateEmptyProductPagingResult(pageIndex, pageSize);
            }
        }

        // 本地供应商选择 200 时，现有口径包含 200 旧行和日统计中直写的全部中国供应商行。
        var sqlLocalSupplierCodes = normalizedLocal;
        if (!normalizedChina.Any() && normalizedLocal.Any(IsChinaLocalSupplierCode))
        {
            sqlLocalSupplierCodes = normalizedLocal
                .Where(code => !IsChinaLocalSupplierCode(code))
                .Concat(new[] { CHINA_LOCAL_SUPPLIER_CODE })
                .Concat(await GetChinaSupplierCodeSetAsync())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(productSearch) ? null : productSearch.Trim();
        var hasCompare = dateRange.CompareStartDate.HasValue && dateRange.CompareEndDate.HasValue;
        var selectedChinaCodes = normalizedChina.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parameters = new List<SugarParameter>
        {
            new("@CurrentStart", dateRange.StartDate.Date),
            new("@CurrentEndExclusive", dateRange.EndDate.Date.AddDays(1)),
            new("@Branches", JsonSerializer.Serialize(normalizedBranches)),
            new("@HasBranchFilter", normalizedBranches.Any()),
            new("@LocalSuppliers", JsonSerializer.Serialize(sqlLocalSupplierCodes)),
            new("@HasLocalFilter", sqlLocalSupplierCodes.Any()),
            new("@ChinaSuppliers", JsonSerializer.Serialize(normalizedChina)),
            // 映射已限定为当前中国供应商集合，SQL 无需在每一行重复匹配供应商表。
            new("@ChinaProductMap", JsonSerializer.Serialize(chinaProductMap
                .Where(pair => selectedChinaCodes.Contains(pair.Value)).Select(pair => new
            {
                ProductCode = pair.Key,
                ChinaSupplierCode = pair.Value,
            }))),
            new("@HasChinaFilter", normalizedChina.Any()),
            new("@ProductSearch", normalizedSearch is null ? DBNull.Value : EscapeLikeValue(normalizedSearch)),
            new("@PageOffset", checked((long)(pageIndex - 1) * pageSize)),
            new("@PageSize", pageSize),
        };
        if (hasCompare)
        {
            parameters.Add(new SugarParameter("@CompareStart", dateRange.CompareStartDate!.Value.Date));
            parameters.Add(new SugarParameter("@CompareEndExclusive", dateRange.CompareEndDate!.Value.Date.AddDays(1)));
        }

        var rows = await _context.Db.Ado.SqlQueryAsync<ProductReportPagingSqlRow>(
            BuildProductReportPagingSql(hasCompare),
            parameters.ToArray()
        );
        // 先确定实际页，避免优化器把商品图片等宽字段连接提前到全量汇总之前。
        var pageCodes = rows.Where(row => row.HasData && !string.IsNullOrWhiteSpace(row.ProductCode))
            .Select(row => row.ProductCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (pageCodes.Count > 0)
        {
            var products = await _context.Db.Queryable<Product>()
                .Where(product => product.ProductCode != null && pageCodes.Contains(product.ProductCode))
                .Select(product => new ProductInfo
                {
                    ProductCode = product.ProductCode ?? string.Empty,
                    ItemNumber = product.ItemNumber,
                    ProductImage = product.ProductImage,
                }).ToListAsync();
            var byCode = products.ToDictionary(product => product.ProductCode, StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.Where(row => row.HasData))
                if (row.ProductCode != null && byCode.TryGetValue(row.ProductCode, out var product))
                {
                    row.ItemNumber = product.ItemNumber;
                    row.ProductImage = product.ProductImage;
                }
        }
        return MapProductReportPagingRows(rows, pageIndex, pageSize);
    }

    internal static string BuildProductReportPagingSql(bool includeCompare)
    {
        // 排名只聚合销售额；页码确定后再读取本页的名称、数量和成本覆盖，避免对全部商品做宽行聚合。
        return $"""
            BEGIN TRY
            -- 筛选集合只解析一次；显式标记空筛选，避免逐商品重复执行 OPENJSON/NOT EXISTS。
            SELECT DISTINCT CONVERT(nvarchar(100), [value]) AS [BranchCode]
            INTO #ProductReportBranchFilter FROM OPENJSON(@Branches);
            CREATE CLUSTERED INDEX [IX_ProductReportBranchFilter] ON #ProductReportBranchFilter ([BranchCode]);
            SELECT DISTINCT CONVERT(nvarchar(100), [value]) AS [SupplierCode]
            INTO #ProductReportLocalFilter FROM OPENJSON(@LocalSuppliers);
            CREATE CLUSTERED INDEX [IX_ProductReportLocalFilter] ON #ProductReportLocalFilter ([SupplierCode]);
            SELECT DISTINCT CONVERT(nvarchar(100), [value]) AS [SupplierCode]
            INTO #ProductReportChinaFilter FROM OPENJSON(@ChinaSuppliers);
            CREATE CLUSTERED INDEX [IX_ProductReportChinaFilter] ON #ProductReportChinaFilter ([SupplierCode]);
            SELECT [ProductCode], [ChinaSupplierCode]
            INTO #ProductReportChinaMap
            FROM OPENJSON(@ChinaProductMap)
            WITH
            (
                [ProductCode] nvarchar(100) '$.ProductCode',
                [ChinaSupplierCode] nvarchar(100) '$.ChinaSupplierCode'
            );
            CREATE CLUSTERED INDEX [IX_ProductReportChinaMap_Product] ON #ProductReportChinaMap ([ProductCode]);

            SELECT DISTINCT product.[ProductCode]
            INTO #ProductReportSearchProducts
            FROM [dbo].[Product] AS product
            WHERE @ProductSearch IS NOT NULL
                AND (product.[ItemNumber] LIKE N'%' + @ProductSearch + N'%' ESCAPE N'\'
                    OR product.[Barcode] LIKE N'%' + @ProductSearch + N'%' ESCAPE N'\');
            CREATE CLUSTERED INDEX [IX_ProductReportSearchProducts] ON #ProductReportSearchProducts ([ProductCode]);

            {BuildProductReportSourceCtes(includeCompare, pageOnly: false)}
            SELECT [ProductCode],
                SUM(CASE WHEN [Period] = 0 THEN [TotalAmount] ELSE 0 END) AS [CurrentSalesAmount],
                SUM(CASE WHEN [Period] = 1 THEN [TotalAmount] ELSE 0 END) AS [CompareSalesAmount]
            INTO #ProductReportAggregates
            FROM PeriodRows
            WHERE [ProductCode] IS NOT NULL AND LTRIM(RTRIM([ProductCode])) <> N''
            GROUP BY [ProductCode]
            OPTION (RECOMPILE);

            ;WITH Windowed AS
            (
                SELECT a.*,
                    CONVERT(int, COUNT(*) OVER ()) AS [TotalCount],
                    ROW_NUMBER() OVER
                    (
                        ORDER BY a.[CurrentSalesAmount] DESC, a.[CompareSalesAmount] DESC, a.[ProductCode] ASC
                    ) AS [RowNumber]
                FROM #ProductReportAggregates AS a
            )
            SELECT CAST(CASE WHEN windowed.[RowNumber] > @PageOffset THEN 1 ELSE 0 END AS bit) AS [HasData],
                windowed.[TotalCount], windowed.[RowNumber], windowed.[ProductCode]
            INTO #ProductReportPage
            FROM Windowed AS windowed
            WHERE
                (
                    windowed.[RowNumber] > @PageOffset
                    AND windowed.[RowNumber] <= @PageOffset + @PageSize
                )
                OR
                (
                    windowed.[TotalCount] <= @PageOffset
                    AND windowed.[RowNumber] = 1
                );
            CREATE CLUSTERED INDEX [IX_ProductReportPage_Product] ON #ProductReportPage ([ProductCode]);

            {BuildProductReportSourceCtes(includeCompare, pageOnly: true)},
            Aggregated AS
            (
                SELECT [ProductCode],
                    MAX(CASE WHEN [Period] = 0 THEN [ProductName] END) AS [CurrentProductName],
                    MAX(CASE WHEN [Period] = 1 THEN [ProductName] END) AS [CompareProductName],
                    SUM(CASE WHEN [Period] = 0 THEN [TotalQuantity] ELSE 0 END) AS [CurrentQuantity],
                    SUM(CASE WHEN [Period] = 0 THEN [TotalAmount] ELSE 0 END) AS [CurrentSalesAmount],
                    SUM(CASE WHEN [Period] = 0 THEN [OrderCount] ELSE 0 END) AS [CurrentOrderCount],
                    SUM(CASE WHEN [Period] = 0 THEN [GrossProfit] END) AS [CurrentGrossProfit],
                    SUM(CASE WHEN [Period] = 0 THEN 1 ELSE 0 END) AS [CurrentStatisticRowCount],
                    SUM(CASE WHEN [Period] = 0 AND [TotalCost] IS NOT NULL THEN 1 ELSE 0 END) AS [CurrentCostedRowCount],
                    SUM(CASE WHEN [Period] = 0 AND [GrossProfit] IS NOT NULL THEN 1 ELSE 0 END) AS [CurrentGrossProfitRowCount],
                    SUM(CASE WHEN [Period] = 1 THEN [TotalQuantity] ELSE 0 END) AS [CompareQuantity],
                    SUM(CASE WHEN [Period] = 1 THEN [TotalAmount] ELSE 0 END) AS [CompareSalesAmount],
                    SUM(CASE WHEN [Period] = 1 THEN [OrderCount] ELSE 0 END) AS [CompareOrderCount],
                    SUM(CASE WHEN [Period] = 1 THEN [GrossProfit] END) AS [CompareGrossProfit],
                    SUM(CASE WHEN [Period] = 1 THEN 1 ELSE 0 END) AS [CompareStatisticRowCount],
                    SUM(CASE WHEN [Period] = 1 AND [TotalCost] IS NOT NULL THEN 1 ELSE 0 END) AS [CompareCostedRowCount],
                    SUM(CASE WHEN [Period] = 1 AND [GrossProfit] IS NOT NULL THEN 1 ELSE 0 END) AS [CompareGrossProfitRowCount]
                FROM PeriodRows
                GROUP BY [ProductCode]
            )
            SELECT page.[HasData], page.[TotalCount], page.[ProductCode],
                detail.[CurrentProductName], detail.[CompareProductName],
                detail.[CurrentQuantity], detail.[CurrentSalesAmount], detail.[CurrentOrderCount], detail.[CurrentGrossProfit],
                detail.[CurrentStatisticRowCount], detail.[CurrentCostedRowCount], detail.[CurrentGrossProfitRowCount],
                detail.[CompareQuantity], detail.[CompareSalesAmount], detail.[CompareOrderCount], detail.[CompareGrossProfit],
                detail.[CompareStatisticRowCount], detail.[CompareCostedRowCount], detail.[CompareGrossProfitRowCount]
            FROM #ProductReportPage AS page
            LEFT JOIN Aggregated AS detail ON detail.[ProductCode] = page.[ProductCode]
            ORDER BY page.[RowNumber] ASC;

            DROP TABLE #ProductReportSearchProducts;
            DROP TABLE #ProductReportPage;
            DROP TABLE #ProductReportAggregates;
            DROP TABLE #ProductReportChinaMap;
            DROP TABLE #ProductReportBranchFilter;
            DROP TABLE #ProductReportLocalFilter;
            DROP TABLE #ProductReportChinaFilter;
            END TRY
            BEGIN CATCH
                IF OBJECT_ID(N'tempdb..#ProductReportSearchProducts') IS NOT NULL
                    DROP TABLE #ProductReportSearchProducts;
                IF OBJECT_ID(N'tempdb..#ProductReportPage') IS NOT NULL
                    DROP TABLE #ProductReportPage;
                IF OBJECT_ID(N'tempdb..#ProductReportAggregates') IS NOT NULL
                    DROP TABLE #ProductReportAggregates;
                IF OBJECT_ID(N'tempdb..#ProductReportChinaMap') IS NOT NULL
                    DROP TABLE #ProductReportChinaMap;
                IF OBJECT_ID(N'tempdb..#ProductReportBranchFilter') IS NOT NULL
                    DROP TABLE #ProductReportBranchFilter;
                IF OBJECT_ID(N'tempdb..#ProductReportLocalFilter') IS NOT NULL
                    DROP TABLE #ProductReportLocalFilter;
                IF OBJECT_ID(N'tempdb..#ProductReportChinaFilter') IS NOT NULL
                    DROP TABLE #ProductReportChinaFilter;
                THROW;
            END CATCH;
            """;
    }

    private static string BuildProductReportSourceCtes(bool includeCompare, bool pageOnly)
    {
        // 排名和本页明细共用同一组来源条件，确保筛选、同期独有商品和中国供应商 200 映射口径一致。
        var sourceNames = new List<string>();
        var sourceCtes = new List<string>();
        foreach (var compare in includeCompare ? new[] { false, true } : new[] { false })
        {
            var period = compare ? "Compare" : "Current";
            var name = period + "Source";
            sourceNames.Add(name);
            sourceCtes.Add($"""
                {name} AS
                (
                    SELECT s.[ProductCode], s.[ProductName], s.[TotalQuantity], s.[TotalAmount],
                        s.[OrderCount], s.[TotalCost], s.[GrossProfit], CAST({(compare ? 1 : 0)} AS int) AS [Period]
                    FROM [dbo].[ProductStoreDailySalesStatistic] AS s
                    {(pageOnly ? "INNER JOIN #ProductReportPage AS requested ON requested.[ProductCode] = s.[ProductCode] AND requested.[HasData] = 1" : string.Empty)}
                    WHERE s.[Date] >= @{period}Start AND s.[Date] < @{period}EndExclusive
                    {BuildSourceFilters()}
                )
                """);
        }
        return """
            ;WITH SearchProducts AS
            (
                SELECT [ProductCode] FROM #ProductReportSearchProducts
            ),
            BranchFilter AS
            (
                SELECT [BranchCode] FROM #ProductReportBranchFilter
            ),
            LocalSupplierFilter AS
            (
                SELECT [SupplierCode] FROM #ProductReportLocalFilter
            ),
            ChinaSupplierFilter AS
            (
                SELECT [SupplierCode] FROM #ProductReportChinaFilter
            ),
            ChinaProductMap AS
            (
                SELECT [ProductCode], [ChinaSupplierCode] FROM #ProductReportChinaMap
            ),
            """ + string.Join(",\n", sourceCtes) + """
            , PeriodRows AS
            (
            """ + string.Join("\nUNION ALL ", sourceNames.Select(name => "SELECT * FROM " + name)) + "\n)";
    }

    private static string BuildSourceFilters()
    {
        const string chinaFilter = """
                  AND
                  (
                      @HasChinaFilter = 0
                      OR EXISTS
                      (
                          SELECT 1 FROM ChinaSupplierFilter AS supplier
                          WHERE supplier.[SupplierCode] = s.[SupplierCode]
                      )
                      OR
                      (
                          s.[SupplierCode] = N'200'
                          AND EXISTS
                          (
                              SELECT 1
                              FROM ChinaProductMap AS mapping
                              WHERE mapping.[ProductCode] = s.[ProductCode]

                          )
                      )
                  )
                """;

        return """
            AND
            (
                @HasBranchFilter = 0
                OR EXISTS
                (
                    SELECT 1 FROM BranchFilter AS branch
                    WHERE branch.[BranchCode] = s.[BranchCode]
                )
            )
            AND
            (
                @HasLocalFilter = 0
                OR EXISTS
                (
                    SELECT 1 FROM LocalSupplierFilter AS supplier
                    WHERE supplier.[SupplierCode] = s.[SupplierCode]
                )
            )
            """ + chinaFilter + """
            AND
            (
                @ProductSearch IS NULL
                OR EXISTS
                (
                    SELECT 1 FROM SearchProducts AS product
                    WHERE product.[ProductCode] = s.[ProductCode]
                )
                OR s.[Barcode] LIKE N'%' + @ProductSearch + N'%' ESCAPE N'\'
            )
        """;
    }

    internal static PagedSalesProductDetailWithDiscountDto MapProductReportPagingRows(
        IEnumerable<ProductReportPagingSqlRow> rows,
        int pageIndex,
        int pageSize
    )
    {
        var rowList = rows.ToList();
        return new PagedSalesProductDetailWithDiscountDto
        {
            Data = rowList
                .Where(row => row.HasData && !string.IsNullOrWhiteSpace(row.ProductCode))
                .Select(ToSalesProductDetailWithDiscount)
                .ToList(),
            Total = rowList.FirstOrDefault()?.TotalCount ?? 0,
            PageIndex = pageIndex,
            PageSize = pageSize,
        };
    }

    internal static SalesProductDetailWithDiscountDto ToSalesProductDetailWithDiscount(
        ProductReportPagingSqlRow row
    )
    {
        var result = new SalesProductDetailWithDiscountDto
        {
            ProductCode = row.ProductCode ?? string.Empty,
            ItemNumber = row.ItemNumber,
            ProductImage = row.ProductImage,
            ProductName = row.CurrentProductName ?? row.CompareProductName,
            Quantity = row.CurrentQuantity,
            DiscountedQuantity = 0,
            SalesAmount = row.CurrentSalesAmount,
            AverageUnitPrice = row.CurrentQuantity > 0 ? row.CurrentSalesAmount / row.CurrentQuantity : 0,
            AverageOriginalPrice = null,
            OrderCount = row.CurrentOrderCount,
            GrossProfit = GetCompleteGrossProfit(
                row.CurrentGrossProfit,
                row.CurrentStatisticRowCount,
                row.CurrentCostedRowCount,
                row.CurrentGrossProfitRowCount
            ),
            QuantityLY = row.CompareQuantity,
            DiscountedQuantityLY = 0,
            SalesAmountLY = row.CompareSalesAmount,
            AverageUnitPriceLY = row.CompareQuantity > 0 ? row.CompareSalesAmount / row.CompareQuantity : 0,
            AverageOriginalPriceLY = null,
            OrderCountLY = row.CompareOrderCount,
            GrossProfitLY = GetCompleteGrossProfit(
                row.CompareGrossProfit,
                row.CompareStatisticRowCount,
                row.CompareCostedRowCount,
                row.CompareGrossProfitRowCount
            ),
        };
        result.GrossMarginRate = CalculateGrossMarginRate(result.SalesAmount, result.GrossProfit);
        result.GrossMarginRateLY = CalculateGrossMarginRate(result.SalesAmountLY, result.GrossProfitLY);
        return result;
    }

    private static PagedSalesProductDetailWithDiscountDto CreateEmptyProductPagingResult(int pageIndex, int pageSize)
    {
        return new PagedSalesProductDetailWithDiscountDto
        {
            Data = new List<SalesProductDetailWithDiscountDto>(),
            Total = 0,
            PageIndex = pageIndex,
            PageSize = pageSize,
        };
    }

    internal static string EscapeLikeValue(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
    }
}
