/*
  审阅用：HOT_POS_CLOUD 折扣日来源读取索引候选。
  2026-09-15 只读基线身份：@@SERVERNAME=10_3_8_3；database_guid=0c518478-c3a0-4b2b-bca2-164427670c88。
  未经运维窗口确认不得执行。每组 CREATE 及其归属标记使用独立显式事务；首个错误即停止。
*/
USE [HOT_POS_CLOUD];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;

/* 精确目标 guard：名称、实例和 database_guid 必须同时匹配只读基线。 */
IF DB_NAME() <> N'HOT_POS_CLOUD'
    THROW 51000, N'拒绝执行：当前数据库不是 HOT_POS_CLOUD。', 1;
IF @@SERVERNAME <> N'10_3_8_3'
    THROW 51001, N'拒绝执行：SQL 实例与 2026-09-15 只读基线不一致。', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.database_recovery_status
    WHERE database_id=DB_ID() AND database_guid=CONVERT(uniqueidentifier, N'0c518478-c3a0-4b2b-bca2-164427670c88'))
    THROW 51002, N'拒绝执行：database_guid 与 2026-09-15 只读基线不一致。', 1;
IF TRY_CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) <> 16
    THROW 51003, N'拒绝执行：需要 SQL Server 2022（major 16）。', 1;
IF CONVERT(nvarchar(256), SERVERPROPERTY('Edition')) NOT LIKE N'Developer Edition%'
    THROW 51004, N'拒绝执行：Edition 与已核验的 Developer Edition 不一致。', 1;
IF OBJECT_ID(N'dbo.B销售清单详情表副本', N'U') IS NULL
   OR OBJECT_ID(N'dbo.B销售清单主表副本', N'U') IS NULL
    THROW 51005, N'拒绝执行：目标主表或明细表不存在。', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.B销售清单详情表副本') AND name=N'B结账日期' AND system_type_id=40)
   OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.B销售清单详情表副本') AND name=N'B销售单号' AND system_type_id=231 AND max_length=100)
   OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.B销售清单主表副本') AND name=N'B销售单号' AND system_type_id=231 AND max_length=100)
   OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.B销售清单主表副本') AND name=N'B结账日期' AND system_type_id=40)
    THROW 51006, N'拒绝执行：目标关键列类型/长度与已核验定义不一致。', 1;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.B销售清单详情表副本') AND name=N'IX_B销售清单详情表副本_折扣日日期单号')
   OR EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.B销售清单主表副本') AND name=N'IX_B销售清单主表副本_折扣日单号日期')
    THROW 51007, N'拒绝执行：候选索引名已存在；先人工核对定义和归属。', 1;
/* 同键序或更长的键前缀也可能已覆盖访问路径；不按名称盲目重复创建。 */
IF EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id=OBJECT_ID(N'dbo.B销售清单详情表副本') AND i.type=2 AND i.is_disabled=0
      AND (SELECT c.name FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
           WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1)=N'B结账日期'
      AND (SELECT c.name FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
           WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=2)=N'B销售单号')
   OR EXISTS (
    SELECT 1 FROM sys.indexes AS i
    WHERE i.object_id=OBJECT_ID(N'dbo.B销售清单主表副本') AND i.type=2 AND i.is_disabled=0
      AND (SELECT c.name FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
           WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1)=N'B销售单号'
      AND (SELECT c.name FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
           WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=2)=N'B结账日期')
    THROW 51010, N'拒绝执行：已存在同键序或更长前缀的索引；先人工评估覆盖能力。', 1;

/* 每次 CREATE 前调用：确认 physical volume、ROWS/log 空间、FULL 与 NOTHING/LOG_BACKUP、可增长事实。 */
DECLARE @minRowsFreeMB decimal(19,1), @minVolumeFreeMB decimal(19,1), @indexLabel nvarchar(128);
DECLARE @rowsFreeMB decimal(19,1), @volumeFreeMB decimal(19,1), @logUsedPercent decimal(9,2);
DECLARE @recovery nvarchar(60), @reuse nvarchar(60), @rowsCanGrow bit, @logCanGrow bit;

/* 索引一：明细 (B结账日期, B销售单号)。 */
SELECT @minRowsFreeMB=4096.0, @minVolumeFreeMB=16384.0, @indexLabel=N'明细索引';
SELECT @rowsFreeMB=SUM((size-FILEPROPERTY(name,'SpaceUsed'))*8.0/1024.0),
       @rowsCanGrow=MIN(CASE WHEN growth>0 AND (max_size=-1 OR max_size>size) THEN 1 ELSE 0 END)
FROM sys.database_files WHERE type_desc=N'ROWS';
SELECT @logCanGrow=MIN(CASE WHEN growth>0 AND (max_size=-1 OR max_size>size) THEN 1 ELSE 0 END)
FROM sys.database_files WHERE type_desc=N'LOG';
SELECT @logUsedPercent=used_log_space_in_percent FROM sys.dm_db_log_space_usage;
SELECT @recovery=recovery_model_desc, @reuse=log_reuse_wait_desc FROM sys.databases WHERE database_id=DB_ID();
SELECT @volumeFreeMB=MIN(vs.available_bytes/1024.0/1024.0)
FROM sys.database_files AS df
CROSS APPLY sys.dm_os_volume_stats(DB_ID(), df.file_id) AS vs
WHERE df.type_desc IN (N'ROWS', N'LOG');
IF @rowsFreeMB IS NULL OR @rowsFreeMB<@minRowsFreeMB OR @volumeFreeMB IS NULL OR @volumeFreeMB<@minVolumeFreeMB
   OR @logUsedPercent IS NULL OR @logUsedPercent>=80 OR @recovery<>N'FULL' OR @reuse NOT IN (N'NOTHING', N'LOG_BACKUP')
   OR @rowsCanGrow<>1 OR @logCanGrow<>1
    THROW 51008, N'拒绝执行明细索引：data/log/volume/recovery/log_reuse_wait/growth 复查未通过。', 1;

