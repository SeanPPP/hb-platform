/*
  澳洲本地商品分析性能索引回滚脚本（SQL Server）。

  只移除本功能命名的对象，不删除任何既有索引。脚本可重复执行。
  EffectivePurchaseDate 仅在列级 LSPSA_Owner 标记证明由本功能创建时删除；
  缺少标记时安全保留，交由数据库负责人另行核验。
  建议按“销量列存 -> 销量行存 -> 明细 -> 单据 -> 商品”的逆序逐段执行并在每步后复查健康状态。
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;
SET LOCK_TIMEOUT 5000;

IF EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
      AND name = N'IX_LSPSA_Sales_Analytics'
)
BEGIN
    DROP INDEX [IX_LSPSA_Sales_Analytics]
        ON [dbo].[ProductStoreDailySalesStatistic];
END;

IF EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
      AND name = N'IX_LSPSA_Sales_Date_Product'
)
BEGIN
    DROP INDEX [IX_LSPSA_Sales_Date_Product]
        ON [dbo].[ProductStoreDailySalesStatistic];
END;

IF EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
      AND name = N'IX_LSPSA_Sales_Product_Date'
)
BEGIN
    DROP INDEX [IX_LSPSA_Sales_Product_Date]
        ON [dbo].[ProductStoreDailySalesStatistic];
END;

IF EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoiceDetails]')
      AND name = N'IX_LSPSA_InvoiceDetails_Product_Invoice'
)
BEGIN
    DROP INDEX [IX_LSPSA_InvoiceDetails_Product_Invoice]
        ON [dbo].[StoreLocalSupplierInvoiceDetails];
END;

IF EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]')
      AND name = N'IX_LSPSA_Invoice_EffectiveDate_Store_Invoice'
)
BEGIN
    DROP INDEX [IX_LSPSA_Invoice_EffectiveDate_Store_Invoice]
        ON [dbo].[StoreLocalSupplierInvoice];
END;

IF OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]', N'U') IS NOT NULL
   AND COL_LENGTH(N'[dbo].[StoreLocalSupplierInvoice]', N'EffectivePurchaseDate') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM sys.extended_properties AS ep
       WHERE ep.class = 1
         AND ep.major_id = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]')
         AND ep.minor_id = COLUMNPROPERTY(
             OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]'),
             N'EffectivePurchaseDate',
             N'ColumnId'
         )
         AND ep.name = N'LSPSA_Owner'
         AND CONVERT(nvarchar(128), ep.value) =
             N'LocalSupplierProductSalesAnalysisPerformanceIndexes'
   )
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.index_columns AS ic
       INNER JOIN sys.columns AS c
           ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = OBJECT_ID(N'[dbo].[StoreLocalSupplierInvoice]')
         AND c.name = N'EffectivePurchaseDate'
   )
BEGIN
    ALTER TABLE [dbo].[StoreLocalSupplierInvoice]
        DROP COLUMN [EffectivePurchaseDate];
END;

IF EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[Product]')
      AND name = N'IX_LSPSA_Product_ProductCode_UUID'
)
BEGIN
    DROP INDEX [IX_LSPSA_Product_ProductCode_UUID]
        ON [dbo].[Product];
END;
