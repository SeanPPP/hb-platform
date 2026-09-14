using BlazorApp.Api.Data;
using BlazorApp.Api.Data.SchemaMigrations;
using Microsoft.Data.SqlClient;
using SqlSugar;

namespace BlazorApp.Api.Services;

/// <summary>销售明细查询日投影；只在 SQL Server 同实例直连且显式 schema 已就绪时维护。</summary>
internal static class SalesDetailQueryProjection
{
    internal const int SchemaVersion = 1;
    internal const string CreateSchemaSql = SalesDetailQueryProjectionSchema.ApplySql;

    /// <summary>构造当前映射全集的稳定 SHA256 表达式，供查询端按需核验。</summary>
    internal static string BuildMappingSignatureSql(string posmDatabase)
    {
        var database = QuoteIdentifier(posmDatabase);
        return $$"""
CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max),
    ISNULL((
        SELECT N'M' [Kind], m.[ProductCode], m.[ChinaSupplierCode]
        FROM {{database}}.[dbo].[posm_product_supplier_mapping] m
        WHERE m.[LocalSupplierCode] = '200' AND m.[IsDeleted] = 0
        ORDER BY m.[ProductCode] COLLATE Latin1_General_100_BIN2,
                 m.[ChinaSupplierCode] COLLATE Latin1_General_100_BIN2
        FOR JSON PATH, INCLUDE_NULL_VALUES
    ), N'[]') + N'|' +
    ISNULL((
        SELECT N'C' [Kind], c.[SupplierCode]
        FROM (SELECT DISTINCT [SupplierCode] FROM [dbo].[ChinaSupplier]
              WHERE [SupplierCode] IS NOT NULL AND [SupplierCode] <> '') c
        ORDER BY c.[SupplierCode] COLLATE Latin1_General_100_BIN2
        FOR JSON PATH, INCLUDE_NULL_VALUES
    ), N'[]'))), 2)
""";
    }