/* CREATE 的 schema-lock 等待只受 WAIT_AT_LOW_PRIORITY 约束：不可用时五分钟后自行放弃，不终止业务会话。 */
SET LOCK_TIMEOUT -1;
BEGIN TRY
    BEGIN TRANSACTION;
    CREATE NONCLUSTERED INDEX [IX_B销售清单详情表副本_折扣日日期单号]
    ON dbo.[B销售清单详情表副本] ([B结账日期], [B销售单号])
    INCLUDE ([B分店代码], [B产品编号], [B货号], [B供应商ID], [B条形码],
             [B单价], [B折扣率], [B数量], [B原价合计金额], [B合计金额],
             [B单位], [B退货码], [FGC_CreateDate], [FGC_LastModifyDate])
    WITH (ONLINE=ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION=5 MINUTES, ABORT_AFTER_WAIT=SELF)), MAXDOP=1, SORT_IN_TEMPDB=OFF);
    EXEC sys.sp_addextendedproperty
        @name=N'HB_DiscountSourceIndex_20260915_Owner', @value=N'hb-discount-source-index-repair-20260915:v1',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'B销售清单详情表副本',
        @level2type=N'INDEX', @level2name=N'IX_B销售清单详情表副本_折扣日日期单号';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

/* 索引二：主表 (B销售单号, B结账日期)。重新读取空间与恢复事实，不复用前次结果。 */
SELECT @minRowsFreeMB=4096.0, @minVolumeFreeMB=16384.0, @indexLabel=N'主表索引';
SELECT @rowsFreeMB=SUM((size-FILEPROPERTY(name,'SpaceUsed'))*8.0/1024.0),
       @rowsCanGrow=MIN(CASE WHEN growth>0 AND (max_size=-1 OR max_size>size) THEN 1 ELSE 0 END)
FROM sys.database_files WHERE type_desc=N'ROWS';
SELECT @logCanGrow=MIN(CASE WHEN growth>0 AND (max_size=-1 OR max_size>size) THEN 1 ELSE 0 END)
FROM sys.database_files WHERE type_desc=N'LOG';
SELECT @logUsedPercent=used_log_space_in_percent FROM sys.dm_db_log_space_usage;
SELECT @recovery=recovery_model_desc, @reuse=log_reuse_wait_desc FROM sys.databases WHERE database_id=DB_ID();
SELECT @volumeFreeMB=MIN(vs.available_bytes/1024.0/1024.0)
FROM sys.database_files AS df CROSS APPLY sys.dm_os_volume_stats(DB_ID(), df.file_id) AS vs
WHERE df.type_desc IN (N'ROWS', N'LOG');
IF @rowsFreeMB IS NULL OR @rowsFreeMB<@minRowsFreeMB OR @volumeFreeMB IS NULL OR @volumeFreeMB<@minVolumeFreeMB
   OR @logUsedPercent IS NULL OR @logUsedPercent>=80 OR @recovery<>N'FULL' OR @reuse NOT IN (N'NOTHING', N'LOG_BACKUP')
   OR @rowsCanGrow<>1 OR @logCanGrow<>1
    THROW 51009, N'拒绝执行主表索引：data/log/volume/recovery/log_reuse_wait/growth 复查未通过。', 1;

SET LOCK_TIMEOUT -1;
BEGIN TRY
    BEGIN TRANSACTION;
    CREATE NONCLUSTERED INDEX [IX_B销售清单主表副本_折扣日单号日期]
    ON dbo.[B销售清单主表副本] ([B销售单号], [B结账日期])
    INCLUDE ([B单据类型], [B原销售单号], [FGC_CreateDate], [FGC_LastModifyDate])
    WITH (ONLINE=ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION=5 MINUTES, ABORT_AFTER_WAIT=SELF)), MAXDOP=1, SORT_IN_TEMPDB=OFF);
    EXEC sys.sp_addextendedproperty
        @name=N'HB_DiscountSourceIndex_20260915_Owner', @value=N'hb-discount-source-index-repair-20260915:v1',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'B销售清单主表副本',
        @level2type=N'INDEX', @level2name=N'IX_B销售清单主表副本_折扣日单号日期';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

/* 仅验证元数据；不运行全日业务查询。 */
SELECT OBJECT_SCHEMA_NAME(i.object_id) AS [Schema], OBJECT_NAME(i.object_id) AS [Table], i.name, i.type_desc,
       i.is_disabled, i.has_filter, i.fill_factor
FROM sys.indexes AS i
WHERE i.object_id IN (OBJECT_ID(N'dbo.B销售清单详情表副本'), OBJECT_ID(N'dbo.B销售清单主表副本'))
  AND i.name IN (N'IX_B销售清单详情表副本_折扣日日期单号', N'IX_B销售清单主表副本_折扣日单号日期');
GO
