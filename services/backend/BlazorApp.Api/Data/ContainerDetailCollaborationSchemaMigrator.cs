using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data;

/// <summary>货柜协作表的幂等启动迁移。</summary>
public static class ContainerDetailCollaborationSchemaMigrator
{
    // 独立 migration 在记账前及每次 schema-check 都使用同一签名，避免账本存在但表被误删时静默通过。
    internal const string VerifySql = """
SET NOCOUNT ON;
IF OBJECT_ID(N'[dbo].[ContainerDetailEditLease]', N'U') IS NULL
    THROW 51540, N'ContainerDetailEditLease table is missing.', 1;
IF OBJECT_ID(N'[dbo].[ContainerDetailFieldOverrideAudit]', N'U') IS NULL
    THROW 51541, N'ContainerDetailFieldOverrideAudit table is missing.', 1;
IF EXISTS (
    SELECT required.name
    FROM (VALUES (N'LeaseKey'), (N'ContainerGuid'), (N'UserGuid'), (N'ClientSessionId'), (N'State'), (N'LastActiveAtUtc'), (N'ExpiresAtUtc')) AS required(name)
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ContainerDetailEditLease]') AND name = required.name)
)
    THROW 51542, N'ContainerDetailEditLease columns are incompatible.', 1;
IF EXISTS (
    SELECT required.name
    FROM (VALUES (N'Id'), (N'ContainerGuid'), (N'DetailHguid'), (N'Field'), (N'ServerValue'), (N'OverrideValue'), (N'ConfirmationToken'), (N'ActorUserGuid'), (N'OccurredAtUtc'), (N'BatchGuid')) AS required(name)
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ContainerDetailFieldOverrideAudit]') AND name = required.name)
)
    THROW 51543, N'ContainerDetailFieldOverrideAudit columns are incompatible.', 1;
-- CodeFirst 映射已显式固定 SQL Server 类型；签名同步校验 type、长度、datetime scale 与 nullability，
-- 防止同名列被错误类型悄然替换后直到协作保存才失败。
IF EXISTS (
    SELECT 1
    FROM (VALUES
        (N'LeaseKey', 231, 128, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'ContainerGuid', 231, 128, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'UserGuid', 231, 128, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'UserName', 231, 256, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'ClientSessionId', 231, 256, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'State', 231, 32, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'LastActiveAtUtc', 42, 8, CAST(27 AS tinyint), CAST(7 AS tinyint), CAST(0 AS bit)),
        (N'ExpiresAtUtc', 42, 8, CAST(27 AS tinyint), CAST(7 AS tinyint), CAST(0 AS bit))
    ) AS expected(name, system_type_id, max_length, precision, scale, is_nullable)
    LEFT JOIN sys.columns AS c ON c.object_id = OBJECT_ID(N'[dbo].[ContainerDetailEditLease]') AND c.name = expected.name
    WHERE c.column_id IS NULL OR c.system_type_id <> expected.system_type_id OR c.max_length <> expected.max_length OR (expected.precision IS NOT NULL AND c.precision <> expected.precision) OR c.scale <> expected.scale OR c.is_nullable <> expected.is_nullable
)
    THROW 51550, N'ContainerDetailEditLease column signature is incompatible.', 1;
IF EXISTS (
    SELECT 1
    FROM (VALUES
        (N'Id', 36, 16, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'ContainerGuid', 231, 128, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'DetailHguid', 231, 128, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'Field', 231, 128, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'ServerValue', 231, -1, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(1 AS bit)),
        (N'OverrideValue', 231, -1, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(1 AS bit)),
        (N'ConfirmationToken', 231, 256, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'ActorUserGuid', 231, 128, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit)),
        (N'ActorName', 231, 256, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(1 AS bit)),
        (N'OccurredAtUtc', 42, 8, CAST(27 AS tinyint), CAST(7 AS tinyint), CAST(0 AS bit)),
        (N'BatchGuid', 36, 16, CAST(NULL AS tinyint), CAST(0 AS tinyint), CAST(0 AS bit))
    ) AS expected(name, system_type_id, max_length, precision, scale, is_nullable)
    LEFT JOIN sys.columns AS c ON c.object_id = OBJECT_ID(N'[dbo].[ContainerDetailFieldOverrideAudit]') AND c.name = expected.name
    WHERE c.column_id IS NULL OR c.system_type_id <> expected.system_type_id OR c.max_length <> expected.max_length OR (expected.precision IS NOT NULL AND c.precision <> expected.precision) OR c.scale <> expected.scale OR c.is_nullable <> expected.is_nullable
)
    THROW 51551, N'ContainerDetailFieldOverrideAudit column signature is incompatible.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes AS i JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(N'[dbo].[ContainerDetailEditLease]') AND i.is_primary_key = 1 AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND ic.key_ordinal = 1 AND c.name = N'LeaseKey' AND 1 = (SELECT COUNT(1) FROM sys.index_columns AS keysOnly WHERE keysOnly.object_id = i.object_id AND keysOnly.index_id = i.index_id AND keysOnly.key_ordinal > 0))
    THROW 51544, N'ContainerDetailEditLease primary key is incompatible.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes AS i JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE i.object_id = OBJECT_ID(N'[dbo].[ContainerDetailFieldOverrideAudit]') AND i.is_primary_key = 1 AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND ic.key_ordinal = 1 AND c.name = N'Id' AND 1 = (SELECT COUNT(1) FROM sys.index_columns AS keysOnly WHERE keysOnly.object_id = i.object_id AND keysOnly.index_id = i.index_id AND keysOnly.key_ordinal > 0))
    THROW 51545, N'ContainerDetailFieldOverrideAudit primary key is incompatible.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ContainerDetailEditLease]') AND name = N'IX_ContainerDetailEditLease_Container_Expires')
    THROW 51546, N'ContainerDetailEditLease expiry index is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ContainerDetailFieldOverrideAudit]') AND name = N'IX_ContainerDetailFieldOverrideAudit_Container_Occurred')
    THROW 51547, N'ContainerDetailFieldOverrideAudit lookup index is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes AS i WHERE i.object_id = OBJECT_ID(N'[dbo].[ContainerDetailEditLease]') AND i.name = N'IX_ContainerDetailEditLease_Container_Expires' AND i.is_unique = 0 AND i.is_disabled = 0 AND i.has_filter = 0 AND i.is_hypothetical = 0 AND 2 = (SELECT COUNT(1) FROM sys.index_columns AS keysOnly WHERE keysOnly.object_id=i.object_id AND keysOnly.index_id=i.index_id AND keysOnly.key_ordinal > 0) AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND ic.is_descending_key=0 AND c.name=N'ContainerGuid') AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=2 AND ic.is_descending_key=0 AND c.name=N'ExpiresAtUtc'))
    THROW 51552, N'ContainerDetailEditLease expiry index signature is incompatible.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes AS i WHERE i.object_id = OBJECT_ID(N'[dbo].[ContainerDetailFieldOverrideAudit]') AND i.name = N'IX_ContainerDetailFieldOverrideAudit_Container_Occurred' AND i.is_unique = 0 AND i.is_disabled = 0 AND i.has_filter = 0 AND i.is_hypothetical = 0 AND 2 = (SELECT COUNT(1) FROM sys.index_columns AS keysOnly WHERE keysOnly.object_id=i.object_id AND keysOnly.index_id=i.index_id AND keysOnly.key_ordinal > 0) AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND ic.is_descending_key=0 AND c.name=N'ContainerGuid') AND EXISTS (SELECT 1 FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=2 AND ic.is_descending_key=1 AND c.name=N'OccurredAtUtc'))
    THROW 51553, N'ContainerDetailFieldOverrideAudit lookup index signature is incompatible.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE object_id = OBJECT_ID(N'[dbo].[TR_ContainerDetailFieldOverrideAudit_AppendOnly]') AND parent_id = OBJECT_ID(N'[dbo].[ContainerDetailFieldOverrideAudit]') AND is_disabled = 0 AND is_instead_of_trigger = 1)
    THROW 51548, N'ContainerDetailFieldOverrideAudit append-only trigger is missing.', 1;
DECLARE @NormalizedAppendOnlyTrigger nvarchar(max) = LOWER(
    REPLACE(REPLACE(REPLACE(REPLACE(
        COALESCE(OBJECT_DEFINITION(OBJECT_ID(N'[dbo].[TR_ContainerDetailFieldOverrideAudit_AppendOnly]')), N''),
        CHAR(13), N''), CHAR(10), N''), CHAR(9), N''), N' ', N'')
);
-- SQL Server 会把 CREATE OR ALTER 创建的模块持久化为 CREATE 定义；两种 DDL 头部语义等价，
-- 但表、事件、正文、错误号和错误文本仍使用 BIN2 做完整逐字校验。
IF @NormalizedAppendOnlyTrigger COLLATE Latin1_General_100_BIN2 NOT IN (
    N'createoraltertrigger[dbo].[tr_containerdetailfieldoverrideaudit_appendonly]on[dbo].[containerdetailfieldoverrideaudit]insteadofupdate,deleteasbeginthrow51110,n''containerdetailfieldoverrideauditisappend-only.'',1;end',
    N'createtrigger[dbo].[tr_containerdetailfieldoverrideaudit_appendonly]on[dbo].[containerdetailfieldoverrideaudit]insteadofupdate,deleteasbeginthrow51110,n''containerdetailfieldoverrideauditisappend-only.'',1;end'
)
    THROW 51549, N'ContainerDetailFieldOverrideAudit append-only trigger is incompatible.', 1;
""";

    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        // 启动通用迁移对非 SQL Server 保持早返回契约；测试与其他 provider 不应隐式创建业务表。
        // 生产 SQL Server 才执行协作租约与覆盖审计的建表、索引和只追加触发器。
        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            return;
        }

        const string sql = """
IF OBJECT_ID(N'[dbo].[ContainerDetailEditLease]', N'U') IS NULL
CREATE TABLE [dbo].[ContainerDetailEditLease] (
    [LeaseKey] nvarchar(64) NOT NULL,
    [ContainerGuid] nvarchar(64) NOT NULL,
    [UserGuid] nvarchar(64) NOT NULL,
    [UserName] nvarchar(128) NOT NULL,
    [ClientSessionId] nvarchar(128) NOT NULL,
    [State] nvarchar(16) NOT NULL,
    [LastActiveAtUtc] datetime2(7) NOT NULL,
    [ExpiresAtUtc] datetime2(7) NOT NULL,
    CONSTRAINT [PK_ContainerDetailEditLease] PRIMARY KEY ([LeaseKey])
);
IF OBJECT_ID(N'[dbo].[ContainerDetailFieldOverrideAudit]', N'U') IS NULL
CREATE TABLE [dbo].[ContainerDetailFieldOverrideAudit] (
    [Id] uniqueidentifier NOT NULL,
    [ContainerGuid] nvarchar(64) NOT NULL,
    [DetailHguid] nvarchar(64) NOT NULL,
    [Field] nvarchar(64) NOT NULL,
    [ServerValue] nvarchar(max) NULL,
    [OverrideValue] nvarchar(max) NULL,
    [ConfirmationToken] nvarchar(128) NOT NULL,
    [ActorUserGuid] nvarchar(64) NOT NULL,
    [ActorName] nvarchar(128) NULL,
    [OccurredAtUtc] datetime2(7) NOT NULL,
    [BatchGuid] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_ContainerDetailFieldOverrideAudit] PRIMARY KEY ([Id])
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContainerDetailEditLease_Container_Expires' AND object_id = OBJECT_ID(N'[dbo].[ContainerDetailEditLease]'))
    CREATE INDEX [IX_ContainerDetailEditLease_Container_Expires] ON [dbo].[ContainerDetailEditLease]([ContainerGuid], [ExpiresAtUtc]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ContainerDetailFieldOverrideAudit_Container_Occurred' AND object_id = OBJECT_ID(N'[dbo].[ContainerDetailFieldOverrideAudit]'))
    CREATE INDEX [IX_ContainerDetailFieldOverrideAudit_Container_Occurred] ON [dbo].[ContainerDetailFieldOverrideAudit]([ContainerGuid], [OccurredAtUtc] DESC);
EXEC(N'CREATE OR ALTER TRIGGER [dbo].[TR_ContainerDetailFieldOverrideAudit_AppendOnly]
    ON [dbo].[ContainerDetailFieldOverrideAudit]
    INSTEAD OF UPDATE, DELETE AS
    BEGIN THROW 51110, N''ContainerDetailFieldOverrideAudit is append-only.'', 1; END');
""";
        await db.Ado.ExecuteCommandAsync(sql);
        logger.LogInformation("货柜明细协作表结构检查完成");
    }
}