    /// <summary>在调用方已开启的主库事务中，替换一天的查询投影并绑定最终发布状态。</summary>
    internal static string BuildRefreshDaySql(string posmDatabase)
    {
        var database = QuoteIdentifier(posmDatabase);
        return $$"""
SET NOCOUNT ON;
SET XACT_ABORT ON;

DROP TABLE IF EXISTS #SalesDetailProjectionRows;
DROP TABLE IF EXISTS #SalesDetailProjectionChinaSupplier;
DROP TABLE IF EXISTS #SalesDetailProjectionMapping;

DECLARE @sdpDay date = CONVERT(date, @sdpDate);
DECLARE @sdpStatus nvarchar(20), @sdpProductVersion nvarchar(128),
        @sdpLastAggregatedAtUtc datetime2, @sdpCompletedAtUtc datetime2,
        @sdpJobId uniqueidentifier, @sdpMappingVersion varchar(64), @sdpMappingHasFanout bit = 0;

SELECT @sdpStatus = [Status], @sdpProductVersion = [SourceProductVersion],
       @sdpLastAggregatedAtUtc = [LastAggregatedAtUtc], @sdpCompletedAtUtc = [CompletedAtUtc],
       @sdpJobId = [JobId]
FROM [dbo].[SalesStatisticRefreshState] WITH (UPDLOCK, HOLDLOCK)
WHERE [StatisticType] = N'ProductStoreDaily' AND [Date] >= @sdpDay AND [Date] < DATEADD(day, 1, @sdpDay);

-- 状态未发布或版本为空时撤销旧覆盖，查询端会自动回退到原事实流程。
IF @sdpStatus NOT IN (N'Fresh', N'ProvisionalFresh')
   OR NULLIF(LTRIM(RTRIM(@sdpProductVersion)), N'') IS NULL
   OR @sdpLastAggregatedAtUtc IS NULL
BEGIN
    DELETE FROM [dbo].[SalesDetailQueryDaily] WHERE [Date] = @sdpDay;
    DELETE FROM [dbo].[SalesDetailQueryMappingUse] WHERE [Date] = @sdpDay;
    DELETE FROM [dbo].[SalesDetailQueryProjectionState] WHERE [Date] = @sdpDay;
    RETURN;
END;

SELECT m.[ProductCode], m.[ChinaSupplierCode]
INTO #SalesDetailProjectionMapping
FROM {{database}}.[dbo].[posm_product_supplier_mapping] m
WHERE m.[LocalSupplierCode] = '200' AND m.[IsDeleted] = 0;

SELECT DISTINCT c.[SupplierCode]
INTO #SalesDetailProjectionChinaSupplier
FROM [dbo].[ChinaSupplier] c
WHERE c.[SupplierCode] IS NOT NULL AND c.[SupplierCode] <> '';

IF EXISTS (
    SELECT 1 FROM #SalesDetailProjectionMapping
    WHERE [ProductCode] IS NOT NULL
    GROUP BY [ProductCode]
    HAVING COUNT_BIG(*) > 1
)
    SET @sdpMappingHasFanout = 1;

-- 签名与投影共同消费这两张临时表，映射写入并发不能产生错签覆盖。
SELECT @sdpMappingVersion = CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max),
    ISNULL((
        SELECT N'M' [Kind], m.[ProductCode], m.[ChinaSupplierCode]
        FROM #SalesDetailProjectionMapping m
        ORDER BY m.[ProductCode] COLLATE Latin1_General_100_BIN2,
                 m.[ChinaSupplierCode] COLLATE Latin1_General_100_BIN2
        FOR JSON PATH, INCLUDE_NULL_VALUES
    ), N'[]') + N'|' +
    ISNULL((
        SELECT N'C' [Kind], c.[SupplierCode]
        FROM #SalesDetailProjectionChinaSupplier c
        ORDER BY c.[SupplierCode] COLLATE Latin1_General_100_BIN2
        FOR JSON PATH, INCLUDE_NULL_VALUES
    ), N'[]'))), 2);

SELECT @sdpDay [Date],
       LTRIM(RTRIM(COALESCE(s.[SupplierCode], N''))) [RawSupplierCode],
       CASE WHEN LTRIM(RTRIM(COALESCE(s.[SupplierCode], N''))) = N'200'
              THEN NULLIF(LTRIM(RTRIM(m.[ChinaSupplierCode])), N'')
            WHEN cs.[SupplierCode] IS NOT NULL THEN LTRIM(RTRIM(s.[SupplierCode])) END [ChinaSupplierCode],
       CASE WHEN LTRIM(RTRIM(COALESCE(s.[SupplierCode], N''))) = N'200' OR cs.[SupplierCode] IS NOT NULL
              THEN N'200' ELSE NULLIF(LTRIM(RTRIM(s.[SupplierCode])), N'') END [AustralianSupplierCode],
       COALESCE(s.[BranchCode], N'') [SourceBranchCode],
       LTRIM(RTRIM(COALESCE(s.[BranchCode], N''))) [BranchCode],
       LTRIM(RTRIM(COALESCE(s.[ProductCode], N''))) [ProductCode],
       s.[TotalAmount], s.[TotalQuantity], s.[OrderCount], s.[GrossProfit], s.[TotalCost]
INTO #SalesDetailProjectionRows
FROM [dbo].[ProductStoreDailySalesStatistic] s
LEFT JOIN #SalesDetailProjectionMapping m
  ON m.[ProductCode] = LTRIM(RTRIM(s.[ProductCode]))
LEFT JOIN #SalesDetailProjectionChinaSupplier cs
  ON cs.[SupplierCode] = LTRIM(RTRIM(s.[SupplierCode]))
WHERE s.[Date] >= @sdpDay AND s.[Date] < DATEADD(day, 1, @sdpDay);

DELETE FROM [dbo].[SalesDetailQueryDaily] WHERE [Date] = @sdpDay;
INSERT INTO [dbo].[SalesDetailQueryDaily]
    ([Date], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode], [SourceBranchCode], [BranchCode],
     [MinProductCode], [MaxProductCode], [Revenue], [Quantity], [OrderCount], [GrossProfit],
     [StatisticRowCount], [CostedRowCount], [GrossProfitRowCount])
SELECT [Date], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode], [SourceBranchCode], [BranchCode],
       MIN([ProductCode]), MAX([ProductCode]), SUM([TotalAmount]), SUM(CONVERT(bigint, [TotalQuantity])),
       SUM(CONVERT(bigint, [OrderCount])), SUM([GrossProfit]), COUNT_BIG([ProductCode]),
       COUNT_BIG([TotalCost]), COUNT_BIG([GrossProfit])
FROM #SalesDetailProjectionRows
GROUP BY [Date], [RawSupplierCode], [ChinaSupplierCode], [AustralianSupplierCode], [SourceBranchCode], [BranchCode];

DELETE FROM [dbo].[SalesDetailQueryMappingUse] WHERE [Date] = @sdpDay;
-- 只有映射全集无重复键时，才记录当天实际使用的本地供应商商品映射证明；
-- ProductCode 和 ChinaSupplierCode 均直接复用本次日投影的物化结果，避免再次读取 POSM 后错签。
IF @sdpMappingHasFanout = 0
BEGIN
    INSERT INTO [dbo].[SalesDetailQueryMappingUse] ([Date], [ProductCode], [ChinaSupplierCode])
    SELECT DISTINCT [Date], [ProductCode], [ChinaSupplierCode]
    FROM #SalesDetailProjectionRows
    WHERE [RawSupplierCode] = N'200';
END;

-- 全局短锁只串行化追加去重；唯一聚集索引负责点查，不扫描整张历史词典。
DECLARE @sdpAliasLockResult int;
EXEC @sdpAliasLockResult = sys.sp_getapplock
    @Resource = N'HBWeb:SalesDetailQueryProductAlias', @LockMode = N'Exclusive',
    @LockOwner = N'Transaction', @LockTimeout = 30000;
IF @sdpAliasLockResult < 0 THROW 51820, N'Cannot acquire sales detail alias projection lock.', 1;

INSERT INTO [dbo].[SalesDetailQueryProductAlias] ([ProductCode], [ProductName], [Barcode])
SELECT aliases.[ProductCode], aliases.[ProductName], aliases.[Barcode]
FROM (
    SELECT DISTINCT s.[ProductCode], COALESCE(s.[ProductName], N'') [ProductName], COALESCE(s.[Barcode], N'') [Barcode]
    FROM [dbo].[ProductStoreDailySalesStatistic] s
    WHERE s.[Date] >= @sdpDay AND s.[Date] < DATEADD(day, 1, @sdpDay)
) aliases
WHERE NOT EXISTS (
    SELECT 1 FROM [dbo].[SalesDetailQueryProductAlias] existing WITH (UPDLOCK, HOLDLOCK)
    WHERE existing.[ProductCode] = aliases.[ProductCode]
      AND existing.[ProductName] = aliases.[ProductName]
      AND existing.[Barcode] = aliases.[Barcode]
);

DELETE FROM [dbo].[SalesDetailQueryProjectionState] WHERE [Date] = @sdpDay;
INSERT INTO [dbo].[SalesDetailQueryProjectionState]
    ([Date], [ProjectionSchemaVersion], [SourceProductVersion], [SourceLastAggregatedAtUtc],
     [SourceCompletedAtUtc], [SourceJobId], [MappingVersion], [MappingHasFanout])
VALUES (@sdpDay, {{SchemaVersion}}, @sdpProductVersion, @sdpLastAggregatedAtUtc,
        @sdpCompletedAtUtc, @sdpJobId, @sdpMappingVersion, @sdpMappingHasFanout);

DROP TABLE #SalesDetailProjectionRows;
DROP TABLE #SalesDetailProjectionChinaSupplier;
DROP TABLE #SalesDetailProjectionMapping;
""";
    }

