using BlazorApp.Api.Data.SchemaMigrations;

namespace BlazorApp.Api.Services;

/// <summary>
/// 销售明细预聚合投影（按日 + 按月）的 SQL 构造器。
/// 生产实测（2026-09-22）：默认路径按（供应商、分店、商品）聚合 746 万行日事实本身就要 7–9 秒，
/// 列存批处理扫描虽快，但行组跨 100–300 天，首尾不满月的"边缘日"仍要解压百万行并落 40 万行临时表（2–4 秒）。
/// 所以改为两级投影：日表（商品粒度约 8.5 千行/日，分店粒度约 2 千行/日）由 worker 逐日从日事实生成，
/// 月表由日表汇总；查询端整月读月表、边缘日读日表，只有日表还没追上的日期才读日事实。
/// 日身份 = 该日商品日统计状态行的（报表来源身份、聚合时间）；月身份 = 该月全部状态行（日身份）的哈希。
/// </summary>
internal static class SalesDetailQueryMonthlyProjection
{
    internal const int SchemaVersion = 2;
    internal const string CreateSchemaSql = SalesDetailQueryMonthlySchema.ApplySql;
    /// <summary>投影表缺失时查询批次抛出的错误号，调用方据此回退原查询。</summary>
    internal const int MissingSchemaErrorNumber = 51014;
    /// <summary>整月汇总时该月日表尚未按当前身份或映射覆盖，worker 据此跳过等下一轮。</summary>
    internal const int DaysNotReadyErrorNumber = 51015;

    internal const string TablesExistSql = """
SELECT CASE WHEN OBJECT_ID(N'dbo.SalesDetailQueryDailyProduct', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.SalesDetailQueryDailyBranch', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.SalesDetailQueryDailyState', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.SalesDetailQueryMonthlyProduct', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.SalesDetailQueryMonthlyBranch', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.SalesDetailQueryMonthlyState', N'U') IS NOT NULL THEN 1 ELSE 0 END;
""";

    /// <summary>
    /// 某个月的发布身份：该月全部 ProductStoreDaily 状态行按日期排序的（日期、报表来源身份、聚合时间）JSON 的 SHA-256。
    /// 排队/运行中的日期在 SNAPSHOT 事务里读到的仍是上一版完整事实，有来源版本时继续沿用上一版身份；
    /// 无版本的排队/运行状态使用带 Status 的不可读标记，避免沿用旧 Failed 投影。
    /// 对账 Failed 但已聚合的日期用任务与最后检查时间组成身份，重复失败重算也会使旧投影失效。
    /// </summary>
    internal static string BuildMonthIdentitySql(string monthExpression)
    {
        var readable = BuildReadableSourceSql(
            "r.[Status]", "r.[SourceProductVersion]", "r.[LastAggregatedAtUtc]", "r.[LastCheckedAtUtc]");
        var sourceIdentity = BuildSourceIdentitySql("r");
        return $$"""
CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max), ISNULL((
    SELECT CONVERT(char(8), r.[Date], 112) [d],
           CASE WHEN {{readable}} THEN {{sourceIdentity}}
                ELSE CONCAT(N'Invalid:', COALESCE(r.[Status], N'none'), N':',
                            CONVERT(nvarchar(33), r.[LastCheckedAtUtc], 126)) END [v],
           CONVERT(varchar(27), r.[LastAggregatedAtUtc], 126) [t]
    FROM [dbo].[SalesStatisticRefreshState] r
    WHERE r.[StatisticType] = N'ProductStoreDaily'
      AND r.[Date] >= CONVERT(datetime, {{monthExpression}})
      AND r.[Date] < CONVERT(datetime, DATEADD(month, 1, {{monthExpression}}))
    ORDER BY r.[Date]
    FOR JSON PATH, INCLUDE_NULL_VALUES
), N'[]'))), 2)
""";
    }

