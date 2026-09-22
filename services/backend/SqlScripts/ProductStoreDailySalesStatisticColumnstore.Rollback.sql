/*
  回滚 ProductStoreDailySalesStatisticColumnstore.sql：把 IX_LSPSA_Sales_Analytics 恢复为原 5 列定义
  （与 LocalSupplierProductSalesAnalysisPerformanceIndexes.sql 一致）。

  说明：
  - 应用侧查询不依赖列存索引存在，回滚只让销售明细长区间回到慢路径，不影响正确性。
  - 同样使用 DROP_EXISTING，旧索引在新索引建成前不会丢失。
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;
SET LOCK_TIMEOUT 5000;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
      AND name = N'IX_LSPSA_Sales_Analytics'
      AND type = 6
)
BEGIN
    PRINT N'IX_LSPSA_Sales_Analytics 不存在，无需回滚。';
    RETURN;
END;

CREATE NONCLUSTERED COLUMNSTORE INDEX [IX_LSPSA_Sales_Analytics]
    ON [dbo].[ProductStoreDailySalesStatistic]
    ([Date], [ProductCode], [BranchCode], [TotalQuantity], [TotalAmount])
    WITH
    (
        DROP_EXISTING = ON,
        ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
        MAXDOP = 2
    );
PRINT N'已恢复为 5 列定义。';
