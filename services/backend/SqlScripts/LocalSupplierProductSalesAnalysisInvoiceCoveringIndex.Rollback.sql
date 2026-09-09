/*
  澳洲本地商品分析进货汇总覆盖索引回退脚本（SQL Server）。

  执行前先恢复不依赖本索引的 service 版本并确认服务健康。
  本脚本只删除名称和完整定义均匹配的任务专用索引，不包含任何业务 DML。
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;
SET LOCK_TIMEOUT 5000;

IF DB_NAME() <> N'HBweb'
BEGIN
    THROW 51035, N'当前数据库不是 HBweb，禁止回退本地商品分析进货汇总覆盖索引。', 1;
END;

PRINT N'回退提醒：请先恢复不依赖本索引的 service 版本并确认服务健康。';

DECLARE @TableObjectId int = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoiceDetails]', N'U');
IF @TableObjectId IS NULL
BEGIN
    THROW 51036, N'缺少 dbo.StoreLocalSupplierInvoiceDetails，无法安全核对回退目标。', 1;
END;

DECLARE @IndexName sysname = N'IX_LSPSA_InvoiceDetails_Invoice_Product';
DECLARE @IndexId int =
(
    SELECT index_id
    FROM sys.indexes
    WHERE object_id = @TableObjectId
      AND name = @IndexName
);

IF @IndexId IS NULL
BEGIN
    PRINT N'任务专用覆盖索引不存在，无需回退。';
    RETURN;
END;

DECLARE @ActualFilter nvarchar(max) =
(
    SELECT filter_definition
    FROM sys.indexes
    WHERE object_id = @TableObjectId
      AND index_id = @IndexId
);
DECLARE @NormalizedFilter nvarchar(max) = LOWER(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@ActualFilter, N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N'')
);
DECLARE @ExpectedFilter nvarchar(max) =
    N'isdeleted=0andproductcodeisnotnullandproductcode<>n' + NCHAR(39) + NCHAR(39);

/* 定义有任何偏差时保留对象，禁止误删人工创建或已调整的同名索引。 */
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS i
    WHERE i.object_id = @TableObjectId
      AND i.index_id = @IndexId
      AND i.type = 2
      AND i.is_unique = 0
      AND i.is_hypothetical = 0
      AND i.has_filter = 1
)
   OR @NormalizedFilter <> @ExpectedFilter
   OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id = @TableObjectId AND index_id = @IndexId AND key_ordinal > 0) <> 2
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.index_columns AS ic
       INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = @TableObjectId AND ic.index_id = @IndexId
         AND ic.key_ordinal = 1 AND ic.is_descending_key = 0 AND c.name = N'InvoiceGUID'
   )
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.index_columns AS ic
       INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = @TableObjectId AND ic.index_id = @IndexId
         AND ic.key_ordinal = 2 AND ic.is_descending_key = 0 AND c.name = N'ProductCode'
   )
   OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id = @TableObjectId AND index_id = @IndexId AND is_included_column = 1) <> 4
   OR EXISTS
   (
       SELECT 1 FROM sys.index_columns AS ic
       INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = @TableObjectId AND ic.index_id = @IndexId
         AND ic.is_included_column = 1
         AND c.name NOT IN (N'StoreCode', N'Quantity', N'PurchasePrice', N'Amount')
   )
BEGIN
    THROW 51037, N'同名索引定义不一致；为保护既有对象，回退脚本未删除任何内容。', 1;
END;

DROP INDEX [IX_LSPSA_InvoiceDetails_Invoice_Product]
    ON [dbo].[StoreLocalSupplierInvoiceDetails];

PRINT N'已删除任务专用覆盖索引 IX_LSPSA_InvoiceDetails_Invoice_Product。';
