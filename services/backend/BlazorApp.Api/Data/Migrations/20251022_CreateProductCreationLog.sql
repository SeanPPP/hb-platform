-- =============================================
-- 国内商品创建记录表 - 数据库迁移脚本
-- 版本: v1.0
-- 创建日期: 2025-10-22
-- 说明: 用于记录批量创建商品的历史，便于追踪和审计
-- =============================================

-- 检查表是否存在，如果不存在则创建
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DomesticProductCreationLog]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[DomesticProductCreationLog] (
        -- 主键
        [LogId] NVARCHAR(50) NOT NULL PRIMARY KEY,
        
        -- 关联字段
        [ProductCode] NVARCHAR(50) NOT NULL,
        [SupplierCode] NVARCHAR(50) NOT NULL,
        
        -- 基本信息（冗余字段，避免关联查询）
        [SupplierName] NVARCHAR(200) NULL,
        [HBProductNo] NVARCHAR(50) NOT NULL,
        [Barcode] NVARCHAR(50) NULL,
        [ProductName] NVARCHAR(200) NULL,
        
        -- 前缀信息
        [PrefixCode] NVARCHAR(50) NULL,
        [PrefixName] NVARCHAR(10) NULL,
        
        -- 创建方式和批次
        [CreationType] NVARCHAR(20) NOT NULL DEFAULT 'Batch',
        [BatchNumber] NVARCHAR(50) NULL,
        
        -- 备注
        [Remark] NVARCHAR(500) NULL,
        
        -- 审计字段
        [IsDeleted] BIT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [UpdatedAt] DATETIME2 NULL,
        [CreatedBy] NVARCHAR(50) NULL,
        [UpdatedBy] NVARCHAR(50) NULL,
        
        -- 外键约束
        CONSTRAINT [FK_DomesticProductCreationLog_DomesticProduct] 
            FOREIGN KEY ([ProductCode]) 
            REFERENCES [dbo].[DomesticProduct] ([ProductCode])
            ON DELETE CASCADE,
            
        CONSTRAINT [FK_DomesticProductCreationLog_ChinaSupplier] 
            FOREIGN KEY ([SupplierCode]) 
            REFERENCES [dbo].[ChinaSupplier] ([SupplierCode])
            ON DELETE NO ACTION
    );
    
    PRINT '✓ 表 [DomesticProductCreationLog] 创建成功';
END
ELSE
BEGIN
    PRINT '✓ 表 [DomesticProductCreationLog] 已存在，跳过创建';
END
GO

-- =============================================
-- 创建索引
-- =============================================

-- 供应商编码索引（高频查询）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DomesticProductCreationLog_SupplierCode' AND object_id = OBJECT_ID('DomesticProductCreationLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DomesticProductCreationLog_SupplierCode]
    ON [dbo].[DomesticProductCreationLog] ([SupplierCode])
    INCLUDE ([CreatedAt], [CreationType], [BatchNumber]);
    
    PRINT '✓ 索引 [IX_DomesticProductCreationLog_SupplierCode] 创建成功';
END
GO

-- 批次号索引（用于查询同一批次的商品）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DomesticProductCreationLog_BatchNumber' AND object_id = OBJECT_ID('DomesticProductCreationLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DomesticProductCreationLog_BatchNumber]
    ON [dbo].[DomesticProductCreationLog] ([BatchNumber])
    WHERE [BatchNumber] IS NOT NULL
    INCLUDE ([ProductCode], [HBProductNo], [CreatedAt]);
    
    PRINT '✓ 索引 [IX_DomesticProductCreationLog_BatchNumber] 创建成功';
END
GO

-- 创建时间索引（用于按时间查询）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DomesticProductCreationLog_CreatedAt' AND object_id = OBJECT_ID('DomesticProductCreationLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DomesticProductCreationLog_CreatedAt]
    ON [dbo].[DomesticProductCreationLog] ([CreatedAt] DESC)
    INCLUDE ([SupplierCode], [CreationType], [BatchNumber]);
    
    PRINT '✓ 索引 [IX_DomesticProductCreationLog_CreatedAt] 创建成功';
