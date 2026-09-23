namespace BlazorApp.Api.Data.SchemaMigrations;

/// <summary>
/// 销售明细预聚合投影：按日与按月两级，各有商品粒度（去分店，不烘焙供应商归属）和分店粒度（烘焙归属）。
/// 由 SalesDetailMonthlyProjectionWorker 从日事实生成日表、再由日表汇总月表；查询端按月身份、日身份逐级判定有效性。
/// </summary>
internal static class SalesDetailQueryMonthlySchema
{
    internal const string ApplySql = """
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;
BEGIN TRY
    -- 2026-09-22 未发布的中间形态：月映射使用记录表与月状态里的 MappingHasFanout 已弃用；状态表由 worker 重建。
    DROP TABLE IF EXISTS [dbo].[SalesDetailQueryMonthlyMappingUse];
    IF COL_LENGTH(N'dbo.SalesDetailQueryMonthlyState', N'MappingHasFanout') IS NOT NULL
        DROP TABLE [dbo].[SalesDetailQueryMonthlyState];

    IF OBJECT_ID(N'dbo.SalesDetailQueryDailyProduct', N'U') IS NULL
    BEGIN
        -- 键是去空格后的原始供应商码与商品码；中国/澳洲归属在查询时用当前映射解析，映射变化不使本表失效。
        CREATE TABLE [dbo].[SalesDetailQueryDailyProduct]
        (
            [Date] date NOT NULL,
            [RawSupplierCode] nvarchar(50) NOT NULL,
            [ProductCode] nvarchar(50) NOT NULL,
            [Revenue] decimal(38,4) NOT NULL,
            [Quantity] bigint NOT NULL,
            [OrderCount] bigint NOT NULL,
            [GrossProfit] decimal(38,4) NULL,
            [StatisticRowCount] bigint NOT NULL,
            [CostedRowCount] bigint NOT NULL,
            [GrossProfitRowCount] bigint NOT NULL,
            CONSTRAINT [PK_SalesDetailQueryDailyProduct] PRIMARY KEY CLUSTERED ([Date], [RawSupplierCode], [ProductCode])
        );
    END;

    IF OBJECT_ID(N'dbo.SalesDetailQueryDailyBranch', N'U') IS NULL
    BEGIN
        -- 分店粒度没有商品维度，归属只能在生成时按当时映射烘焙；映射签名变化的日期由 worker 重算。
        CREATE TABLE [dbo].[SalesDetailQueryDailyBranch]
        (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Date] date NOT NULL,
            [BranchCode] nvarchar(50) NOT NULL,
            [RawSupplierCode] nvarchar(50) NOT NULL,
            [ChinaSupplierCode] nvarchar(50) NULL,
            [AustralianSupplierCode] nvarchar(50) NULL,
            [MinProductCode] nvarchar(50) NOT NULL,
            [MaxProductCode] nvarchar(50) NOT NULL,
            [Revenue] decimal(38,4) NOT NULL,
            [Quantity] bigint NOT NULL,
            [OrderCount] bigint NOT NULL,
            [GrossProfit] decimal(38,4) NULL,
            [StatisticRowCount] bigint NOT NULL,
            [CostedRowCount] bigint NOT NULL,
            [GrossProfitRowCount] bigint NOT NULL,
            CONSTRAINT [PK_SalesDetailQueryDailyBranch] PRIMARY KEY NONCLUSTERED ([Id])
        );
        CREATE CLUSTERED INDEX [CX_SalesDetailQueryDailyBranch_Date]
            ON [dbo].[SalesDetailQueryDailyBranch] ([Date], [BranchCode], [RawSupplierCode]);
    END;

    IF OBJECT_ID(N'dbo.SalesDetailQueryDailyState', N'U') IS NULL
    BEGIN
        -- 日身份 = 商品日统计状态行的（来源版本、聚合时间）；datetime2 能无损保存 datetime 或 datetime2 来源值以便精确比较。
        CREATE TABLE [dbo].[SalesDetailQueryDailyState]
        (
            [Date] date NOT NULL,
            [ProjectionSchemaVersion] int NOT NULL,
            [SourceProductVersion] nvarchar(128) NULL,
            [SourceLastAggregatedAtUtc] datetime2 NULL,
            [MappingVersion] varchar(64) NOT NULL,
            [RefreshedAtUtc] datetime2 NOT NULL,
            [RefreshDurationMs] int NOT NULL,
            CONSTRAINT [PK_SalesDetailQueryDailyState] PRIMARY KEY CLUSTERED ([Date])
        );
    END;

    IF OBJECT_ID(N'dbo.SalesDetailQueryMonthlyProduct', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[SalesDetailQueryMonthlyProduct]
        (
            [Month] date NOT NULL,
            [RawSupplierCode] nvarchar(50) NOT NULL,
            [ProductCode] nvarchar(50) NOT NULL,
            [Revenue] decimal(38,4) NOT NULL,
            [Quantity] bigint NOT NULL,
            [OrderCount] bigint NOT NULL,
            [GrossProfit] decimal(38,4) NULL,
            [StatisticRowCount] bigint NOT NULL,
            [CostedRowCount] bigint NOT NULL,
            [GrossProfitRowCount] bigint NOT NULL,
            CONSTRAINT [PK_SalesDetailQueryMonthlyProduct] PRIMARY KEY CLUSTERED ([Month], [RawSupplierCode], [ProductCode])
        );
    END;

    IF OBJECT_ID(N'dbo.SalesDetailQueryMonthlyBranch', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[SalesDetailQueryMonthlyBranch]
        (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Month] date NOT NULL,
            [BranchCode] nvarchar(50) NOT NULL,
            [RawSupplierCode] nvarchar(50) NOT NULL,
            [ChinaSupplierCode] nvarchar(50) NULL,
            [AustralianSupplierCode] nvarchar(50) NULL,
            [MinProductCode] nvarchar(50) NOT NULL,
            [MaxProductCode] nvarchar(50) NOT NULL,
            [Revenue] decimal(38,4) NOT NULL,
            [Quantity] bigint NOT NULL,
            [OrderCount] bigint NOT NULL,
            [GrossProfit] decimal(38,4) NULL,
            [StatisticRowCount] bigint NOT NULL,
            [CostedRowCount] bigint NOT NULL,
            [GrossProfitRowCount] bigint NOT NULL,
            CONSTRAINT [PK_SalesDetailQueryMonthlyBranch] PRIMARY KEY NONCLUSTERED ([Id])
        );
        CREATE CLUSTERED INDEX [CX_SalesDetailQueryMonthlyBranch_Month]
            ON [dbo].[SalesDetailQueryMonthlyBranch] ([Month], [BranchCode], [RawSupplierCode]);
    END;

    IF OBJECT_ID(N'dbo.SalesDetailQueryMonthlyState', N'U') IS NULL
    BEGIN
        -- 月身份 = 该月全部日状态行（日期、来源版本、聚合时间）的哈希；分店粒度还要求映射签名未变。
        CREATE TABLE [dbo].[SalesDetailQueryMonthlyState]
        (
            [Month] date NOT NULL,
            [ProjectionSchemaVersion] int NOT NULL,
            [DayIdentity] varchar(64) NOT NULL,
            [DayCount] int NOT NULL,
            [MappingVersion] varchar(64) NOT NULL,
            [RefreshedAtUtc] datetime2 NOT NULL,
            [RefreshDurationMs] int NOT NULL,
            CONSTRAINT [PK_SalesDetailQueryMonthlyState] PRIMARY KEY CLUSTERED ([Month])
        );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";

    // 运行时只读门禁锁定列、类型和关键索引；不兼容结构只能通过显式迁移修复。
    internal const string VerifySql = """
IF OBJECT_ID(N'dbo.SalesDetailQueryDailyProduct', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesDetailQueryDailyBranch', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesDetailQueryDailyState', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesDetailQueryMonthlyProduct', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesDetailQueryMonthlyBranch', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesDetailQueryMonthlyState', N'U') IS NULL
    THROW 51810, N'Sales detail monthly projection tables are missing.', 1;

IF EXISTS (
    SELECT 1 FROM (VALUES
      (N'SalesDetailQueryDailyProduct', N'Date', N'date', 0, 0),
      (N'SalesDetailQueryDailyProduct', N'RawSupplierCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryDailyProduct', N'ProductCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryDailyProduct', N'Revenue', N'decimal', 0, 0),
      (N'SalesDetailQueryDailyProduct', N'Quantity', N'bigint', 0, 0),
      (N'SalesDetailQueryDailyBranch', N'Date', N'date', 0, 0),
      (N'SalesDetailQueryDailyBranch', N'BranchCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryDailyBranch', N'RawSupplierCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryDailyBranch', N'ChinaSupplierCode', N'nvarchar', 100, 1),
      (N'SalesDetailQueryDailyBranch', N'AustralianSupplierCode', N'nvarchar', 100, 1),
      (N'SalesDetailQueryDailyBranch', N'MinProductCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryDailyBranch', N'MaxProductCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryDailyState', N'Date', N'date', 0, 0),
      (N'SalesDetailQueryDailyState', N'ProjectionSchemaVersion', N'int', 0, 0),
      (N'SalesDetailQueryDailyState', N'SourceProductVersion', N'nvarchar', 256, 1),
      (N'SalesDetailQueryDailyState', N'SourceLastAggregatedAtUtc', N'datetime2', 0, 1),
      (N'SalesDetailQueryDailyState', N'MappingVersion', N'varchar', 64, 0),
      (N'SalesDetailQueryMonthlyProduct', N'Month', N'date', 0, 0),
      (N'SalesDetailQueryMonthlyProduct', N'RawSupplierCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryMonthlyProduct', N'ProductCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryMonthlyProduct', N'Revenue', N'decimal', 0, 0),
      (N'SalesDetailQueryMonthlyProduct', N'Quantity', N'bigint', 0, 0),
      (N'SalesDetailQueryMonthlyBranch', N'Month', N'date', 0, 0),
      (N'SalesDetailQueryMonthlyBranch', N'BranchCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryMonthlyBranch', N'RawSupplierCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryMonthlyBranch', N'ChinaSupplierCode', N'nvarchar', 100, 1),
      (N'SalesDetailQueryMonthlyBranch', N'AustralianSupplierCode', N'nvarchar', 100, 1),
      (N'SalesDetailQueryMonthlyBranch', N'MinProductCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryMonthlyBranch', N'MaxProductCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryMonthlyState', N'Month', N'date', 0, 0),
      (N'SalesDetailQueryMonthlyState', N'ProjectionSchemaVersion', N'int', 0, 0),
      (N'SalesDetailQueryMonthlyState', N'DayIdentity', N'varchar', 64, 0),
      (N'SalesDetailQueryMonthlyState', N'MappingVersion', N'varchar', 64, 0),
      (N'SalesDetailQueryMonthlyState', N'RefreshedAtUtc', N'datetime2', 0, 0)
    ) expected([table_name], [column_name], [type_name], [max_length], [is_nullable])
    LEFT JOIN sys.tables t ON t.[name] = expected.[table_name] AND SCHEMA_NAME(t.[schema_id]) = N'dbo'
    LEFT JOIN sys.columns c ON c.[object_id] = t.[object_id] AND c.[name] = expected.[column_name]
    LEFT JOIN sys.types ty ON ty.[user_type_id] = c.[user_type_id]
    WHERE c.[column_id] IS NULL OR ty.[name] <> expected.[type_name]
       OR (expected.[max_length] <> 0 AND c.[max_length] <> expected.[max_length])
       OR c.[is_nullable] <> expected.[is_nullable]
)
    THROW 51811, N'Sales detail monthly projection column signature is incompatible.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE [parent_object_id] = OBJECT_ID(N'dbo.SalesDetailQueryDailyProduct') AND [type] = N'PK')
 OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.SalesDetailQueryDailyBranch')
               AND [name] = N'CX_SalesDetailQueryDailyBranch_Date' AND [type] = 1)
 OR NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE [parent_object_id] = OBJECT_ID(N'dbo.SalesDetailQueryDailyState') AND [type] = N'PK')
 OR NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE [parent_object_id] = OBJECT_ID(N'dbo.SalesDetailQueryMonthlyProduct') AND [type] = N'PK')
 OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.SalesDetailQueryMonthlyBranch')
               AND [name] = N'CX_SalesDetailQueryMonthlyBranch_Month' AND [type] = 1)
 OR NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE [parent_object_id] = OBJECT_ID(N'dbo.SalesDetailQueryMonthlyState') AND [type] = N'PK')
    THROW 51812, N'Sales detail monthly projection index signature is incompatible.', 1;
""";
}