    /// <summary>当前状态可供报表读取，且日表状态行与当前来源身份、聚合时间和 schema 版本一致。</summary>
    internal static string BuildDayIdentityMatchesSql(string dailyStateAlias, string refreshStateAlias)
        => $"{BuildReadableSourceSql(refreshStateAlias)}"
           + $" AND {dailyStateAlias}.[ProjectionSchemaVersion] = {SchemaVersion}"
           + $" AND NOT EXISTS (SELECT {dailyStateAlias}.[SourceProductVersion], {dailyStateAlias}.[SourceLastAggregatedAtUtc]"
           + $" EXCEPT SELECT {BuildSourceIdentitySql(refreshStateAlias)}, {refreshStateAlias}.[LastAggregatedAtUtc])";

    private static string BuildSourceIdentitySql(string refreshStateAlias)
        => SalesDetailQueryProjection.BuildSourceIdentitySql(
            $"{refreshStateAlias}.[Status]", $"{refreshStateAlias}.[SourceProductVersion]",
            $"{refreshStateAlias}.[JobId]", $"{refreshStateAlias}.[LastCheckedAtUtc]");

    /// <summary>
    /// Fresh/ProvisionalFresh 及有上一版身份的排队状态可读；Failed 必须已有聚合事实和本次检查时间。
    /// Queued/Running 若来源版本为空则拒绝，避免旧 writer 已替换事实但未刷新投影后错误复用旧 Failed 投影。
    /// </summary>
    internal static string BuildReadableSourceSql(string refreshStateAlias)
        => BuildReadableSourceSql(
            $"{refreshStateAlias}.[Status]", $"{refreshStateAlias}.[SourceProductVersion]",
            $"{refreshStateAlias}.[LastAggregatedAtUtc]", $"{refreshStateAlias}.[LastCheckedAtUtc]");

    private static string BuildReadableSourceSql(
        string status, string productVersion, string lastAggregatedAtUtc, string lastCheckedAtUtc)
        => $"({lastAggregatedAtUtc} IS NOT NULL AND (({status} IN (N'Fresh', N'ProvisionalFresh', N'Queued', N'Running')"
           + $" AND NULLIF(LTRIM(RTRIM({productVersion})), N'') IS NOT NULL)"
           + $" OR ({status} = N'Failed' AND {lastCheckedAtUtc} IS NOT NULL)))";

    /// <summary>LEFT JOIN 日状态后判断该日需要重算：无状态、身份不同或映射签名不同。</summary>
    private static string BuildDayStaleSql(string dailyStateAlias, string refreshStateAlias)
        => $"({dailyStateAlias}.[Date] IS NULL OR {dailyStateAlias}.[MappingVersion] <> @sdmMappingVersion"
           + $" OR NOT ({BuildDayIdentityMatchesSql(dailyStateAlias, refreshStateAlias)}))";

    /// <summary>该月内存在需要重算的日期（日表尚未按当前身份与映射覆盖）。</summary>
    private static string BuildMonthDaysStaleSql(string monthExpression) => $"""
EXISTS (SELECT 1 FROM [dbo].[SalesStatisticRefreshState] r
        LEFT JOIN [dbo].[SalesDetailQueryDailyState] ds ON ds.[Date] = CONVERT(date, r.[Date])
        WHERE r.[StatisticType] = N'ProductStoreDaily'
          AND r.[Date] >= CONVERT(datetime, {monthExpression})
          AND r.[Date] < CONVERT(datetime, DATEADD(month, 1, {monthExpression}))
          AND {BuildDayStaleSql("ds", "r")})
""";

    /// <summary>
    /// 列出需要重算的日期（参数 @sdmMaxDays）：有商品日统计状态行但日表无状态、身份或映射签名不同。
    /// 最近的日期排前面，因为当日每小时重算、报表也最常查近期。
    /// </summary>
    internal static string BuildStaleDaysSql(string posmDatabase) => $"""
SET NOCOUNT ON;
DECLARE @sdmMappingVersion varchar(64) = {SalesDetailQueryProjection.BuildMappingSignatureSql(posmDatabase)};
SELECT TOP (@sdmMaxDays) CONVERT(date, r.[Date]) [Day]
FROM [dbo].[SalesStatisticRefreshState] r
LEFT JOIN [dbo].[SalesDetailQueryDailyState] ds ON ds.[Date] = CONVERT(date, r.[Date])
WHERE r.[StatisticType] = N'ProductStoreDaily' AND {BuildDayStaleSql("ds", "r")}
  AND {BuildReadableSourceSql("r")}
ORDER BY r.[Date] DESC;
""";

