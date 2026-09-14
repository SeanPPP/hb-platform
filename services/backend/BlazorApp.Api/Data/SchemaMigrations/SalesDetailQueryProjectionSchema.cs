namespace BlazorApp.Api.Data.SchemaMigrations;

/// <summary>销售明细日投影与只增不删的历史商品搜索词典。</summary>
internal static class SalesDetailQueryProjectionSchema
{
    internal const string ApplySql = """
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;
BEGIN TRY
    IF OBJECT_ID(N'dbo.SalesDetailQueryDaily', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[SalesDetailQueryDaily]
        (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Date] date NOT NULL,
            [RawSupplierCode] nvarchar(50) NOT NULL,
            [ChinaSupplierCode] nvarchar(50) NULL,
            [AustralianSupplierCode] nvarchar(50) NULL,
            [SourceBranchCode] nvarchar(50) NOT NULL,
            [BranchCode] nvarchar(50) NOT NULL,
            [MinProductCode] nvarchar(50) NOT NULL,
            [MaxProductCode] nvarchar(50) NOT NULL,
            [Revenue] decimal(38,4) NOT NULL,
            [Quantity] bigint NOT NULL,
            [OrderCount] bigint NOT NULL,
            [GrossProfit] decimal(38,4) NULL,
            [StatisticRowCount] bigint NOT NULL,
            [CostedRowCount] bigint NOT NULL,
            [GrossProfitRowCount] bigint NOT NULL,
            CONSTRAINT [PK_SalesDetailQueryDaily] PRIMARY KEY CLUSTERED ([Id])
        );
        CREATE NONCLUSTERED INDEX [IX_SalesDetailQueryDaily_Date_SourceBranch]
            ON [dbo].[SalesDetailQueryDaily]
               ([Date], [SourceBranchCode], [BranchCode], [AustralianSupplierCode], [ChinaSupplierCode], [RawSupplierCode])
            INCLUDE ([MinProductCode], [MaxProductCode], [Revenue], [Quantity], [OrderCount], [GrossProfit],
                     [StatisticRowCount], [CostedRowCount], [GrossProfitRowCount]);
    END;

    IF OBJECT_ID(N'dbo.SalesDetailQueryProductAlias', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[SalesDetailQueryProductAlias]
        (
            [ProductCode] nvarchar(50) NOT NULL,
            [ProductName] nvarchar(255) NOT NULL,
            [Barcode] nvarchar(100) NOT NULL
        );
        -- 三列合计 810 字节，唯一聚集键同时承担追加去重与关键词候选回查。
        CREATE UNIQUE CLUSTERED INDEX [CUX_SalesDetailQueryProductAlias]
            ON [dbo].[SalesDetailQueryProductAlias] ([ProductCode], [ProductName], [Barcode]);
    END;

    IF OBJECT_ID(N'dbo.SalesDetailQueryProjectionState', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[SalesDetailQueryProjectionState]
        (
            [Date] date NOT NULL,
            [ProjectionSchemaVersion] int NOT NULL,
            [SourceProductVersion] nvarchar(128) NULL,
            [SourceLastAggregatedAtUtc] datetime2 NULL,
            [SourceCompletedAtUtc] datetime2 NULL,
            [SourceJobId] uniqueidentifier NULL,
            [MappingVersion] varchar(64) NOT NULL,
            [MappingHasFanout] bit NOT NULL,
            CONSTRAINT [PK_SalesDetailQueryProjectionState] PRIMARY KEY CLUSTERED ([Date])
        );
    END;

    IF COL_LENGTH(N'dbo.SalesDetailQueryProjectionState', N'MappingHasFanout') IS NULL
        ALTER TABLE [dbo].[SalesDetailQueryProjectionState]
            ADD [MappingHasFanout] bit NOT NULL
                CONSTRAINT [DF_SalesDetailQueryProjectionState_MappingHasFanout] DEFAULT (1);

    DECLARE @sdpCreatedMappingUse bit = 0;
    IF OBJECT_ID(N'dbo.SalesDetailQueryMappingUse', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[SalesDetailQueryMappingUse]
        (
            [Date] date NOT NULL,
            [ProductCode] nvarchar(50) NOT NULL,
            [ChinaSupplierCode] nvarchar(50) NULL,
            CONSTRAINT [PK_SalesDetailQueryMappingUse] PRIMARY KEY CLUSTERED ([Date], [ProductCode])
        );
        SET @sdpCreatedMappingUse = 1;
    END;

    -- 老覆盖没有逐日相关映射证明；新建证明表时统一降级为全局签名严格校验。
    IF @sdpCreatedMappingUse = 1
        UPDATE [dbo].[SalesDetailQueryProjectionState] SET [MappingHasFanout] = 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";

    // 运行时只读门禁锁定列、类型和关键索引；不兼容结构只能通过显式迁移修复。
    internal const string VerifySql = """
IF OBJECT_ID(N'dbo.SalesDetailQueryDaily', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesDetailQueryProductAlias', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesDetailQueryMappingUse', N'U') IS NULL
 OR OBJECT_ID(N'dbo.SalesDetailQueryProjectionState', N'U') IS NULL
    THROW 51800, N'Sales detail query projection tables are missing.', 1;

IF EXISTS (
    SELECT 1 FROM (VALUES
      (N'SalesDetailQueryDaily', N'Date', N'date', 0, 0),
      (N'SalesDetailQueryDaily', N'RawSupplierCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryDaily', N'ChinaSupplierCode', N'nvarchar', 100, 1),
      (N'SalesDetailQueryDaily', N'AustralianSupplierCode', N'nvarchar', 100, 1),
      (N'SalesDetailQueryDaily', N'SourceBranchCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryDaily', N'BranchCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryDaily', N'MinProductCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryDaily', N'MaxProductCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryProductAlias', N'ProductCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryProductAlias', N'ProductName', N'nvarchar', 510, 0),
      (N'SalesDetailQueryProductAlias', N'Barcode', N'nvarchar', 200, 0),
      (N'SalesDetailQueryMappingUse', N'Date', N'date', 0, 0),
      (N'SalesDetailQueryMappingUse', N'ProductCode', N'nvarchar', 100, 0),
      (N'SalesDetailQueryMappingUse', N'ChinaSupplierCode', N'nvarchar', 100, 1),
      (N'SalesDetailQueryProjectionState', N'Date', N'date', 0, 0),
      (N'SalesDetailQueryProjectionState', N'ProjectionSchemaVersion', N'int', 0, 0),
      (N'SalesDetailQueryProjectionState', N'SourceProductVersion', N'nvarchar', 256, 1),
      (N'SalesDetailQueryProjectionState', N'SourceLastAggregatedAtUtc', N'datetime2', 0, 1),
      (N'SalesDetailQueryProjectionState', N'SourceCompletedAtUtc', N'datetime2', 0, 1),
      (N'SalesDetailQueryProjectionState', N'SourceJobId', N'uniqueidentifier', 0, 1),
      (N'SalesDetailQueryProjectionState', N'MappingVersion', N'varchar', 64, 0)
     ,(N'SalesDetailQueryProjectionState', N'MappingHasFanout', N'bit', 0, 0)
    ) expected([table_name], [column_name], [type_name], [max_length], [is_nullable])
    LEFT JOIN sys.tables t ON t.[name] = expected.[table_name] AND SCHEMA_NAME(t.[schema_id]) = N'dbo'
    LEFT JOIN sys.columns c ON c.[object_id] = t.[object_id] AND c.[name] = expected.[column_name]
    LEFT JOIN sys.types ty ON ty.[user_type_id] = c.[user_type_id]
    WHERE c.[column_id] IS NULL OR ty.[name] <> expected.[type_name]
       OR (expected.[max_length] <> 0 AND c.[max_length] <> expected.[max_length])
       OR c.[is_nullable] <> expected.[is_nullable]
)
    THROW 51801, N'Sales detail query projection column signature is incompatible.', 1;

IF EXISTS (
    SELECT 1 FROM (VALUES
      (N'SalesDetailQueryDaily', N'Id', N'bigint', 19, 0, 0),
      (N'SalesDetailQueryDaily', N'Revenue', N'decimal', 38, 4, 0),
      (N'SalesDetailQueryDaily', N'Quantity', N'bigint', 19, 0, 0),
      (N'SalesDetailQueryDaily', N'OrderCount', N'bigint', 19, 0, 0),
      (N'SalesDetailQueryDaily', N'GrossProfit', N'decimal', 38, 4, 1),
      (N'SalesDetailQueryDaily', N'StatisticRowCount', N'bigint', 19, 0, 0),
      (N'SalesDetailQueryDaily', N'CostedRowCount', N'bigint', 19, 0, 0),
      (N'SalesDetailQueryDaily', N'GrossProfitRowCount', N'bigint', 19, 0, 0)
    ) expected([table_name], [column_name], [type_name], [precision], [scale], [is_nullable])
    LEFT JOIN sys.tables t ON t.[name] = expected.[table_name] AND SCHEMA_NAME(t.[schema_id]) = N'dbo'
    LEFT JOIN sys.columns c ON c.[object_id] = t.[object_id] AND c.[name] = expected.[column_name]
    LEFT JOIN sys.types ty ON ty.[user_type_id] = c.[user_type_id]
    WHERE c.[column_id] IS NULL OR ty.[name] <> expected.[type_name]
       OR c.[precision] <> expected.[precision] OR c.[scale] <> expected.[scale]
       OR c.[is_nullable] <> expected.[is_nullable]
)
    THROW 51801, N'Sales detail query projection numeric signature is incompatible.', 1;

IF COLUMNPROPERTY(OBJECT_ID(N'dbo.SalesDetailQueryDaily'), N'Id', 'IsIdentity') <> 1
    THROW 51801, N'Sales detail query projection identity signature is incompatible.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.SalesDetailQueryDaily')
               AND [name] = N'IX_SalesDetailQueryDaily_Date_SourceBranch')
 OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.SalesDetailQueryProductAlias')
               AND [name] = N'CUX_SalesDetailQueryProductAlias' AND [is_unique] = 1 AND [type] = 1)
 OR NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE [parent_object_id] = OBJECT_ID(N'dbo.SalesDetailQueryMappingUse')
               AND [type] = N'PK')
 OR NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE [parent_object_id] = OBJECT_ID(N'dbo.SalesDetailQueryProjectionState')
               AND [type] = N'PK')
    THROW 51802, N'Sales detail query projection index signature is incompatible.', 1;
""";
}
