/*
  澳洲本地商品分析性能索引（SQL Server）。

  安全约束：
  1. 本脚本仅供人工在已确认的业务数据库中执行，不接入应用启动迁移。
  2. 执行前必须核对数据库、表行数、等价索引、最近完整备份、磁盘空间、活动请求与阻塞。
  3. 建议按下方五个步骤逐段在低峰执行，每步完成后复查执行计划、锁等待和接口耗时。
  4. 脚本不会删除或重建既有索引；仅按本功能精确名称及定义判断已创建，重复执行安全。
  5. 非支持 ONLINE CREATE INDEX 的版本会直接停止，禁止无意中退化为离线建索引。
  6. EffectivePurchaseDate 持久化计算列会扫描并回填单据表，可能持有 SCH-M 锁；必须单独在低峰评估后执行。
  7. 快速路径还依赖三个共享基线索引；本脚本只核验、不创建或回滚，缺失时会在任何 DDL 前停止。

  共享基线索引预期：
  - PK_StoreLocalSupplierInvoice_InvoiceGUID：StoreLocalSupplierInvoice(InvoiceGUID) 聚集主键；
  - IX_ProductStoreDailySalesStatistic_Branch_Product_Date：现有分店前导销量覆盖索引；
  - IX_StoreLocalSupplierInvoiceDetails_InvoiceGUID_NotDeleted：
      ON StoreLocalSupplierInvoiceDetails(InvoiceGUID) INCLUDE(Amount) WHERE IsDeleted = 0。
    若最后一项缺失，须由数据库基线迁移负责人确认无等价共享索引后补建；禁止把它纳入本功能回滚。
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;
SET LOCK_TIMEOUT 5000;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @EngineEdition int = CONVERT(int, SERVERPROPERTY(N'EngineEdition'));
IF @EngineEdition NOT IN (3, 5, 8)
BEGIN
    THROW 51020, N'当前 SQL Server 版本未确认支持 ONLINE CREATE INDEX；请在维护窗口单独审阅，禁止自动改为离线创建。', 1;
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]')
      AND name = N'PK_StoreLocalSupplierInvoice_InvoiceGUID'
      AND is_disabled = 0
      AND is_hypothetical = 0
)
BEGIN
    THROW 51023, N'缺少共享基线主键 PK_StoreLocalSupplierInvoice_InvoiceGUID，已在任何 DDL 前停止。', 1;
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
      AND name = N'IX_ProductStoreDailySalesStatistic_Branch_Product_Date'
      AND is_disabled = 0
      AND is_hypothetical = 0
)
BEGIN
    THROW 51024, N'缺少共享基线索引 IX_ProductStoreDailySalesStatistic_Branch_Product_Date，已在任何 DDL 前停止。', 1;
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoiceDetails]')
      AND name = N'IX_StoreLocalSupplierInvoiceDetails_InvoiceGUID_NotDeleted'
      AND is_disabled = 0
      AND is_hypothetical = 0
)
BEGIN
    THROW 51025, N'缺少共享基线索引 IX_StoreLocalSupplierInvoiceDetails_InvoiceGUID_NotDeleted，已在任何 DDL 前停止。', 1;
END;

/* 第 1 步：本地商品主档候选、筛选与稳定排序。 */
IF OBJECT_ID(N'[dbo].[Product]', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes AS i
       WHERE i.object_id = OBJECT_ID(N'[dbo].[Product]')
         AND i.name = N'IX_LSPSA_Product_ProductCode_UUID'
         AND i.is_disabled = 0
         AND i.is_hypothetical = 0
         AND i.has_filter = 1
         AND EXISTS
         (
             SELECT 1
             FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c
                 ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 1 AND c.name = N'ProductCode'
         )
         AND EXISTS
         (
             SELECT 1
             FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c
                 ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 2 AND c.name = N'UUID'
         )
   )
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LSPSA_Product_ProductCode_UUID]
        ON [dbo].[Product] ([ProductCode], [UUID])
        INCLUDE ([LocalSupplierCode], [ItemNumber], [Barcode], [ProductName], [EnglishName], [ProductImage], [WarehouseCategoryGUID])
        WHERE [IsDeleted] = 0
          AND [IsActive] = 1
          AND [LocalSupplierCode] IS NOT NULL
          AND [LocalSupplierCode] <> N''
          AND [ProductCode] IS NOT NULL
          AND [ProductCode] <> N''
        WITH
        (
            ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
            SORT_IN_TEMPDB = ON,
            MAXDOP = 2
        );
END;

/* 第 2 步：进货单有效日期、分店和主明细连接。 */
IF OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]', N'U') IS NOT NULL
   AND COL_LENGTH(N'[dbo].[StoreLocalSupplierInvoice]', N'EffectivePurchaseDate') IS NULL
BEGIN
    /* 新建列和所有权标记必须原子完成；失败时不留下无法安全回滚的孤立列。 */
    BEGIN TRANSACTION;
    BEGIN TRY
        ALTER TABLE [dbo].[StoreLocalSupplierInvoice]
            ADD [EffectivePurchaseDate] AS
                CONVERT(date, COALESCE([InboundDate], [OrderDate], [CreatedAt])) PERSISTED;

        EXEC sys.sp_addextendedproperty
            @name = N'LSPSA_Owner',
            @value = N'LocalSupplierProductSalesAnalysisPerformanceIndexes',
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE', @level1name = N'StoreLocalSupplierInvoice',
            @level2type = N'COLUMN', @level2name = N'EffectivePurchaseDate';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;

