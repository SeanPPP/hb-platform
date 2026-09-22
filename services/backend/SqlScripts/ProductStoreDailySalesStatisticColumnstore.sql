/*
  商品分店日统计列存索引：覆盖销售明细报表所需全部列（SQL Server）。

  背景（2026-09-22 生产实测）：
  - 销售明细「1 年 + 同期」要按聚集索引读完整张 3.3 GB 宽表，CPU 21 秒、耗时 31–48 秒；
    去掉分组键上的字符串函数没有收益，成本在读宽行本身。
  - 表上已有列存索引 IX_LSPSA_Sales_Analytics 只含 5 列，缺 SupplierCode/OrderCount/TotalCost/GrossProfit，
    销售明细用不上；且因每日删除 + 5000 行一批的 BulkCopy 写入，已累积 5400 个碎行组、72% 已删除行，
    1 年聚合反而要 27 秒。

  做法：
  1. 用 DROP_EXISTING 把同名列存索引重建为 9 列（原 5 列的超集），重建后行组重新紧凑；
     LocalSupplierProductSalesAnalysisPerformanceIndexes.sql 只按名称与类型判断存在，重复执行仍是空操作。
  2. 由 API 内的 ProductStoreDailyColumnstoreMaintenanceWorker 按行组健康度定期 REORGANIZE，
     手工处理时可执行本目录的 ProductStoreDailySalesStatisticColumnstore.Reorganize.sql。

  安全约束：
  1. 仅供人工在已确认的业务数据库低峰执行，不接入启动迁移；执行前核对库名、行数、最近备份、磁盘余量。
  2. DROP_EXISTING 在新索引建成前不会丢弃旧索引，失败时旧索引保持不变。
  3. 只在支持 ONLINE 的版本执行（Developer/Enterprise），禁止退化为离线重建。
  4. 7.5M 行、MAXDOP 2 预计数分钟；期间会占用一到两个核心，避开统计回填高峰。
  5. 回滚脚本：ProductStoreDailySalesStatisticColumnstore.Rollback.sql（恢复原 5 列定义）。
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;
SET LOCK_TIMEOUT 5000;

DECLARE @EngineEdition int = CONVERT(int, SERVERPROPERTY(N'EngineEdition'));
IF @EngineEdition NOT IN (3, 5, 8)
BEGIN
    THROW 51030, N'当前 SQL Server 版本未确认支持 ONLINE 列存索引重建；请在维护窗口单独审阅。', 1;
END;

IF OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]', N'U') IS NULL
BEGIN
    THROW 51031, N'缺少 ProductStoreDailySalesStatistic 表，已停止。', 1;
END;

/* 已有其他列存索引（一表只能有一个）且不叫本脚本的名字时停止，由人工决定合并方式。 */
IF EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
      AND type = 6
      AND name <> N'IX_LSPSA_Sales_Analytics'
)
BEGIN
    THROW 51032, N'表上已存在其他名称的列存索引，请先人工确认后再执行。', 1;
END;

/* 已经覆盖全部 9 列时重复执行为空操作。 */
IF EXISTS
(
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
      AND i.name = N'IX_LSPSA_Sales_Analytics'
      AND i.type = 6
      AND i.is_disabled = 0
      AND
      (
          SELECT COUNT(*)
          FROM sys.index_columns AS ic
          INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
            AND c.name IN (N'Date', N'ProductCode', N'BranchCode', N'SupplierCode',
                           N'TotalQuantity', N'TotalAmount', N'OrderCount', N'TotalCost', N'GrossProfit')
      ) = 9
)
BEGIN
    PRINT N'IX_LSPSA_Sales_Analytics 已覆盖销售明细所需列，无需重建。';
    RETURN;
END;

PRINT N'重建 IX_LSPSA_Sales_Analytics 为 9 列覆盖定义（ONLINE，MAXDOP 2）…';
CREATE NONCLUSTERED COLUMNSTORE INDEX [IX_LSPSA_Sales_Analytics]
    ON [dbo].[ProductStoreDailySalesStatistic]
    ([Date], [ProductCode], [BranchCode], [SupplierCode], [TotalQuantity], [TotalAmount], [OrderCount], [TotalCost], [GrossProfit])
    WITH
    (
        DROP_EXISTING = ON,
        ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)),
        MAXDOP = 2
    );

/* 重建后核对行组健康度：期望已删除行为 0、行组数接近 行数/1048576。 */
SELECT state_desc,
       COUNT(*) AS row_groups,
       SUM(total_rows) AS total_rows,
       SUM(deleted_rows) AS deleted_rows,
       SUM(size_in_bytes) / 1024 / 1024 AS size_mb
FROM sys.dm_db_column_store_row_group_physical_stats
WHERE object_id = OBJECT_ID(N'[dbo].[ProductStoreDailySalesStatistic]')
GROUP BY state_desc;