    /// <summary>
    /// 在调用方开启的 SNAPSHOT 事务里重算一天（参数 @sdmDay）：按聚集主键读当日日事实（约 1.2 万行），
    /// 先按原始键聚合再去空格，写商品粒度日表、分店粒度日表（归属用当前映射解析）与日状态。
    /// 没有状态行的日期不维护，查询端会直接读该日的日事实。
    /// </summary>
    internal static string BuildRefreshDaySql(string posmDatabase)
    {
        var database = QuoteIdentifier(posmDatabase);
        return $$"""
SET NOCOUNT ON;
SET XACT_ABORT ON;

DROP TABLE IF EXISTS #sdmMapping;
DROP TABLE IF EXISTS #sdmChinaSupplier;
DROP TABLE IF EXISTS #sdmDayFacts;

DECLARE @sdmDayStart datetime = CONVERT(datetime, @sdmDay), @sdmDayEnd datetime = CONVERT(datetime, DATEADD(day, 1, @sdmDay));
DECLARE @sdmStartedAt datetime2 = SYSUTCDATETIME();
DECLARE @sdmHasState bit = 0, @sdmStatus nvarchar(20), @sdmVersion nvarchar(128),
        @sdmProductVersion nvarchar(128), @sdmAggregatedAt datetime2,
        @sdmLastCheckedAtUtc datetime2, @sdmJobId uniqueidentifier;
SELECT TOP (1) @sdmHasState = 1, @sdmStatus = r.[Status], @sdmProductVersion = r.[SourceProductVersion],
       @sdmAggregatedAt = r.[LastAggregatedAtUtc], @sdmLastCheckedAtUtc = r.[LastCheckedAtUtc], @sdmJobId = r.[JobId]
FROM [dbo].[SalesStatisticRefreshState] r
WHERE r.[StatisticType] = N'ProductStoreDaily' AND r.[Date] = @sdmDayStart;
IF @sdmHasState = 0 RETURN;
IF NOT ({{BuildReadableSourceSql("@sdmStatus", "@sdmProductVersion", "@sdmAggregatedAt", "@sdmLastCheckedAtUtc")}}) RETURN;
SET @sdmVersion = {{SalesDetailQueryProjection.BuildSourceIdentitySql("@sdmStatus", "@sdmProductVersion", "@sdmJobId", "@sdmLastCheckedAtUtc")}};

SELECT m.[ProductCode], m.[ChinaSupplierCode]
INTO #sdmMapping
FROM {{database}}.[dbo].[posm_product_supplier_mapping] m
WHERE m.[LocalSupplierCode] = '200' AND m.[IsDeleted] = 0;

SELECT DISTINCT c.[SupplierCode]
INTO #sdmChinaSupplier
FROM [dbo].[ChinaSupplier] c
WHERE c.[SupplierCode] IS NOT NULL AND c.[SupplierCode] <> '';

-- 签名与分店归属共同消费这两张临时表，与日投影相同的口径。
DECLARE @sdmMappingVersion varchar(64) = CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max),
    ISNULL((
        SELECT N'M' [Kind], m.[ProductCode], m.[ChinaSupplierCode]
        FROM #sdmMapping m
        ORDER BY m.[ProductCode] COLLATE Latin1_General_100_BIN2,
                 m.[ChinaSupplierCode] COLLATE Latin1_General_100_BIN2
        FOR JSON PATH, INCLUDE_NULL_VALUES
    ), N'[]') + N'|' +
    ISNULL((
        SELECT N'C' [Kind], c.[SupplierCode]
        FROM #sdmChinaSupplier c
        ORDER BY c.[SupplierCode] COLLATE Latin1_General_100_BIN2
        FOR JSON PATH, INCLUDE_NULL_VALUES
    ), N'[]'))), 2);

-- 内层只按事实表原始列分组，去空格放到聚合后的窄行上，与原查询的键口径一致。
WITH RawFacts AS
(
    SELECT s.[SupplierCode] [RawSupplierCode], s.[BranchCode], s.[ProductCode],
           SUM(s.[TotalAmount]) [Revenue], SUM(CONVERT(bigint, s.[TotalQuantity])) [Quantity],
           SUM(CONVERT(bigint, s.[OrderCount])) [OrderCount], SUM(s.[GrossProfit]) [GrossProfit],
           COUNT_BIG(*) [StatisticRowCount], COUNT_BIG(s.[TotalCost]) [CostedRowCount], COUNT_BIG(s.[GrossProfit]) [GrossProfitRowCount]
    FROM [dbo].[ProductStoreDailySalesStatistic] s
    WHERE s.[Date] >= @sdmDayStart AND s.[Date] < @sdmDayEnd
    GROUP BY s.[SupplierCode], s.[BranchCode], s.[ProductCode]
)
SELECT LTRIM(RTRIM(COALESCE([RawSupplierCode], N''))) [RawSupplierCode],
       LTRIM(RTRIM(COALESCE([BranchCode], N''))) [BranchCode],
       LTRIM(RTRIM(COALESCE([ProductCode], N''))) [ProductCode],
       SUM([Revenue]) [Revenue], SUM([Quantity]) [Quantity], SUM([OrderCount]) [OrderCount], SUM([GrossProfit]) [GrossProfit],
       SUM([StatisticRowCount]) [StatisticRowCount], SUM([CostedRowCount]) [CostedRowCount], SUM([GrossProfitRowCount]) [GrossProfitRowCount]
INTO #sdmDayFacts
FROM RawFacts
GROUP BY LTRIM(RTRIM(COALESCE([RawSupplierCode], N''))), LTRIM(RTRIM(COALESCE([BranchCode], N''))), LTRIM(RTRIM(COALESCE([ProductCode], N'')));

DELETE FROM [dbo].[SalesDetailQueryDailyProduct] WHERE [Date] = @sdmDay;
INSERT INTO [dbo].[SalesDetailQueryDailyProduct]
    ([Date], [RawSupplierCode], [ProductCode], [Revenue], [Quantity], [OrderCount], [GrossProfit],
     [StatisticRowCount], [CostedRowCount], [GrossProfitRowCount])
SELECT @sdmDay, [RawSupplierCode], [ProductCode],
       SUM([Revenue]), SUM([Quantity]), SUM([OrderCount]), SUM([GrossProfit]),
       SUM([StatisticRowCount]), SUM([CostedRowCount]), SUM([GrossProfitRowCount])
FROM #sdmDayFacts
GROUP BY [RawSupplierCode], [ProductCode];

-- 分店粒度的归属与原查询同样在聚合后的键上 LEFT JOIN 映射，重复映射的放大口径保持一致。
DELETE FROM [dbo].[SalesDetailQueryDailyBranch] WHERE [Date] = @sdmDay;
INSERT INTO [dbo].[SalesDetailQueryDailyBranch]
    ([Date], [BranchCode], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode],
     [MinProductCode], [MaxProductCode], [Revenue], [Quantity], [OrderCount], [GrossProfit],
     [StatisticRowCount], [CostedRowCount], [GrossProfitRowCount])
SELECT @sdmDay, r.[BranchCode], r.[RawSupplierCode], r.[ChinaSupplierCode], r.[AustralianSupplierCode],
       MIN(r.[ProductCode]), MAX(r.[ProductCode]),
       SUM(r.[Revenue]), SUM(r.[Quantity]), SUM(r.[OrderCount]), SUM(r.[GrossProfit]),
       SUM(r.[StatisticRowCount]), SUM(r.[CostedRowCount]), SUM(r.[GrossProfitRowCount])
FROM (
    SELECT f.*,
           CASE WHEN f.[RawSupplierCode] = N'200' THEN NULLIF(LTRIM(RTRIM(m.[ChinaSupplierCode])), N'')
                WHEN cs.[SupplierCode] IS NOT NULL THEN f.[RawSupplierCode] END [ChinaSupplierCode],
           CASE WHEN f.[RawSupplierCode] = N'200' OR cs.[SupplierCode] IS NOT NULL THEN N'200'
                ELSE NULLIF(f.[RawSupplierCode], N'') END [AustralianSupplierCode]
    FROM #sdmDayFacts f
    LEFT JOIN #sdmMapping m ON m.[ProductCode] = f.[ProductCode]
    LEFT JOIN #sdmChinaSupplier cs ON cs.[SupplierCode] = f.[RawSupplierCode]
) r
GROUP BY r.[BranchCode], r.[RawSupplierCode], r.[ChinaSupplierCode], r.[AustralianSupplierCode];

DELETE FROM [dbo].[SalesDetailQueryDailyState] WHERE [Date] = @sdmDay;
INSERT INTO [dbo].[SalesDetailQueryDailyState]
    ([Date], [ProjectionSchemaVersion], [SourceProductVersion], [SourceLastAggregatedAtUtc], [MappingVersion], [RefreshedAtUtc], [RefreshDurationMs])
VALUES (@sdmDay, {{SchemaVersion}}, @sdmVersion, @sdmAggregatedAt, @sdmMappingVersion,
        SYSUTCDATETIME(), DATEDIFF(millisecond, @sdmStartedAt, SYSUTCDATETIME()));

DROP TABLE #sdmDayFacts;
DROP TABLE #sdmChinaSupplier;
DROP TABLE #sdmMapping;
""";
    }

