/*
  HOT_POS_CLOUD 折扣日来源索引：仅续跑主表索引。
  先证明 2026-09-15 明细索引已按本方案提交且归属正确；不会重建或回退明细索引。
  只允许已核验实例/数据库；每一项 guard 的实际值会在拒绝前返回。
*/
USE [HOT_POS_CLOUD];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;

DECLARE @detailObjectId int=OBJECT_ID(N'dbo.B销售清单详情表副本');
DECLARE @mainObjectId int=OBJECT_ID(N'dbo.B销售清单主表副本');
DECLARE @owner nvarchar(256)=N'hb-discount-source-index-repair-20260915:v1';
IF DB_NAME()<>N'HOT_POS_CLOUD' OR @@SERVERNAME<>N'10_3_8_3'
    THROW 51300,N'拒绝续跑：数据库或 SQL 实例与 2026-09-15 基线不一致。',1;
IF NOT EXISTS (SELECT 1 FROM sys.database_recovery_status WHERE database_id=DB_ID()
               AND database_guid=CONVERT(uniqueidentifier,N'0c518478-c3a0-4b2b-bca2-164427670c88'))
    THROW 51301,N'拒绝续跑：database_guid 与 2026-09-15 基线不一致。',1;
IF TRY_CONVERT(int,SERVERPROPERTY('ProductMajorVersion'))<>16
   OR CONVERT(nvarchar(256),SERVERPROPERTY('Edition')) NOT LIKE N'Developer Edition%'
    THROW 51302,N'拒绝续跑：SQL Server 版本或 Edition 与已审 ONLINE 选项不一致。',1;
IF @detailObjectId IS NULL OR @mainObjectId IS NULL
    THROW 51303,N'拒绝续跑：目标表不存在。',1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=@detailObjectId AND name=N'B结账日期' AND system_type_id=40)
   OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=@detailObjectId AND name=N'B销售单号' AND system_type_id=231 AND max_length=100)
   OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=@mainObjectId AND name=N'B销售单号' AND system_type_id=231 AND max_length=100)
   OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=@mainObjectId AND name=N'B结账日期' AND system_type_id=40)
    THROW 51304,N'拒绝续跑：关键列类型/长度与基线不一致。',1;

/* 不仅按名称：必须验证已提交明细索引的完整定义与 owner property。 */
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i JOIN sys.extended_properties ep
      ON ep.class=7 AND ep.major_id=i.object_id AND ep.minor_id=i.index_id
     AND ep.name=N'HB_DiscountSourceIndex_20260915_Owner' AND CONVERT(nvarchar(256),ep.value)=@owner
    WHERE i.object_id=@detailObjectId AND i.name=N'IX_B销售清单详情表副本_折扣日日期单号'
      AND i.type=2 AND i.is_disabled=0 AND i.is_unique=0 AND i.has_filter=0
      AND (SELECT STRING_AGG(CONCAT(CASE WHEN ic.is_included_column=1 THEN N'I:' ELSE N'K:' END,c.name),N';')
           WITHIN GROUP (ORDER BY CASE WHEN ic.is_included_column=0 THEN 0 ELSE 1 END,ic.key_ordinal,ic.index_column_id)
           FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
           WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id)
       =N'K:B结账日期;K:B销售单号;I:B分店代码;I:B产品编号;I:B货号;I:B供应商ID;I:B条形码;I:B单价;I:B折扣率;I:B数量;I:B原价合计金额;I:B合计金额;I:B单位;I:B退货码;I:FGC_CreateDate;I:FGC_LastModifyDate')
    THROW 51305,N'拒绝续跑：既有明细索引的定义或 owner property 不匹配。',1;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=@mainObjectId AND name=N'IX_B销售清单主表副本_折扣日单号日期')
    THROW 51306,N'拒绝续跑：主表候选索引名已存在；先人工核对定义/归属。',1;
IF EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id=@mainObjectId AND i.type=2 AND i.is_disabled=0
      AND (SELECT c.name FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1)=N'B销售单号'
      AND (SELECT c.name FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=2)=N'B结账日期')
    THROW 51307,N'拒绝续跑：主表已存在同键序或更长前缀索引，先评估覆盖能力。',1;