IF OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]', N'U') IS NOT NULL
   AND COL_LENGTH(N'[dbo].[StoreLocalSupplierInvoice]', N'EffectivePurchaseDate') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.computed_columns
       WHERE object_id = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]')
         AND name = N'EffectivePurchaseDate'
         AND is_persisted = 1
   )
BEGIN
    THROW 51021, N'EffectivePurchaseDate 已存在但不是持久化计算列，已停止以避免使用错误定义。', 1;
END;

IF OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]', N'U') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM sys.computed_columns
       WHERE object_id = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]')
         AND name = N'EffectivePurchaseDate'
         AND
         (
             definition NOT LIKE N'%InboundDate%OrderDate%CreatedAt%'
             OR definition NOT LIKE N'%CONVERT%date%'
         )
   )
BEGIN
    THROW 51022, N'EffectivePurchaseDate 的现有定义与三级日期回退不一致，已停止以避免使用错误语义。', 1;
END;

IF OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]', N'U') IS NOT NULL
   AND COL_LENGTH(N'[dbo].[StoreLocalSupplierInvoice]', N'EffectivePurchaseDate') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes AS i
       WHERE i.object_id = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]')
         AND i.name = N'IX_LSPSA_Invoice_EffectiveDate_Store_Invoice'
         AND i.is_disabled = 0
         AND i.is_hypothetical = 0
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 1 AND c.name = N'EffectivePurchaseDate'
         )
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 2 AND c.name = N'StoreCode'
         )
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 3 AND c.name = N'InvoiceGUID'
         )
   )
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LSPSA_Invoice_EffectiveDate_Store_Invoice]
        ON [dbo].[StoreLocalSupplierInvoice] ([EffectivePurchaseDate], [StoreCode], [InvoiceGUID])
        INCLUDE ([SupplierCode], [InvoiceNo], [InboundDate], [OrderDate], [CreatedAt])
        WHERE [IsDeleted] = 0
        WITH
        (
            ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
            SORT_IN_TEMPDB = ON,
            MAXDOP = 2
        );
END;

/* 第 3 步：进货明细按商品收敛后连接单据；避免再增加第二个宽明细索引。 */
IF OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoiceDetails]', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes AS i
       WHERE i.object_id = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoiceDetails]')
         AND i.name = N'IX_LSPSA_InvoiceDetails_Product_Invoice'
         AND i.is_disabled = 0
         AND i.is_hypothetical = 0
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 1 AND c.name = N'ProductCode'
         )
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 2 AND c.name = N'InvoiceGUID'
         )
   )
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LSPSA_InvoiceDetails_Product_Invoice]
        ON [dbo].[StoreLocalSupplierInvoiceDetails] ([ProductCode], [InvoiceGUID])
        INCLUDE ([StoreCode], [SupplierCode], [Quantity], [PurchasePrice], [Amount], [ProductName])
        WHERE [IsDeleted] = 0
          AND [ProductCode] IS NOT NULL
          AND [ProductCode] <> N''
        WITH
        (
            ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
            SORT_IN_TEMPDB = ON,
            MAXDOP = 2
        );
END;

/* 第 4 步：单商品与全商品日期范围使用互补覆盖索引；保留已有分店前导索引。 */
IF OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes AS i
       WHERE i.object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
         AND i.name = N'IX_LSPSA_Sales_Product_Date'
         AND i.is_disabled = 0
         AND i.is_hypothetical = 0
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 1 AND c.name = N'ProductCode'
         )
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 2 AND c.name = N'Date'
         )
   )
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LSPSA_Sales_Product_Date]
        ON [dbo].[ProductStoreDailySalesStatistic] ([ProductCode], [Date])
        INCLUDE ([BranchCode], [TotalQuantity], [TotalAmount])
        WITH
        (
            ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
            SORT_IN_TEMPDB = ON,
            MAXDOP = 2
        );
END;

IF OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes AS i
       WHERE i.object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
         AND i.name = N'IX_LSPSA_Sales_Date_Product'
         AND i.is_disabled = 0
         AND i.is_hypothetical = 0
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 1 AND c.name = N'Date'
         )
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.key_ordinal = 2 AND c.name = N'ProductCode'
         )
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.is_included_column = 1 AND c.name = N'TotalQuantity'
         )
         AND EXISTS
         (
             SELECT 1 FROM sys.index_columns AS ic
             INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
               AND ic.is_included_column = 1 AND c.name = N'TotalAmount'
         )
   )
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LSPSA_Sales_Date_Product]
        ON [dbo].[ProductStoreDailySalesStatistic] ([Date], [ProductCode])
        INCLUDE ([BranchCode], [TotalQuantity], [TotalAmount])
        WITH
        (
            ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
            SORT_IN_TEMPDB = ON,
            MAXDOP = 2
        );
END;

/* 第 5 步：90 天全分店聚合使用批处理执行；需监控每日删除 + BulkCopy 的写入回归。 */
IF OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes AS i
       WHERE i.object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
         AND i.name = N'IX_LSPSA_Sales_Analytics'
         AND i.type = 6
         AND i.is_disabled = 0
         AND i.is_hypothetical = 0
   )
BEGIN
    CREATE NONCLUSTERED COLUMNSTORE INDEX [IX_LSPSA_Sales_Analytics]
        ON [dbo].[ProductStoreDailySalesStatistic]
        ([Date], [ProductCode], [BranchCode], [TotalQuantity], [TotalAmount])
        WITH
        (
            ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
            MAXDOP = 2
        );
END;
