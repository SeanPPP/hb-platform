using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.React;

public partial class SalesDashboardReactService
{
    // 投影表缺失（51014）后 5 分钟内不再尝试预聚合路径，避免每次请求都多一次往返。
    private static DateTime _monthlyProjectionMissingUntilUtc = DateTime.MinValue;

    /// <summary>
    /// 只有全部分店范围（管理员）且没有关键词、没有选中分店的请求走预聚合：
    /// 商品粒度月表/日表不含分店，无法应用授权分店或选中分店；单独请求分店栏的商品抽屉走原查询的商品索引已足够快。
    /// </summary>
    internal static bool UsesMonthlyProjection(
        string? search, IReadOnlyCollection<string>? branches, string? selectedBranch, IReadOnlySet<SalesDetailSection> wanted)
        => string.IsNullOrWhiteSpace(search)
           && branches == null
           && string.IsNullOrWhiteSpace(selectedBranch)
           && !(wanted.Count == 1 && wanted.Contains(SalesDetailSection.Branches));

    private static bool MonthlyProjectionRecentlyMissing() => DateTime.UtcNow < _monthlyProjectionMissingUntilUtc;

    private static void RememberMonthlyProjectionMissing() => _monthlyProjectionMissingUntilUtc = DateTime.UtcNow.AddMinutes(5);

