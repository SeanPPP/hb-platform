/*
  收银记录：订单时间索引 IX_sales_order_OrderTime 增加包含列 Status。
  2026-09-19 只读基线身份：@@SERVERNAME=10_3_8_3；POSM database_guid=99BE94C1-3CEC-48CB-A22A-6850BF07B169。
  现定义：K:OrderTime; I:OrderGuid, BranchCode, DeviceCode, TotalAmount, DiscountAmount, ActualAmount, ItemCount
  （PRIMARY 文件组、无压缩、fill_factor 0、无筛选、无扩展属性）。

  背景：列表按状态汇总（已支付/退款/已取消）、按状态筛选、关键词命中后取状态都要读 Status，
  而它不在时间索引里，只能回聚簇索引（约 850 MB）逐行查。生产缓存寿命低时（2026-09-19 实测 PLE 59 秒）
  全部分店 92 天的汇总要 2.4–3.8 秒；包含 Status 后只读约 300 MB 的时间索引。
  只增加一个 int 包含列，键与其余包含列不变，现有查询计划不受影响。

  ONLINE + DROP_EXISTING：重建期间旧索引继续可用；最后的结构锁低优先级等待五分钟，拿不到就放弃，不影响收银同步。
  未经用户确认执行窗口不得执行。回退见 PosmSalesOrderOrderTimeIndexIncludeStatus.Rollback.sql。
*/
USE [POSM];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;

IF DB_NAME() <> N'POSM'
    THROW 53000, N'拒绝执行：当前数据库不是 POSM。', 1;
IF @@SERVERNAME <> N'10_3_8_3'
    THROW 53001, N'拒绝执行：SQL 实例与 2026-09-19 只读基线不一致。', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.database_recovery_status
    WHERE database_id=DB_ID() AND database_guid=CONVERT(uniqueidentifier, N'99BE94C1-3CEC-48CB-A22A-6850BF07B169'))
    THROW 53002, N'拒绝执行：database_guid 与 2026-09-19 只读基线不一致。', 1;
IF CONVERT(nvarchar(256), SERVERPROPERTY('Edition')) NOT LIKE N'Developer Edition%'
    THROW 53004, N'拒绝执行：Edition 与已核验的 Developer Edition 不一致（ONLINE 需要）。', 1;

/* 现定义必须与基线完全一致（含 Status 已加入时视为已执行，拒绝重复执行）。 */
DECLARE @cols nvarchar(4000) = (
    SELECT STRING_AGG(CONCAT(CASE WHEN ic.is_included_column=1 THEN N'I:' ELSE N'K:' END, c.name), N';')
           WITHIN GROUP (ORDER BY CASE WHEN ic.is_included_column=0 THEN 0 ELSE 1 END, ic.key_ordinal, ic.index_column_id)
    FROM sys.indexes AS i
    JOIN sys.index_columns AS ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'dbo.sales_order') AND i.name=N'IX_sales_order_OrderTime'
      AND i.type=2 AND i.is_unique=0 AND i.has_filter=0 AND i.is_disabled=0);
IF @cols IS NULL OR @cols <> N'K:OrderTime;I:OrderGuid;I:BranchCode;I:DeviceCode;I:TotalAmount;I:DiscountAmount;I:ActualAmount;I:ItemCount'
    THROW 53005, N'拒绝执行：IX_sales_order_OrderTime 现定义与基线不一致（可能已执行过）；先人工核对。', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.sales_order') AND name=N'Status' AND system_type_id=56)
    THROW 53006, N'拒绝执行：sales_order.Status 不是 int。', 1;

/* 空间与日志复查。 */
DECLARE @volumeFreeMB decimal(19,1), @logUsedPercent decimal(9,2), @rowsCanGrow bit, @logCanGrow bit, @reuse nvarchar(60);
SELECT @rowsCanGrow=MIN(CASE WHEN growth>0 AND (max_size=-1 OR max_size>size) THEN 1 ELSE 0 END) FROM sys.database_files WHERE type_desc=N'ROWS';
SELECT @logCanGrow=MIN(CASE WHEN growth>0 AND (max_size=-1 OR max_size>size) THEN 1 ELSE 0 END) FROM sys.database_files WHERE type_desc=N'LOG';
SELECT @logUsedPercent=used_log_space_in_percent FROM sys.dm_db_log_space_usage;
SELECT @reuse=log_reuse_wait_desc FROM sys.databases WHERE database_id=DB_ID();
SELECT @volumeFreeMB=MIN(vs.available_bytes/1024.0/1024.0)
FROM sys.database_files AS df CROSS APPLY sys.dm_os_volume_stats(DB_ID(), df.file_id) AS vs
WHERE df.type_desc IN (N'ROWS', N'LOG');
/* SIMPLE 模式下 log_reuse_wait_desc 反映上次检查点时的状态，可能残留 ACTIVE_TRANSACTION；
   只有库内确有超过 60 秒的事务时才视为日志被占住。 */
DECLARE @longTransactions int = (
    SELECT COUNT(*) FROM sys.dm_tran_database_transactions
    WHERE database_id=DB_ID() AND database_transaction_begin_time < DATEADD(second, -60, GETDATE()));
IF @volumeFreeMB IS NULL OR @volumeFreeMB<8192.0 OR @rowsCanGrow<>1 OR @logCanGrow<>1
   OR @logUsedPercent IS NULL OR @logUsedPercent>=80
   OR @reuse NOT IN (N'NOTHING', N'CHECKPOINT', N'LOG_BACKUP', N'ACTIVE_TRANSACTION')
   OR (@reuse=N'ACTIVE_TRANSACTION' AND @longTransactions>0)
    THROW 53009, N'拒绝执行：volume/growth/log/log_reuse_wait 复查未通过。', 1;

SET LOCK_TIMEOUT -1;
CREATE NONCLUSTERED INDEX [IX_sales_order_OrderTime]
ON dbo.[sales_order] ([OrderTime])
INCLUDE ([OrderGuid], [BranchCode], [DeviceCode], [TotalAmount], [DiscountAmount], [ActualAmount], [ItemCount], [Status])
WITH (DROP_EXISTING=ON, ONLINE=ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION=5 MINUTES, ABORT_AFTER_WAIT=SELF)), MAXDOP=1, SORT_IN_TEMPDB=OFF)
ON [PRIMARY];

SELECT i.name,
       (SELECT STRING_AGG(CONCAT(CASE WHEN ic.is_included_column=1 THEN N'I:' ELSE N'K:' END, c.name), N';')
               WITHIN GROUP (ORDER BY CASE WHEN ic.is_included_column=0 THEN 0 ELSE 1 END, ic.key_ordinal, ic.index_column_id)
        FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id) AS [Columns],
       CAST((SELECT SUM(used_page_count) FROM sys.dm_db_partition_stats ps WHERE ps.object_id=i.object_id AND ps.index_id=i.index_id)*8.0/1024 AS decimal(18,1)) AS [UsedMB]
FROM sys.indexes AS i
WHERE i.object_id=OBJECT_ID(N'dbo.sales_order') AND i.name=N'IX_sales_order_OrderTime';
GO