    /// <summary>
    /// 列出需要重算且日表已就绪的月份：无状态、schema 版本不同、日身份或映射签名变化。
    /// 范围从最早的商品日统计状态所在月到当前月；最近的月份排前面，因为报表最常查它们。
    /// </summary>
    internal static string BuildStaleMonthsSql(string posmDatabase) => $$"""
SET NOCOUNT ON;
DECLARE @sdmMappingVersion varchar(64) = {{SalesDetailQueryProjection.BuildMappingSignatureSql(posmDatabase)}};
DECLARE @sdmFirstMonth date = (SELECT DATEFROMPARTS(YEAR(MIN([Date])), MONTH(MIN([Date])), 1)
                               FROM [dbo].[SalesStatisticRefreshState] WHERE [StatisticType] = N'ProductStoreDaily');
DECLARE @sdmLastMonth date = DATEFROMPARTS(YEAR(SYSDATETIME()), MONTH(SYSDATETIME()), 1);
IF @sdmFirstMonth IS NULL RETURN;
WITH Digits(n) AS (SELECT n FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) v(n)),
MonthOffsets(n) AS (SELECT a.n + b.n * 10 FROM Digits a CROSS JOIN Digits b),
Months AS (SELECT DATEADD(month, n, @sdmFirstMonth) [Month] FROM MonthOffsets WHERE DATEADD(month, n, @sdmFirstMonth) <= @sdmLastMonth)
SELECT m.[Month]
FROM Months m
LEFT JOIN [dbo].[SalesDetailQueryMonthlyState] st ON st.[Month] = m.[Month]
CROSS APPLY (SELECT {{BuildMonthIdentitySql("m.[Month]")}} [Identity]) ident
WHERE (st.[Month] IS NULL
   OR st.[ProjectionSchemaVersion] <> {{SchemaVersion}}
   OR st.[DayIdentity] <> ident.[Identity]
   OR st.[MappingVersion] <> @sdmMappingVersion)
  -- 日表还没按当前身份与映射覆盖的月份先不列出，等日表追上后再汇总。
  AND NOT {{BuildMonthDaysStaleSql("m.[Month]")}}
ORDER BY m.[Month] DESC;
""";

