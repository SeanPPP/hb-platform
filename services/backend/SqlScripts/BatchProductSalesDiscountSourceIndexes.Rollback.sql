/* 精确回退：只删除本方案创建、且定义和专属 extended property 都匹配的两个索引。 */
USE [HOT_POS_CLOUD];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;
SET LOCK_TIMEOUT -1;

IF DB_NAME()<>N'HOT_POS_CLOUD' OR @@SERVERNAME<>N'10_3_8_3'
    THROW 51100, N'拒绝回退：数据库或 SQL 实例与 2026-09-15 基线不一致。', 1;
IF NOT EXISTS (SELECT 1 FROM sys.database_recovery_status WHERE database_id=DB_ID()
               AND database_guid=CONVERT(uniqueidentifier,N'0c518478-c3a0-4b2b-bca2-164427670c88'))
    THROW 51101, N'拒绝回退：database_guid 与基线不一致。', 1;

DECLARE @owner nvarchar(256)=N'hb-discount-source-index-repair-20260915:v1';
/* 每项先验完整定义和唯一归属；任一不匹配就停，不猜测删除。 */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.B销售清单详情表副本') AND name=N'IX_B销售清单详情表副本_折扣日日期单号')
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes AS i
        JOIN sys.extended_properties AS ep ON ep.class=7 AND ep.major_id=i.object_id AND ep.minor_id=i.index_id
            AND ep.name=N'HB_DiscountSourceIndex_20260915_Owner' AND CONVERT(nvarchar(256),ep.value)=@owner
        WHERE i.object_id=OBJECT_ID(N'dbo.B销售清单详情表副本') AND i.name=N'IX_B销售清单详情表副本_折扣日日期单号'
          AND i.type=2 AND i.is_unique=0 AND i.is_primary_key=0 AND i.has_filter=0
          AND (SELECT STRING_AGG(CONCAT(CASE WHEN ic.is_included_column=1 THEN N'I:' ELSE N'K:' END,c.name),N';') WITHIN GROUP (ORDER BY CASE WHEN ic.is_included_column=0 THEN 0 ELSE 1 END, ic.key_ordinal,ic.index_column_id)
               FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
               WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id)
              =N'K:B结账日期;K:B销售单号;I:B分店代码;I:B产品编号;I:B货号;I:B供应商ID;I:B条形码;I:B单价;I:B折扣率;I:B数量;I:B原价合计金额;I:B合计金额;I:B单位;I:B退货码;I:FGC_CreateDate;I:FGC_LastModifyDate')
        THROW 51102, N'拒绝回退明细索引：定义或专属 extended property 不匹配。', 1;
END;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.B销售清单主表副本') AND name=N'IX_B销售清单主表副本_折扣日单号日期')
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes AS i
        JOIN sys.extended_properties AS ep ON ep.class=7 AND ep.major_id=i.object_id AND ep.minor_id=i.index_id
            AND ep.name=N'HB_DiscountSourceIndex_20260915_Owner' AND CONVERT(nvarchar(256),ep.value)=@owner
        WHERE i.object_id=OBJECT_ID(N'dbo.B销售清单主表副本') AND i.name=N'IX_B销售清单主表副本_折扣日单号日期'
          AND i.type=2 AND i.is_unique=0 AND i.is_primary_key=0 AND i.has_filter=0
          AND (SELECT STRING_AGG(CONCAT(CASE WHEN ic.is_included_column=1 THEN N'I:' ELSE N'K:' END,c.name),N';') WITHIN GROUP (ORDER BY CASE WHEN ic.is_included_column=0 THEN 0 ELSE 1 END, ic.key_ordinal,ic.index_column_id)
               FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
               WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id)
              =N'K:B销售单号;K:B结账日期;I:B单据类型;I:B原销售单号;I:FGC_CreateDate;I:FGC_LastModifyDate')
        THROW 51103, N'拒绝回退主表索引：定义或专属 extended property 不匹配。', 1;
END;

/* 两个仍存在的对象均已通过定义与归属核验，才开始实际删除。 */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.B销售清单详情表副本') AND name=N'IX_B销售清单详情表副本_折扣日日期单号')
    DROP INDEX [IX_B销售清单详情表副本_折扣日日期单号] ON dbo.[B销售清单详情表副本];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.B销售清单主表副本') AND name=N'IX_B销售清单主表副本_折扣日单号日期')
    DROP INDEX [IX_B销售清单主表副本_折扣日单号日期] ON dbo.[B销售清单主表副本];
GO
