/* READ-ONLY metadata baseline for HOT_POS_CLOUD discount-source index review. */
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
GO
SELECT N'IDENTITY' AS [Section], @@SERVERNAME AS [ServerName],
       CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS [ProductVersion],
       CAST(SERVERPROPERTY('Edition') AS nvarchar(256)) AS [Edition],
       DB_NAME() AS [ConnectedDatabase], DB_ID(N'HOT_POS_CLOUD') AS [TargetDatabaseId],
       (SELECT database_guid FROM sys.database_recovery_status WHERE database_id=DB_ID(N'HOT_POS_CLOUD')) AS [TargetDatabaseGuid];
SELECT N'DATABASE' AS [Section], d.name, rs.database_guid, d.state_desc, d.recovery_model_desc, d.log_reuse_wait_desc,
       compatibility_level, collation_name
FROM sys.databases AS d
JOIN sys.database_recovery_status AS rs ON rs.database_id=d.database_id
WHERE d.name=N'HOT_POS_CLOUD';
GO
USE [HOT_POS_CLOUD];
GO
SELECT N'OBJECT' AS [Section], s.name AS [SchemaName], o.name AS [ObjectName], o.type_desc,
       o.create_date, o.modify_date
FROM sys.objects AS o JOIN sys.schemas AS s ON s.schema_id=o.schema_id
WHERE o.object_id IN (OBJECT_ID(N'dbo.B销售清单详情表副本'), OBJECT_ID(N'dbo.B销售清单主表副本'));
SELECT N'COLUMN' AS [Section], OBJECT_SCHEMA_NAME(c.object_id) AS [SchemaName], OBJECT_NAME(c.object_id) AS [ObjectName],
       c.column_id, c.name, t.name AS [TypeName], c.max_length, c.precision, c.scale, c.is_nullable, c.collation_name
FROM sys.columns AS c JOIN sys.types AS t ON t.user_type_id=c.user_type_id
WHERE c.object_id IN (OBJECT_ID(N'dbo.B销售清单详情表副本'), OBJECT_ID(N'dbo.B销售清单主表副本'))
ORDER BY [ObjectName], c.column_id;
SELECT N'INDEX' AS [Section], OBJECT_SCHEMA_NAME(i.object_id) AS [SchemaName], OBJECT_NAME(i.object_id) AS [ObjectName],
       i.index_id, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_disabled, i.fill_factor, i.has_filter,
       i.filter_definition,
       STRING_AGG(CONCAT(CASE WHEN ic.is_included_column=1 THEN N'I:' ELSE N'K:' END, c.name,
                         CASE WHEN ic.is_descending_key=1 THEN N' DESC' ELSE N'' END), N';')
           WITHIN GROUP (ORDER BY ic.key_ordinal, ic.index_column_id) AS [Columns]
FROM sys.indexes AS i
LEFT JOIN sys.index_columns AS ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
LEFT JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id IN (OBJECT_ID(N'dbo.B销售清单详情表副本'), OBJECT_ID(N'dbo.B销售清单主表副本'))
GROUP BY i.object_id,i.index_id,i.name,i.type_desc,i.is_unique,i.is_primary_key,i.is_disabled,i.fill_factor,i.has_filter,i.filter_definition
ORDER BY [ObjectName],i.index_id;
SELECT N'TABLE_SPACE' AS [Section], OBJECT_SCHEMA_NAME(ps.object_id) AS [SchemaName], OBJECT_NAME(ps.object_id) AS [ObjectName],
       SUM(ps.row_count) AS [Rows], CAST(SUM(ps.reserved_page_count)*8.0/1024 AS decimal(18,1)) AS [ReservedMB],
       CAST(SUM(ps.used_page_count)*8.0/1024 AS decimal(18,1)) AS [UsedMB]
FROM sys.dm_db_partition_stats AS ps
WHERE ps.object_id IN (OBJECT_ID(N'dbo.B销售清单详情表副本'), OBJECT_ID(N'dbo.B销售清单主表副本'))
GROUP BY ps.object_id;
SELECT N'FILE' AS [Section], name, type_desc, physical_name,
       CAST(size*8.0/1024 AS decimal(18,1)) AS [AllocatedMB],
       CASE WHEN type_desc=N'ROWS' THEN CAST(FILEPROPERTY(name,'SpaceUsed')*8.0/1024 AS decimal(18,1)) END AS [UsedMB],
       CASE WHEN type_desc=N'ROWS' THEN CAST((size-FILEPROPERTY(name,'SpaceUsed'))*8.0/1024 AS decimal(18,1)) END AS [InternalFreeMB],
       max_size, growth, is_percent_growth
FROM sys.database_files;
SELECT N'LOG' AS [Section], total_log_size_in_bytes/1024/1024 AS [TotalLogMB],
       used_log_space_in_bytes/1024/1024 AS [UsedLogMB], used_log_space_in_percent AS [UsedPercent]
FROM sys.dm_db_log_space_usage;
SELECT N'CANDIDATE_INDEX' AS [Section], OBJECT_NAME(object_id) AS [ObjectName], name, type_desc, is_disabled
FROM sys.indexes
WHERE name IN (N'IX_B销售清单详情表副本_折扣日日期单号', N'IX_B销售清单主表副本_折扣日单号日期');
GO
USE [master];
GO
SELECT N'VOLUME' AS [Section], DB_NAME(mf.database_id) AS [DatabaseName], mf.type_desc, mf.name, mf.physical_name,
       CAST(vs.total_bytes/1024.0/1024/1024 AS decimal(18,2)) AS [VolumeTotalGB],
       CAST(vs.available_bytes/1024.0/1024/1024 AS decimal(18,2)) AS [VolumeAvailableGB],
       vs.file_system_type, vs.supports_sparse_files, vs.supports_alternate_streams
FROM sys.master_files AS mf
CROSS APPLY sys.dm_os_volume_stats(mf.database_id, mf.file_id) AS vs
WHERE mf.database_id=DB_ID(N'HOT_POS_CLOUD')
ORDER BY mf.type_desc, mf.file_id;
GO