    /// <summary>
    /// 在调用方开启的 SNAPSHOT 事务里由日表汇总整月（参数 @sdmMonth）：
    /// 同一快照内先核对该月每天的日状态都与当前身份、映射签名一致，否则抛 51015；
    /// 这样月身份（由状态行计算）与写入的月表内容必然对应。
    /// </summary>
    internal static string BuildRefreshMonthSql(string posmDatabase) => $$"""
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @sdmMonthStart date = DATEFROMPARTS(YEAR(@sdmMonth), MONTH(@sdmMonth), 1);
DECLARE @sdmMonthEnd date = DATEADD(month, 1, @sdmMonthStart);
DECLARE @sdmStartedAt datetime2 = SYSUTCDATETIME();
DECLARE @sdmMappingVersion varchar(64) = {{SalesDetailQueryProjection.BuildMappingSignatureSql(posmDatabase)}};
IF {{BuildMonthDaysStaleSql("@sdmMonthStart")}}
    THROW {{DaysNotReadyErrorNumber}}, N'销售明细按日投影尚未覆盖该月。', 1;
DECLARE @sdmDayIdentity varchar(64) = {{BuildMonthIdentitySql("@sdmMonthStart")}};
DECLARE @sdmDayCount int = (SELECT COUNT(*) FROM [dbo].[SalesStatisticRefreshState]
                            WHERE [StatisticType] = N'ProductStoreDaily'
                              AND [Date] >= CONVERT(datetime, @sdmMonthStart) AND [Date] < CONVERT(datetime, @sdmMonthEnd));

DELETE FROM [dbo].[SalesDetailQueryMonthlyProduct] WHERE [Month] = @sdmMonthStart;
INSERT INTO [dbo].[SalesDetailQueryMonthlyProduct]
    ([Month], [RawSupplierCode], [ProductCode], [Revenue], [Quantity], [OrderCount], [GrossProfit],
     [StatisticRowCount], [CostedRowCount], [GrossProfitRowCount])
SELECT @sdmMonthStart, [RawSupplierCode], [ProductCode],
       SUM([Revenue]), SUM([Quantity]), SUM([OrderCount]), SUM([GrossProfit]),
       SUM([StatisticRowCount]), SUM([CostedRowCount]), SUM([GrossProfitRowCount])
FROM [dbo].[SalesDetailQueryDailyProduct]
WHERE [Date] >= @sdmMonthStart AND [Date] < @sdmMonthEnd
GROUP BY [RawSupplierCode], [ProductCode];

DELETE FROM [dbo].[SalesDetailQueryMonthlyBranch] WHERE [Month] = @sdmMonthStart;
INSERT INTO [dbo].[SalesDetailQueryMonthlyBranch]
    ([Month], [BranchCode], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode],
     [MinProductCode], [MaxProductCode], [Revenue], [Quantity], [OrderCount], [GrossProfit],
     [StatisticRowCount], [CostedRowCount], [GrossProfitRowCount])
SELECT @sdmMonthStart, [BranchCode], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode],
       MIN([MinProductCode]), MAX([MaxProductCode]),
       SUM([Revenue]), SUM([Quantity]), SUM([OrderCount]), SUM([GrossProfit]),
       SUM([StatisticRowCount]), SUM([CostedRowCount]), SUM([GrossProfitRowCount])
FROM [dbo].[SalesDetailQueryDailyBranch]
WHERE [Date] >= @sdmMonthStart AND [Date] < @sdmMonthEnd
GROUP BY [BranchCode], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode];

DELETE FROM [dbo].[SalesDetailQueryMonthlyState] WHERE [Month] = @sdmMonthStart;
INSERT INTO [dbo].[SalesDetailQueryMonthlyState]
    ([Month], [ProjectionSchemaVersion], [DayIdentity], [DayCount], [MappingVersion], [RefreshedAtUtc], [RefreshDurationMs])
VALUES (@sdmMonthStart, {{SchemaVersion}}, @sdmDayIdentity, @sdmDayCount, @sdmMappingVersion,
        SYSUTCDATETIME(), DATEDIFF(millisecond, @sdmStartedAt, SYSUTCDATETIME()));
""";

    internal static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.IndexOf('\0') >= 0)
            throw new ArgumentException("数据库名称不能为空。", nameof(identifier));
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