END
GO

-- 创建方式索引（用于统计分析）
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DomesticProductCreationLog_CreationType' AND object_id = OBJECT_ID('DomesticProductCreationLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DomesticProductCreationLog_CreationType]
    ON [dbo].[DomesticProductCreationLog] ([CreationType])
    INCLUDE ([SupplierCode], [CreatedAt]);
    
    PRINT '✓ 索引 [IX_DomesticProductCreationLog_CreationType] 创建成功';
END
GO

-- =============================================
-- 创建视图（可选，用于查询统计）
-- =============================================

IF EXISTS (SELECT * FROM sys.views WHERE name = 'V_ProductCreationStat')
BEGIN
    DROP VIEW [dbo].[V_ProductCreationStat];
    PRINT '✓ 删除旧视图 [V_ProductCreationStat]';
END
GO

CREATE VIEW [dbo].[V_ProductCreationStat]
AS
SELECT 
    l.[SupplierCode],
    l.[SupplierName],
    l.[CreationType],
    l.[BatchNumber],
    COUNT(*) AS [ProductCount],
    MIN(l.[CreatedAt]) AS [FirstCreatedAt],
    MAX(l.[CreatedAt]) AS [LastCreatedAt],
    l.[CreatedBy]
FROM [dbo].[DomesticProductCreationLog] l
WHERE l.[IsDeleted] = 0
GROUP BY 
    l.[SupplierCode],
    l.[SupplierName],
    l.[CreationType],
    l.[BatchNumber],
    l.[CreatedBy];
GO

PRINT '✓ 视图 [V_ProductCreationStat] 创建成功';
GO

-- =============================================
-- 添加注释（SQL Server 扩展属性）
-- =============================================

EXEC sys.sp_addextendedproperty 
    @name = N'MS_Description', 
    @value = N'国内商品创建记录表，用于记录批量创建商品的历史，便于追踪和审计', 
    @level0type = N'SCHEMA', @level0name = 'dbo', 
    @level1type = N'TABLE',  @level1name = 'DomesticProductCreationLog';
GO

EXEC sys.sp_addextendedproperty 
    @name = N'MS_Description', 
    @value = N'记录ID（UUID7格式）', 
    @level0type = N'SCHEMA', @level0name = 'dbo', 
    @level1type = N'TABLE',  @level1name = 'DomesticProductCreationLog',
    @level2type = N'COLUMN', @level2name = 'LogId';
GO

EXEC sys.sp_addextendedproperty 
    @name = N'MS_Description', 
    @value = N'创建方式：Batch=批量创建，Single=单个创建，Import=Excel导入创建', 
    @level0type = N'SCHEMA', @level0name = 'dbo', 
    @level1type = N'TABLE',  @level1name = 'DomesticProductCreationLog',
    @level2type = N'COLUMN', @level2name = 'CreationType';
GO

EXEC sys.sp_addextendedproperty 
    @name = N'MS_Description', 
    @value = N'批次号，同一批次创建的商品使用相同的批次号（UUID格式）', 
    @level0type = N'SCHEMA', @level0name = 'dbo', 
    @level1type = N'TABLE',  @level1name = 'DomesticProductCreationLog',
    @level2type = N'COLUMN', @level2name = 'BatchNumber';
GO

PRINT '✓ 表注释添加成功';
GO

-- =============================================
-- 完成
-- =============================================

PRINT '';
PRINT '========================================';
PRINT '✓ 数据库迁移完成！';
PRINT '========================================';
PRINT '';
PRINT '创建内容:';
PRINT '  - 表: DomesticProductCreationLog';
PRINT '  - 索引: 4个';
PRINT '  - 视图: V_ProductCreationStat';
PRINT '  - 注释: 完整';
PRINT '';
PRINT '下一步:';
PRINT '  1. 在Service层实现批量创建时记录日志';
PRINT '  2. 测试批量创建功能';
PRINT '  3. 验证外键约束和索引性能';
PRINT '';
GO

