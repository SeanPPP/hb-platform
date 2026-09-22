/*
  手工整理商品分店日统计列存索引（在线，可随时中断）。

  何时需要：每天有数十万到数百万行日统计被删除重写，列存索引会累积已删除行和几千行的小行组；
  当 deleted_rows 超过 20% 或小行组超过 64 个时，扫描耗时会成倍增加。
  API 内的 ProductStoreDailyColumnstoreMaintenanceWorker 会按同样阈值自动执行；这里供人工兜底。
*/

SET NOCOUNT ON;
SET DEADLOCK_PRIORITY LOW;

SELECT state_desc,
       COUNT(*) AS row_groups,
       SUM(CASE WHEN total_rows < 102400 THEN 1 ELSE 0 END) AS small_row_groups,
       SUM(total_rows) AS total_rows,
       SUM(deleted_rows) AS deleted_rows
FROM sys.dm_db_column_store_row_group_physical_stats
WHERE object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
GROUP BY state_desc;

ALTER INDEX [IX_LSPSA_Sales_Analytics] ON [dbo].[ProductStoreDailySalesStatistic]
    REORGANIZE WITH (COMPRESS_ALL_ROW_GROUPS = ON);

SELECT state_desc,
       COUNT(*) AS row_groups,
       SUM(CASE WHEN total_rows < 102400 THEN 1 ELSE 0 END) AS small_row_groups,
       SUM(total_rows) AS total_rows,
       SUM(deleted_rows) AS deleted_rows
FROM sys.dm_db_column_store_row_group_physical_stats
WHERE object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
GROUP BY state_desc;
