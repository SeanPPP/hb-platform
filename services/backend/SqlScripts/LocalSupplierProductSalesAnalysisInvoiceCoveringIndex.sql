/*
  澳洲本地商品分析进货汇总覆盖索引（SQL Server）。

  仅允许在 HBweb 人工执行。脚本不会修改或删除任何既有共享索引；
  同名索引已存在时必须与本任务定义完全一致，否则立即停止。
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

IF DB_NAME() <> N'HBweb'
BEGIN
    THROW 51030, N'当前数据库不是 HBweb，禁止创建本地商品分析进货汇总覆盖索引。', 1;
END;

DECLARE @TableObjectId int = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoiceDetails]', N'U');
IF @TableObjectId IS NULL
BEGIN
    THROW 51031, N'缺少 dbo.StoreLocalSupplierInvoiceDetails，已在任何 DDL 前停止。', 1;
END;

IF
(
    SELECT COUNT(*)
    FROM sys.columns
    WHERE object_id = @TableObjectId
      AND name IN (N'InvoiceGUID', N'ProductCode', N'StoreCode', N'Quantity', N'PurchasePrice', N'Amount', N'IsDeleted')
) <> 7
BEGIN
    THROW 51032, N'进货明细表缺少索引所需列，已在任何 DDL 前停止。', 1;
END;

DECLARE @IndexName sysname = N'IX_LSPSA_InvoiceDetails_Invoice_Product';
DECLARE @IndexId int =
(
    SELECT index_id
    FROM sys.indexes
    WHERE object_id = @TableObjectId
      AND name = @IndexName
);

IF @IndexId IS NOT NULL
BEGIN
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

    /* 精确核对类型、键顺序、包含列集合与过滤条件，防止误认同名对象。 */
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id = @TableObjectId
          AND i.index_id = @IndexId
          AND i.type = 2
          AND i.is_unique = 0
          AND i.is_disabled = 0
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
        THROW 51033, N'同名索引已存在但定义不一致；为保护既有对象，脚本未作修改。', 1;
    END;

    PRINT N'目标覆盖索引已存在且定义一致，无需重复创建。';
    RETURN;
END;

DECLARE @EngineEdition int = CONVERT(int, SERVERPROPERTY(N'EngineEdition'));
IF @EngineEdition NOT IN (3, 5, 8)
BEGIN
    THROW 51034, N'当前 SQL Server 版本未确认支持 ONLINE CREATE INDEX；禁止自动退化为离线创建。', 1;
END;

CREATE NONCLUSTERED INDEX [IX_LSPSA_InvoiceDetails_Invoice_Product]
    ON [dbo].[StoreLocalSupplierInvoiceDetails] ([InvoiceGUID], [ProductCode])
    INCLUDE ([StoreCode], [Quantity], [PurchasePrice], [Amount])
    WHERE [IsDeleted] = 0
      AND [ProductCode] IS NOT NULL
      AND [ProductCode] <> N''
    WITH
    (
        ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
        SORT_IN_TEMPDB = ON,
        MAXDOP = 2
    );

PRINT N'已创建 IX_LSPSA_InvoiceDetails_Invoice_Product。';