    internal static async Task RefreshDayIfSupportedAsync(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        DateTime date)
    {
        if (context.Db.CurrentConnectionConfig.DbType != DbType.SqlServer
            || posmContext.Db.CurrentConnectionConfig.DbType != DbType.SqlServer
            || context.Db.Ado.Transaction is null
            || !TryGetSameServerPosmDatabase(context, posmContext, out var posmDatabase))
            return;

        var quotedDatabase = QuoteIdentifier(posmDatabase);
        var readySql = $$"""
SELECT CASE WHEN OBJECT_ID(N'dbo.SalesDetailQueryDaily', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.SalesDetailQueryProductAlias', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.SalesDetailQueryMappingUse', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.SalesDetailQueryProjectionState', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.ProductStoreDailySalesStatistic', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.SalesStatisticRefreshState', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.ChinaSupplier', N'U') IS NOT NULL
 AND OBJECT_ID(N'{{quotedDatabase}}.[dbo].[posm_product_supplier_mapping]', N'U') IS NOT NULL
 THEN 1 ELSE 0 END;
""";
        var ready = await context.Db.Ado.GetIntAsync(readySql);
        if (ready != 1)
            return;

        await context.Db.Ado.ExecuteCommandAsync(
            BuildRefreshDaySql(posmDatabase),
            new SugarParameter("@sdpDate", date.Date));
    }

    private static bool TryGetSameServerPosmDatabase(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        out string database)
    {
        database = string.Empty;
        try
        {
            var main = new SqlConnectionStringBuilder(context.Db.CurrentConnectionConfig.ConnectionString);
            var posm = new SqlConnectionStringBuilder(posmContext.Db.CurrentConnectionConfig.ConnectionString);
            if (!string.Equals(main.DataSource, posm.DataSource, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(posm.InitialCatalog))
                return false;
            database = posm.InitialCatalog;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.IndexOf('\0') >= 0)
            throw new ArgumentException("数据库名称不能为空。", nameof(identifier));
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