    /// <summary>
    /// 预聚合路径：完整落在各期内且身份有效的月份读月表，其余日期按日身份读日表，日表也没覆盖的日期才读日事实；
    /// 七个结果集与原查询同形同序。商品粒度归属在这里用当前映射解析，分店粒度用月表/日表烘焙的归属，
    /// 映射签名变化的月份和日期分店行退回日事实。汇总、供应商与分母改从分店粒度事实求和（同一批基础行的另一种分组，和数一致），
    /// 只有商品栏和选中商品的筛选需要商品粒度事实。
    /// </summary>
    internal static string BuildSalesDetailReportSqlMonthly(
        string posmDatabase, DateRangeDto range, SalesDetailKind kind, string? selectedSupplier, string? selectedProduct,
        int pageIndex, int pageSize, IReadOnlySet<SalesDetailSection> wanted)
    {
        var hasCompare = HasCompare(range);
        var database = QuoteIdentifier(posmDatabase);
        var supplierFilter = string.IsNullOrWhiteSpace(selectedSupplier) ? string.Empty : " AND [SupplierCode] = @sdrSelectedSupplier";
        var productFilter = string.IsNullOrWhiteSpace(selectedProduct) ? string.Empty : " AND [ProductCode] = @sdrSelectedProduct";
        var hasSelectedProduct = !string.IsNullOrWhiteSpace(selectedProduct);
        var offset = ((long)pageIndex - 1L) * pageSize;
        const string periods = """
Periods AS
(
 SELECT 0 [Period], @sdrCurrentStart [StartDate], @sdrCurrentEnd [EndDate]
 UNION ALL
 SELECT 1 [Period], @sdrCompareStart [StartDate], @sdrCompareEnd [EndDate] WHERE @sdrHasCompare=1
)
""";
        const string measures = "[Revenue], [Quantity], [OrderCount], [GrossProfit], [StatisticRowCount], [CostedRowCount], [GrossProfitRowCount]";
        const string sumMeasures = "SUM([Revenue]) [Revenue], SUM([Quantity]) [Quantity], SUM([OrderCount]) [OrderCount], SUM([GrossProfit]) [GrossProfit], "
            + "SUM([StatisticRowCount]) [StatisticRowCount], SUM([CostedRowCount]) [CostedRowCount], SUM([GrossProfitRowCount]) [GrossProfitRowCount]";
        const string factMeasures = "SUM(s.[TotalAmount]) [Revenue], SUM(CONVERT(bigint, s.[TotalQuantity])) [Quantity], SUM(CONVERT(bigint, s.[OrderCount])) [OrderCount], "
            + "SUM(s.[GrossProfit]) [GrossProfit], COUNT_BIG(*) [StatisticRowCount], COUNT_BIG(s.[TotalCost]) [CostedRowCount], COUNT_BIG(s.[GrossProfit]) [GrossProfitRowCount]";
        const string trimmedKeys = "LTRIM(RTRIM(COALESCE([RawSupplierCode],''))), LTRIM(RTRIM(COALESCE([BranchCode],''))), LTRIM(RTRIM(COALESCE([ProductCode],'')))";
        string prefixed(string alias) => alias + "." + measures.Replace(", [", $", {alias}.[");
        string china(string alias) => $"CASE WHEN {alias}.[RawSupplierCode]='200' THEN NULLIF(LTRIM(RTRIM(m.[ChinaSupplierCode])), '') WHEN cs.[SupplierCode] IS NOT NULL THEN {alias}.[RawSupplierCode] END";
        string aus(string alias) => $"CASE WHEN {alias}.[RawSupplierCode]='200' OR cs.[SupplierCode] IS NOT NULL THEN '200' ELSE NULLIF({alias}.[RawSupplierCode], '') END";
        string mappingJoins(string alias) => $"""
LEFT JOIN {database}.[dbo].[posm_product_supplier_mapping] m ON m.[ProductCode] = {alias}.[ProductCode] AND m.[LocalSupplierCode] = '200' AND m.[IsDeleted] = 0
LEFT JOIN (SELECT [SupplierCode] FROM [ChinaSupplier] WHERE [SupplierCode] IS NOT NULL AND [SupplierCode]<>'' GROUP BY [SupplierCode]) cs ON cs.[SupplierCode]={alias}.[RawSupplierCode]
""";

        var guard = $"""
IF OBJECT_ID(N'dbo.SalesDetailQueryMonthlyProduct', N'U') IS NULL OR OBJECT_ID(N'dbo.SalesDetailQueryMonthlyBranch', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesDetailQueryMonthlyState', N'U') IS NULL OR OBJECT_ID(N'dbo.SalesDetailQueryDailyProduct', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesDetailQueryDailyBranch', N'U') IS NULL OR OBJECT_ID(N'dbo.SalesDetailQueryDailyState', N'U') IS NULL
 THROW {SalesDetailQueryMonthlyProjection.MissingSchemaErrorNumber}, N'销售明细预聚合投影尚未建立。', 1;
DECLARE @sdmMappingVersion varchar(64) = {SalesDetailQueryProjection.BuildMappingSignatureSql(posmDatabase)};
""";
        // 1. 完整落在各期内的月份：商品粒度只看日身份，分店粒度还要求映射签名未变。
        var months = $"""
WITH {periods}, Digits(n) AS (SELECT n FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) v(n)),
MonthOffsets(n) AS (SELECT a.n + b.n * 10 FROM Digits a CROSS JOIN Digits b),
Months AS
(
 SELECT p.[Period], p.[StartDate], p.[EndDate],
        DATEADD(month, o.n, DATEFROMPARTS(YEAR(p.[StartDate]), MONTH(p.[StartDate]), 1)) [Month]
 FROM Periods p CROSS JOIN MonthOffsets o WHERE o.n <= 26
)
SELECT m.[Period], m.[Month], DATEADD(month, 1, m.[Month]) [NextMonth],
       CAST(CASE WHEN st.[Month] IS NOT NULL AND st.[ProjectionSchemaVersion]={SalesDetailQueryMonthlyProjection.SchemaVersion}
                  AND st.[DayIdentity]=ident.[Identity]
                  AND NOT EXISTS (SELECT 1 FROM [dbo].[SalesStatisticRefreshState] failed
                                  WHERE failed.[StatisticType]=N'ProductStoreDaily'
                                    AND failed.[Status]=N'Failed'
                                    AND failed.[Date] >= CONVERT(datetime, m.[Month])
                                    AND failed.[Date] < CONVERT(datetime, DATEADD(month, 1, m.[Month]))) THEN 1 ELSE 0 END AS bit) [ProductValid],
       CAST(CASE WHEN st.[Month] IS NOT NULL AND st.[ProjectionSchemaVersion]={SalesDetailQueryMonthlyProjection.SchemaVersion}
                  AND st.[DayIdentity]=ident.[Identity] AND st.[MappingVersion]=@sdmMappingVersion
                  AND NOT EXISTS (SELECT 1 FROM [dbo].[SalesStatisticRefreshState] failed
                                  WHERE failed.[StatisticType]=N'ProductStoreDaily'
                                    AND failed.[Status]=N'Failed'
                                    AND failed.[Date] >= CONVERT(datetime, m.[Month])
                                    AND failed.[Date] < CONVERT(datetime, DATEADD(month, 1, m.[Month]))) THEN 1 ELSE 0 END AS bit) [BranchValid]
INTO #sdmMonths
FROM Months m
LEFT JOIN [dbo].[SalesDetailQueryMonthlyState] st ON st.[Month]=m.[Month]
CROSS APPLY (SELECT {SalesDetailQueryMonthlyProjection.BuildMonthIdentitySql("m.[Month]")} [Identity]) ident
WHERE m.[Month] >= m.[StartDate] AND DATEADD(month, 1, m.[Month]) <= m.[EndDate];
""";
        // 2. 未被有效月份覆盖的日期：来源 0=月表已含、1=日表、2=日事实（日表无状态、身份不同或分店粒度映射签名不同）。
        var days = $"""
WITH {periods}, Digits(n) AS (SELECT n FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) v(n)),
DayOffsets(n) AS (SELECT a.n + b.n * 10 + c.n * 100 FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c),
Days AS
(
 SELECT p.[Period], CONVERT(date, DATEADD(day, o.n, p.[StartDate])) [Day]
 FROM Periods p CROSS JOIN DayOffsets o WHERE o.n < DATEDIFF(day, p.[StartDate], p.[EndDate])
)
SELECT d.[Period], d.[Day], CONVERT(datetime, d.[Day]) [DayStart], CONVERT(datetime, DATEADD(day, 1, d.[Day])) [DayEnd],
       CAST(CASE WHEN pm.[Month] IS NOT NULL THEN 0 WHEN dv.[DayValid]=1 THEN 1 ELSE 2 END AS tinyint) [ProductSource],
       CAST(CASE WHEN bm.[Month] IS NOT NULL THEN 0 WHEN dv.[DayValid]=1 AND dv.[MappingValid]=1 THEN 1 ELSE 2 END AS tinyint) [BranchSource]
INTO #sdmDays
FROM Days d
LEFT JOIN #sdmMonths pm ON pm.[Period]=d.[Period] AND pm.[ProductValid]=1 AND d.[Day]>=pm.[Month] AND d.[Day]<pm.[NextMonth]
LEFT JOIN #sdmMonths bm ON bm.[Period]=d.[Period] AND bm.[BranchValid]=1 AND d.[Day]>=bm.[Month] AND d.[Day]<bm.[NextMonth]
CROSS APPLY (SELECT
 CASE WHEN EXISTS (SELECT 1 FROM [dbo].[SalesStatisticRefreshState] r
                   INNER JOIN [dbo].[SalesDetailQueryDailyState] ds ON ds.[Date]=d.[Day]
                   WHERE r.[StatisticType]=N'ProductStoreDaily' AND r.[Date]=CONVERT(datetime, d.[Day])
                     AND {SalesDetailQueryMonthlyProjection.BuildDayIdentityMatchesSql("ds", "r")}) THEN 1 ELSE 0 END [DayValid],
 CASE WHEN EXISTS (SELECT 1 FROM [dbo].[SalesDetailQueryDailyState] ds WHERE ds.[Date]=d.[Day] AND ds.[MappingVersion]=@sdmMappingVersion) THEN 1 ELSE 0 END [MappingValid]) dv
WHERE (pm.[Month] IS NULL OR bm.[Month] IS NULL)
  AND NOT EXISTS (SELECT 1 FROM [dbo].[SalesStatisticRefreshState] failed
                  WHERE failed.[StatisticType]=N'ProductStoreDaily'
                    AND failed.[Status]=N'Failed'
                    AND failed.[Date]=CONVERT(datetime, d.[Day]));
""";
        // 3. 日表没覆盖的日期读日事实：临时表里的日期逐日走聚集主键 seek（每天约 1.2 万行），正常只有当日或回填中的日期。
        var baseFacts = $"""
WITH RawFacts AS
(
 SELECT d.[Period], d.[ProductSource], d.[BranchSource], s.[SupplierCode] [RawSupplierCode], s.[BranchCode], s.[ProductCode], {factMeasures}
 FROM #sdmDays d
 INNER JOIN [ProductStoreDailySalesStatistic] s ON s.[Date]>=d.[DayStart] AND s.[Date]<d.[DayEnd]
 WHERE d.[ProductSource]=2 OR d.[BranchSource]=2
 GROUP BY d.[Period], d.[ProductSource], d.[BranchSource], s.[SupplierCode], s.[BranchCode], s.[ProductCode]
)
SELECT [Period], CAST(CASE WHEN [ProductSource]=2 THEN 1 ELSE 0 END AS bit) [IsProductEdge], CAST(CASE WHEN [BranchSource]=2 THEN 1 ELSE 0 END AS bit) [IsBranchEdge],
       LTRIM(RTRIM(COALESCE([RawSupplierCode],''))) [RawSupplierCode],
       LTRIM(RTRIM(COALESCE([BranchCode],''))) [BranchCode],
       LTRIM(RTRIM(COALESCE([ProductCode],''))) [ProductCode],
       {sumMeasures}
INTO #sdmBaseFacts
FROM RawFacts
GROUP BY [Period], [ProductSource], [BranchSource], {trimmedKeys};
""";
        // 4. 商品粒度事实：月表 + 日表 + 日事实，归属用当前映射解析（与原查询同样在聚合后的键上 LEFT JOIN）。
        var productFacts = $"""
WITH Combined AS
(
 SELECT mo.[Period], mp.[RawSupplierCode], mp.[ProductCode], {prefixed("mp")}
 FROM [dbo].[SalesDetailQueryMonthlyProduct] mp
 INNER JOIN #sdmMonths mo ON mo.[Month]=mp.[Month] AND mo.[ProductValid]=1
 UNION ALL
 SELECT d.[Period], dp.[RawSupplierCode], dp.[ProductCode], {prefixed("dp")}
 FROM [dbo].[SalesDetailQueryDailyProduct] dp
 INNER JOIN #sdmDays d ON d.[Day]=dp.[Date] AND d.[ProductSource]=1
 UNION ALL
 SELECT [Period], [RawSupplierCode], [ProductCode], {measures} FROM #sdmBaseFacts WHERE [IsProductEdge]=1
), Grouped AS
(
 SELECT [Period], [RawSupplierCode], [ProductCode], {sumMeasures} FROM Combined GROUP BY [Period], [RawSupplierCode], [ProductCode]
)
SELECT g.[Period], g.[RawSupplierCode], {china("g")} [ChinaSupplierCode], {aus("g")} [AustralianSupplierCode], g.[ProductCode],
       CASE WHEN @sdrKind=1 THEN {china("g")} ELSE {aus("g")} END [SupplierCode],
       {prefixed("g")}
INTO #sdmProductFacts
FROM Grouped g
{mappingJoins("g")}
OPTION (HASH GROUP);
""";
        // 5. 分店粒度事实：选中商品时分店栏只看该商品，直接按商品索引读日事实；否则月表 + 日表 + 日事实（当前映射解析）。
        var branchFacts = !hasSelectedProduct
            ? $"""
WITH BaseResolved AS
(
 SELECT e.[Period], e.[BranchCode], e.[RawSupplierCode], {china("e")} [ChinaSupplierCode], {aus("e")} [AustralianSupplierCode],
        e.[ProductCode], {prefixed("e")}
 FROM #sdmBaseFacts e
 {mappingJoins("e")}
 WHERE e.[IsBranchEdge]=1
), Combined AS
(
 SELECT mo.[Period], b.[BranchCode], b.[RawSupplierCode], b.[ChinaSupplierCode], b.[AustralianSupplierCode],
        b.[MinProductCode], b.[MaxProductCode], {prefixed("b")}
 FROM [dbo].[SalesDetailQueryMonthlyBranch] b
 INNER JOIN #sdmMonths mo ON mo.[Month]=b.[Month] AND mo.[BranchValid]=1
 UNION ALL
 SELECT d.[Period], db.[BranchCode], db.[RawSupplierCode], db.[ChinaSupplierCode], db.[AustralianSupplierCode],
        db.[MinProductCode], db.[MaxProductCode], {prefixed("db")}
 FROM [dbo].[SalesDetailQueryDailyBranch] db
 INNER JOIN #sdmDays d ON d.[Day]=db.[Date] AND d.[BranchSource]=1
 UNION ALL
 SELECT [Period], [BranchCode], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode], [ProductCode], [ProductCode], {measures}
 FROM BaseResolved
)
SELECT [Period], [BranchCode], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode],
       CASE WHEN @sdrKind=1 THEN [ChinaSupplierCode] ELSE [AustralianSupplierCode] END [SupplierCode],
       MIN([MinProductCode]) [MinProductCode], MAX([MaxProductCode]) [MaxProductCode], {sumMeasures}
INTO #sdmBranchFacts
FROM Combined
GROUP BY [Period], [BranchCode], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode] OPTION (HASH GROUP);
"""
            : $"""
WITH {periods}, RawFacts AS
(
 SELECT p.[Period], s.[SupplierCode] [RawSupplierCode], s.[BranchCode], s.[ProductCode], {factMeasures}
 FROM [ProductStoreDailySalesStatistic] s CROSS JOIN Periods p
 WHERE s.[Date]>=p.[StartDate] AND s.[Date]<p.[EndDate] AND s.[ProductCode]=@sdrSelectedProduct
   AND NOT EXISTS (SELECT 1 FROM [dbo].[SalesStatisticRefreshState] failed
                   WHERE failed.[StatisticType]=N'ProductStoreDaily'
                     AND failed.[Status]=N'Failed'
                     AND failed.[Date]=CONVERT(datetime, CONVERT(date, s.[Date])))
 GROUP BY p.[Period], s.[SupplierCode], s.[BranchCode], s.[ProductCode]
), Trimmed AS
(
 SELECT [Period], LTRIM(RTRIM(COALESCE([RawSupplierCode],''))) [RawSupplierCode], LTRIM(RTRIM(COALESCE([BranchCode],''))) [BranchCode],
        LTRIM(RTRIM(COALESCE([ProductCode],''))) [ProductCode], {sumMeasures}
 FROM RawFacts GROUP BY [Period], {trimmedKeys}
), Resolved AS
(
 SELECT e.[Period], e.[BranchCode], e.[RawSupplierCode], {china("e")} [ChinaSupplierCode], {aus("e")} [AustralianSupplierCode],
        e.[ProductCode], {prefixed("e")}
 FROM Trimmed e
 {mappingJoins("e")}
)
SELECT [Period], [BranchCode], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode],
       CASE WHEN @sdrKind=1 THEN [ChinaSupplierCode] ELSE [AustralianSupplierCode] END [SupplierCode],
       MIN([ProductCode]) [MinProductCode], MAX([ProductCode]) [MaxProductCode], {sumMeasures}
INTO #sdmBranchFacts
FROM Resolved
GROUP BY [Period], [BranchCode], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode];
""";

        // 6. 各栏位：列名与顺序和原查询完全一致（20 列）。
        string compare(string current, string zero) => hasCompare ? current : zero;
        string measureColumns(string prefix = "") => $"""
 COALESCE(SUM(CASE WHEN [Period]=0 THEN [Revenue] ELSE 0 END),0) [Revenue], {compare("COALESCE(SUM(CASE WHEN [Period]=1 THEN [Revenue] ELSE 0 END),0)", "0")} [CompareRevenue],
 COALESCE(SUM(CASE WHEN [Period]=0 THEN [Quantity] ELSE 0 END),0) [Quantity], {compare("COALESCE(SUM(CASE WHEN [Period]=1 THEN [Quantity] ELSE 0 END),0)", "0")} [CompareQuantity],
 COALESCE(SUM(CASE WHEN [Period]=0 THEN [OrderCount] ELSE 0 END),0) [OrderCount], {compare("COALESCE(SUM(CASE WHEN [Period]=1 THEN [OrderCount] ELSE 0 END),0)", "0")} [CompareOrderCount],
 SUM(CASE WHEN [Period]=0 THEN [GrossProfit] END) [GrossProfit], {compare("SUM(CASE WHEN [Period]=1 THEN [GrossProfit] END)", "CAST(NULL AS decimal(18,2))")} [CompareGrossProfit],
 COALESCE(SUM(CASE WHEN [Period]=0 THEN [StatisticRowCount] ELSE 0 END),0) [StatisticRowCount], COALESCE(SUM(CASE WHEN [Period]=0 THEN [CostedRowCount] ELSE 0 END),0) [CostedRowCount], COALESCE(SUM(CASE WHEN [Period]=0 THEN [GrossProfitRowCount] ELSE 0 END),0) [GrossProfitRowCount],
 {compare("COALESCE(SUM(CASE WHEN [Period]=1 THEN [StatisticRowCount] ELSE 0 END),0)", "0")} [CompareStatisticRowCount], {compare("COALESCE(SUM(CASE WHEN [Period]=1 THEN [CostedRowCount] ELSE 0 END),0)", "0")} [CompareCostedRowCount], {compare("COALESCE(SUM(CASE WHEN [Period]=1 THEN [GrossProfitRowCount] ELSE 0 END),0)", "0")} [CompareGrossProfitRowCount],
""";
        string productMultiplicity(string period, string min, string max)
            => $"CASE WHEN MIN(CASE WHEN [Period]={period} THEN [{min}] END) IS NULL THEN 0 WHEN MIN(CASE WHEN [Period]={period} THEN [{min}] END) = MAX(CASE WHEN [Period]={period} THEN [{max}] END) THEN 1 ELSE 2 END";
        string sectionSelect(string source, string whereClause, string group, string code, string name, string order, string minColumn, string maxColumn) => $"""
SELECT {code} [Code], {name} [Name], CAST(NULL AS nvarchar(50)) [ItemNumber], CAST(NULL AS nvarchar(200)) [ProductImage],
{measureColumns()}
 {productMultiplicity("0", minColumn, maxColumn)} [CurrentProductCount], {compare(productMultiplicity("1", minColumn, maxColumn), "0")} [CompareProductCount]
FROM {source} f {whereClause}
{(string.IsNullOrWhiteSpace(group) ? string.Empty : $"GROUP BY {group}")} {order};
""";
        var supplierName = $"CASE WHEN @sdrKind=1 THEN COALESCE(NULLIF(LTRIM(RTRIM((SELECT MAX(cName.[SupplierName]) FROM [ChinaSupplier] cName WHERE cName.[SupplierCode]=f.[SupplierCode]))), ''), f.[SupplierCode]) ELSE COALESCE(NULLIF(LTRIM(RTRIM((SELECT MAX(lName.[Name]) FROM [LocalSupplier] lName WHERE lName.[LocalSupplierCode]=f.[SupplierCode] AND lName.[IsDeleted]=0))), ''), CASE WHEN f.[SupplierCode]='{CHINA_LOCAL_SUPPLIER_CODE}' THEN '{CHINA_LOCAL_SUPPLIER_FALLBACK_NAME}' ELSE f.[SupplierCode] END) END";
        var branchName = "COALESCE(NULLIF(LTRIM(RTRIM((SELECT MAX(sName.[StoreName]) FROM [Store] sName WHERE sName.[StoreCode]=f.[BranchCode]))), ''), f.[BranchCode])";
        // 没有选中商品时，汇总、供应商、分母从分店粒度事实（约 1 万行）求和；选中商品时这些栏位要按商品过滤，仍用商品粒度事实。
        var totalsSource = hasSelectedProduct ? "#sdmProductFacts" : "#sdmBranchFacts";
        var totalsMin = hasSelectedProduct ? "ProductCode" : "MinProductCode";
        var totalsMax = hasSelectedProduct ? "ProductCode" : "MaxProductCode";
        var summary = sectionSelect(totalsSource, $"WHERE [SupplierCode] IS NOT NULL{supplierFilter}{productFilter}", string.Empty, "'summary'", "'当前筛选汇总'", string.Empty, totalsMin, totalsMax);
        var suppliers = sectionSelect(totalsSource, $"WHERE [SupplierCode] IS NOT NULL{productFilter}", "f.[SupplierCode]", "f.[SupplierCode]", supplierName, "ORDER BY [Revenue] DESC, [CompareRevenue] DESC, [Code] ASC", totalsMin, totalsMax);
        var branches = sectionSelect("#sdmBranchFacts", $"WHERE [SupplierCode] IS NOT NULL{supplierFilter}", "f.[BranchCode]", "f.[BranchCode]", branchName, "ORDER BY [Revenue] DESC, [CompareRevenue] DESC, [Code] ASC", "MinProductCode", "MaxProductCode");
        var productAggregate = $"""
SELECT [ProductCode],
       SUM(CASE WHEN [Period]=0 THEN [Revenue] ELSE 0 END) [Revenue], {compare("SUM(CASE WHEN [Period]=1 THEN [Revenue] ELSE 0 END)", "0")} [CompareRevenue],
       SUM(CASE WHEN [Period]=0 THEN [Quantity] ELSE 0 END) [Quantity], {compare("SUM(CASE WHEN [Period]=1 THEN [Quantity] ELSE 0 END)", "0")} [CompareQuantity],
       SUM(CASE WHEN [Period]=0 THEN [OrderCount] ELSE 0 END) [OrderCount], {compare("SUM(CASE WHEN [Period]=1 THEN [OrderCount] ELSE 0 END)", "0")} [CompareOrderCount],
       SUM(CASE WHEN [Period]=0 THEN [GrossProfit] END) [GrossProfit], {compare("SUM(CASE WHEN [Period]=1 THEN [GrossProfit] END)", "CAST(NULL AS decimal(18,2))")} [CompareGrossProfit],
       SUM(CASE WHEN [Period]=0 THEN [StatisticRowCount] ELSE 0 END) [StatisticRowCount], SUM(CASE WHEN [Period]=0 THEN [CostedRowCount] ELSE 0 END) [CostedRowCount], SUM(CASE WHEN [Period]=0 THEN [GrossProfitRowCount] ELSE 0 END) [GrossProfitRowCount],
       {compare("SUM(CASE WHEN [Period]=1 THEN [StatisticRowCount] ELSE 0 END)", "0")} [CompareStatisticRowCount], {compare("SUM(CASE WHEN [Period]=1 THEN [CostedRowCount] ELSE 0 END)", "0")} [CompareCostedRowCount], {compare("SUM(CASE WHEN [Period]=1 THEN [GrossProfitRowCount] ELSE 0 END)", "0")} [CompareGrossProfitRowCount],
       CASE WHEN MAX(CASE WHEN [Period]=0 THEN 1 ELSE 0 END)=1 THEN 1 ELSE 0 END [CurrentProductCount],
       {compare("CASE WHEN MAX(CASE WHEN [Period]=1 THEN 1 ELSE 0 END)=1 THEN 1 ELSE 0 END", "0")} [CompareProductCount]
FROM #sdmProductFacts WHERE [SupplierCode] IS NOT NULL{supplierFilter}
GROUP BY [ProductCode]
""";
        var products = $"""
SELECT a.[ProductCode] [Code], COALESCE(NULLIF(LTRIM(RTRIM(p.[ProductName])), ''),NULLIF(LTRIM(RTRIM(stat.[StatisticProductName])), ''),a.[ProductCode]) [Name], p.[ItemNumber], p.[ProductImage],
       a.[Revenue], a.[CompareRevenue], a.[Quantity], a.[CompareQuantity], a.[OrderCount], a.[CompareOrderCount],
       a.[GrossProfit], a.[CompareGrossProfit], a.[StatisticRowCount], a.[CostedRowCount], a.[GrossProfitRowCount],
       a.[CompareStatisticRowCount], a.[CompareCostedRowCount], a.[CompareGrossProfitRowCount], a.[CurrentProductCount], a.[CompareProductCount]
FROM ({productAggregate} ORDER BY [Quantity] DESC, [CompareQuantity] DESC, [ProductCode] ASC OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY) a
OUTER APPLY (SELECT TOP (1) p0.[ProductName], p0.[EnglishName], p0.[ItemNumber], p0.[ProductImage], p0.[Barcode]
             FROM [Product] p0 WHERE p0.[ProductCode]=a.[ProductCode] ORDER BY p0.[UUID]) p
OUTER APPLY (SELECT TOP (1) s0.[ProductName] [StatisticProductName]
             FROM [ProductStoreDailySalesStatistic] s0
             WHERE NULLIF(LTRIM(RTRIM(p.[ProductName])), '') IS NULL
               AND s0.[ProductCode]=a.[ProductCode]
               AND ((s0.[Date]>=@sdrCurrentStart AND s0.[Date]<@sdrCurrentEnd) OR (@sdrHasCompare=1 AND s0.[Date]>=@sdrCompareStart AND s0.[Date]<@sdrCompareEnd))
             ORDER BY s0.[Date] DESC) stat
ORDER BY a.[Quantity] DESC, a.[CompareQuantity] DESC, a.[ProductCode] ASC OPTION (HASH GROUP);
""";
        var productCount = $"SELECT COUNT(*) FROM (SELECT [ProductCode] FROM #sdmProductFacts WHERE [SupplierCode] IS NOT NULL{supplierFilter} GROUP BY [ProductCode]) x;";
        var denominator = wanted.Contains(SalesDetailSection.Suppliers)
            ? $"SELECT COALESCE(SUM(CASE WHEN [Period]=0 AND [AustralianSupplierCode] IS NOT NULL THEN [Revenue] ELSE 0 END),0), COALESCE(SUM(CASE WHEN [Period]=0 AND [ChinaSupplierCode] IS NOT NULL THEN [Revenue] ELSE 0 END),0), COALESCE(SUM(CASE WHEN [Period]=1 AND [AustralianSupplierCode] IS NOT NULL THEN [Revenue] ELSE 0 END),0), COALESCE(SUM(CASE WHEN [Period]=1 AND [ChinaSupplierCode] IS NOT NULL THEN [Revenue] ELSE 0 END),0) FROM {totalsSource} f WHERE [AustralianSupplierCode] IS NOT NULL;"
            : "SELECT 0,0,0,0;";
        const string emptyRows = "SELECT TOP 0 CAST(NULL AS nvarchar(50)) [Code], CAST(NULL AS nvarchar(200)) [Name], CAST(NULL AS nvarchar(50)) [ItemNumber], CAST(NULL AS nvarchar(200)) [ProductImage], CAST(0 AS decimal(18,2)) [Revenue], CAST(0 AS decimal(18,2)) [CompareRevenue], CAST(0 AS int) [Quantity], CAST(0 AS int) [CompareQuantity], CAST(0 AS int) [OrderCount], CAST(0 AS int) [CompareOrderCount], CAST(NULL AS decimal(18,2)) [GrossProfit], CAST(NULL AS decimal(18,2)) [CompareGrossProfit], CAST(0 AS int) [StatisticRowCount], CAST(0 AS int) [CostedRowCount], CAST(0 AS int) [GrossProfitRowCount], CAST(0 AS int) [CompareStatisticRowCount], CAST(0 AS int) [CompareCostedRowCount], CAST(0 AS int) [CompareGrossProfitRowCount], CAST(0 AS int) [CurrentProductCount], CAST(0 AS int) [CompareProductCount];";
        if (!wanted.Contains(SalesDetailSection.Summary)) summary = emptyRows;
        if (!wanted.Contains(SalesDetailSection.Suppliers)) suppliers = emptyRows;
        if (!wanted.Contains(SalesDetailSection.Branches)) branches = emptyRows;
        if (!wanted.Contains(SalesDetailSection.Products)) { products = emptyRows; productCount = "SELECT 0;"; }
        var status = "SELECT [StatisticType],[Date],[Status],[LastAggregatedAtUtc],[CompletedAtUtc],[SourceProductVersion] FROM [SalesStatisticRefreshState] WHERE [StatisticType]='ProductStoreDaily' AND (([Date]>=@sdrCurrentStart AND [Date]<@sdrCurrentEnd) OR (@sdrHasCompare=1 AND [Date]>=@sdrCompareStart AND [Date]<@sdrCompareEnd)) ORDER BY [Date],[StatisticType];";
        return guard + months + days + baseFacts + productFacts + branchFacts + status + summary + suppliers + branches + products + productCount + denominator
            + "DROP TABLE #sdmMonths;DROP TABLE #sdmDays;DROP TABLE #sdmBaseFacts;DROP TABLE #sdmProductFacts;DROP TABLE #sdmBranchFacts;";
    }
}
