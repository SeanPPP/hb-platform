/* 精确回退：把 IX_sales_order_OrderTime 恢复为不含 Status 的原定义（在线重建，不删除索引）。 */
USE [POSM];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;
SET LOCK_TIMEOUT -1;

IF DB_NAME()<>N'POSM' OR @@SERVERNAME<>N'10_3_8_3'
    THROW 53100, N'拒绝回退：数据库或 SQL 实例与 2026-09-19 基线不一致。', 1;
IF NOT EXISTS (SELECT 1 FROM sys.database_recovery_status WHERE database_id=DB_ID()
               AND database_guid=CONVERT(uniqueidentifier,N'99BE94C1-3CEC-48CB-A22A-6850BF07B169'))
    THROW 53101, N'拒绝回退：database_guid 与基线不一致。', 1;

/* 只回退本方案产生的定义；其他定义一律不动。 */
DECLARE @cols nvarchar(4000) = (
    SELECT STRING_AGG(CONCAT(CASE WHEN ic.is_included_column=1 THEN N'I:' ELSE N'K:' END, c.name), N';')
           WITHIN GROUP (ORDER BY CASE WHEN ic.is_included_column=0 THEN 0 ELSE 1 END, ic.key_ordinal, ic.index_column_id)
    FROM sys.indexes AS i
    JOIN sys.index_columns AS ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'dbo.sales_order') AND i.name=N'IX_sales_order_OrderTime' AND i.type=2);
IF @cols IS NULL OR @cols <> N'K:OrderTime;I:OrderGuid;I:BranchCode;I:DeviceCode;I:TotalAmount;I:DiscountAmount;I:ActualAmount;I:ItemCount;I:Status'
    THROW 53102, N'拒绝回退：IX_sales_order_OrderTime 现定义不是本方案产生的定义。', 1;

CREATE NONCLUSTERED INDEX [IX_sales_order_OrderTime]
ON dbo.[sales_order] ([OrderTime])
INCLUDE ([OrderGuid], [BranchCode], [DeviceCode], [TotalAmount], [DiscountAmount], [ActualAmount], [ItemCount])
WITH (DROP_EXISTING=ON, ONLINE=ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION=5 MINUTES, ABORT_AFTER_WAIT=SELF)), MAXDOP=1, SORT_IN_TEMPDB=OFF)
ON [PRIMARY];
GO