/* 每次运行实际输出 guard 值；后续 IF 失败前已有可审计原因。 */
DECLARE @rowsFreeMB decimal(19,1),@volumeFreeMB decimal(19,1),@logUsedPercent decimal(9,2),@recovery nvarchar(60),@reuse nvarchar(60),@rowsCanGrow bit,@logCanGrow bit;
SELECT @rowsFreeMB=SUM((size-FILEPROPERTY(name,'SpaceUsed'))*8.0/1024.0),
 @rowsCanGrow=MIN(CASE WHEN growth>0 AND (max_size=-1 OR max_size>size) THEN 1 ELSE 0 END)
FROM sys.database_files WHERE type_desc=N'ROWS';
SELECT @logCanGrow=MIN(CASE WHEN growth>0 AND (max_size=-1 OR max_size>size) THEN 1 ELSE 0 END)
FROM sys.database_files WHERE type_desc=N'LOG';
SELECT @logUsedPercent=used_log_space_in_percent FROM sys.dm_db_log_space_usage;
SELECT @recovery=recovery_model_desc,@reuse=log_reuse_wait_desc FROM sys.databases WHERE database_id=DB_ID();
SELECT @volumeFreeMB=MIN(vs.available_bytes/1024.0/1024.0)
FROM sys.database_files df CROSS APPLY sys.dm_os_volume_stats(DB_ID(),df.file_id) vs WHERE df.type_desc IN(N'ROWS',N'LOG');
SELECT N'RESUME_MAIN_GUARD' AS GuardName,@rowsFreeMB AS RowsFreeMB,@volumeFreeMB AS VolumeFreeMB,@logUsedPercent AS LogUsedPercent,
 @recovery AS RecoveryModel,@reuse AS LogReuseWait,@rowsCanGrow AS RowsCanGrow,@logCanGrow AS LogCanGrow,
 CAST(CASE WHEN @rowsFreeMB>=4096 THEN 1 ELSE 0 END AS bit) AS RowsFreePass,
 CAST(CASE WHEN @volumeFreeMB>=16384 THEN 1 ELSE 0 END AS bit) AS VolumePass,
 CAST(CASE WHEN @logUsedPercent<80 THEN 1 ELSE 0 END AS bit) AS LogUsedPass,
 CAST(CASE WHEN @recovery=N'FULL' THEN 1 ELSE 0 END AS bit) AS RecoveryPass,
 CAST(CASE WHEN @reuse IN(N'NOTHING',N'LOG_BACKUP') THEN 1 ELSE 0 END AS bit) AS ReusePass;
IF @rowsFreeMB IS NULL OR @rowsFreeMB<4096 OR @volumeFreeMB IS NULL OR @volumeFreeMB<16384
   OR @logUsedPercent IS NULL OR @logUsedPercent>=80 OR @recovery<>N'FULL' OR @reuse NOT IN(N'NOTHING',N'LOG_BACKUP')
   OR @rowsCanGrow<>1 OR @logCanGrow<>1
    THROW 51308,N'拒绝续跑主表索引：上方 RESUME_MAIN_GUARD 有未通过项。',1;

SET LOCK_TIMEOUT -1;
BEGIN TRY
    BEGIN TRANSACTION;
    CREATE NONCLUSTERED INDEX [IX_B销售清单主表副本_折扣日单号日期]
    ON dbo.[B销售清单主表副本] ([B销售单号],[B结账日期])
    INCLUDE ([B单据类型],[B原销售单号],[FGC_CreateDate],[FGC_LastModifyDate])
    WITH (ONLINE=ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION=5 MINUTES,ABORT_AFTER_WAIT=SELF)),MAXDOP=1,SORT_IN_TEMPDB=OFF);
    EXEC sys.sp_addextendedproperty
      @name=N'HB_DiscountSourceIndex_20260915_Owner',@value=N'hb-discount-source-index-repair-20260915:v1',
      @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'B销售清单主表副本',
      @level2type=N'INDEX',@level2name=N'IX_B销售清单主表副本_折扣日单号日期';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
