using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

[SugarTable("POSM_DeviceActivationGrant")]
public sealed class DeviceActivationCodeGrant
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid GrantId { get; set; }

    [SugarColumn(ColumnDataType = "binary(32)")]
    public byte[] SecretHash { get; set; } = Array.Empty<byte>();

    [SugarColumn(Length = 50)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(Length = 20)]
    public string DeviceSystem { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    [SugarColumn(Length = 128)]
    public string CreatedBy { get; set; } = string.Empty;

    [SugarColumn(Length = 200)]
    public string Reason { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? RevokedBy { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? RevokeReason { get; set; }

    public DateTime? ConsumedAtUtc { get; set; }

    [SugarColumn(Length = 100, IsNullable = true)]
    public string? ConsumedHardwareId { get; set; }

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? ConsumedDeviceCode { get; set; }

    public int? ConsumedDeviceRegistrationId { get; set; }

    [SugarColumn(ColumnDataType = "binary(32)", IsNullable = true)]
    public byte[]? ConsumedAuthorizationHash { get; set; }

    [SugarColumn(Length = 20, IsNullable = true)]
    public string? ConsumedDeviceSystem { get; set; }

    [SugarColumn(Length = 10, IsNullable = true)]
    public string? ConsumptionKind { get; set; }

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? PreviousStoreCode { get; set; }

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? PreviousDeviceCode { get; set; }

    [SugarColumn(ColumnDataType = "rowversion", IsOnlyIgnoreInsert = true)]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Web 与 POS API 共用同一份幂等 SQL，防止两个进程启动时创建出不一致的表结构。
/// </summary>
public static class DeviceActivationCodeSchema
{
    /// <summary>
    /// HBWeb 常规启动使用的只读结构门禁；只查询系统目录，不获取锁，也不写入探针数据。
    /// </summary>
    public const string VerifySql = """
        SET NOCOUNT ON;

        DECLARE @DeviceActivationTableId int =
            OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]', N'U');
        IF @DeviceActivationTableId IS NULL
            THROW 51100, 'Device activation grant table is missing.', 1;

        IF COL_LENGTH(N'dbo.POSM_DeviceActivationGrant', N'ActivationCode') IS NOT NULL
            THROW 51101, 'Device activation grant table must not store plaintext activation codes.', 1;

        -- 用完整期望集合与系统目录双向比对，新增、缺失或形状漂移的列都会失败关闭。
        IF EXISTS (
            SELECT 1
            FROM (VALUES
                (N'GrantId', N'uniqueidentifier', CAST(16 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)),
                (N'SecretHash', N'binary', CAST(32 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)),
                (N'StoreCode', N'varchar', CAST(50 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)),
                (N'DeviceSystem', N'varchar', CAST(20 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)),
                (N'CreatedAtUtc', N'datetime2', CAST(8 AS smallint), CAST(0 AS bit), CAST(7 AS tinyint)),
                (N'CreatedBy', N'nvarchar', CAST(256 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)),
                (N'Reason', N'nvarchar', CAST(400 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)),
                (N'ExpiresAtUtc', N'datetime2', CAST(8 AS smallint), CAST(0 AS bit), CAST(7 AS tinyint)),
                (N'RevokedAtUtc', N'datetime2', CAST(8 AS smallint), CAST(1 AS bit), CAST(7 AS tinyint)),
                (N'RevokedBy', N'nvarchar', CAST(256 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                (N'RevokeReason', N'nvarchar', CAST(400 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                (N'ConsumedAtUtc', N'datetime2', CAST(8 AS smallint), CAST(1 AS bit), CAST(7 AS tinyint)),
                (N'ConsumedHardwareId', N'varchar', CAST(100 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                (N'ConsumedDeviceCode', N'varchar', CAST(50 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                (N'ConsumedDeviceRegistrationId', N'int', CAST(4 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                (N'ConsumedAuthorizationHash', N'binary', CAST(32 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                (N'ConsumedDeviceSystem', N'varchar', CAST(20 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                (N'ConsumptionKind', N'varchar', CAST(10 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                (N'PreviousStoreCode', N'varchar', CAST(50 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                (N'PreviousDeviceCode', N'varchar', CAST(50 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                (N'RowVersion', N'timestamp', CAST(8 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)))
                AS expected([ColumnName], [TypeName], [MaxLength], [IsNullable], [Scale])
            FULL OUTER JOIN (
                SELECT
                    columnInfo.[name] AS [ColumnName],
                    typeInfo.[name] AS [TypeName],
                    columnInfo.[max_length] AS [MaxLength],
                    columnInfo.[is_nullable] AS [IsNullable],
                    columnInfo.[scale] AS [Scale]
                FROM sys.columns AS columnInfo
                INNER JOIN sys.types AS typeInfo
                    ON typeInfo.[user_type_id] = columnInfo.[user_type_id]
                WHERE columnInfo.[object_id] = @DeviceActivationTableId)
                AS actual
                ON actual.[ColumnName] = expected.[ColumnName]
            WHERE expected.[ColumnName] IS NULL
               OR actual.[ColumnName] IS NULL
               OR actual.[TypeName] <> expected.[TypeName]
               OR actual.[MaxLength] <> expected.[MaxLength]
               OR actual.[IsNullable] <> expected.[IsNullable]
               OR (expected.[Scale] IS NOT NULL AND actual.[Scale] <> expected.[Scale]))
            THROW 51102, 'Device activation grant columns are missing, unexpected, or incompatible.', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.key_constraints AS keyConstraint
            INNER JOIN sys.indexes AS indexInfo
                ON indexInfo.[object_id] = keyConstraint.[parent_object_id]
               AND indexInfo.[index_id] = keyConstraint.[unique_index_id]
            INNER JOIN sys.index_columns AS keyColumn
                ON keyColumn.[object_id] = indexInfo.[object_id]
               AND keyColumn.[index_id] = indexInfo.[index_id]
               AND keyColumn.[key_ordinal] = 1
            INNER JOIN sys.columns AS columnInfo
                ON columnInfo.[object_id] = keyColumn.[object_id]
               AND columnInfo.[column_id] = keyColumn.[column_id]
            WHERE keyConstraint.[parent_object_id] = @DeviceActivationTableId
              AND keyConstraint.[name] = N'PK_POSM_DeviceActivationGrant'
              AND keyConstraint.[type] = N'PK'
              AND indexInfo.[is_primary_key] = 1
              AND indexInfo.[is_unique] = 1
              AND indexInfo.[type] = 1
              AND indexInfo.[is_disabled] = 0
              AND indexInfo.[is_hypothetical] = 0
              AND indexInfo.[has_filter] = 0
              AND indexInfo.[filter_definition] IS NULL
              AND keyColumn.[is_descending_key] = 0
              AND columnInfo.[name] = N'GrantId'
              AND (SELECT COUNT(1)
                   FROM sys.index_columns AS allKeyColumns
                   WHERE allKeyColumns.[object_id] = indexInfo.[object_id]
                     AND allKeyColumns.[index_id] = indexInfo.[index_id]
                     AND allKeyColumns.[key_ordinal] > 0) = 1
              AND (SELECT COUNT(1)
                   FROM sys.index_columns AS includedColumn
                   WHERE includedColumn.[object_id] = indexInfo.[object_id]
                     AND includedColumn.[index_id] = indexInfo.[index_id]
                     AND includedColumn.[is_included_column] = 1) = 0)
            THROW 51103, 'Device activation primary key is missing or incompatible.', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes AS indexInfo
            INNER JOIN sys.index_columns AS keyColumn
                ON keyColumn.[object_id] = indexInfo.[object_id]
               AND keyColumn.[index_id] = indexInfo.[index_id]
               AND keyColumn.[key_ordinal] = 1
            INNER JOIN sys.columns AS columnInfo
                ON columnInfo.[object_id] = keyColumn.[object_id]
               AND columnInfo.[column_id] = keyColumn.[column_id]
            WHERE indexInfo.[object_id] = @DeviceActivationTableId
              AND indexInfo.[name] = N'UX_POSM_DeviceActivationGrant_SecretHash'
              AND indexInfo.[type] = 2
              AND indexInfo.[is_unique] = 1
              AND indexInfo.[is_primary_key] = 0
              AND indexInfo.[is_unique_constraint] = 0
              AND indexInfo.[is_disabled] = 0
              AND indexInfo.[is_hypothetical] = 0
              AND indexInfo.[has_filter] = 0
              AND indexInfo.[filter_definition] IS NULL
              AND keyColumn.[is_descending_key] = 0
              AND columnInfo.[name] = N'SecretHash'
              AND (SELECT COUNT(1)
                   FROM sys.index_columns AS allKeyColumns
                   WHERE allKeyColumns.[object_id] = indexInfo.[object_id]
                     AND allKeyColumns.[index_id] = indexInfo.[index_id]
                     AND allKeyColumns.[key_ordinal] > 0) = 1
              AND (SELECT COUNT(1)
                   FROM sys.index_columns AS includedColumn
                   WHERE includedColumn.[object_id] = indexInfo.[object_id]
                     AND includedColumn.[index_id] = indexInfo.[index_id]
                     AND includedColumn.[is_included_column] = 1) = 0)
            THROW 51104, 'Device activation secret hash unique index is missing or incompatible.', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes AS indexInfo
            WHERE indexInfo.[object_id] = @DeviceActivationTableId
              AND indexInfo.[name] = N'IX_POSM_DeviceActivationGrant_StoreCreated'
              AND indexInfo.[type] = 2
              AND indexInfo.[is_unique] = 0
              AND indexInfo.[is_primary_key] = 0
              AND indexInfo.[is_unique_constraint] = 0
              AND indexInfo.[is_disabled] = 0
              AND indexInfo.[is_hypothetical] = 0
              AND indexInfo.[has_filter] = 0
              AND indexInfo.[filter_definition] IS NULL
              AND (SELECT COUNT(1)
                   FROM sys.index_columns AS keyColumn
                   WHERE keyColumn.[object_id] = indexInfo.[object_id]
                     AND keyColumn.[index_id] = indexInfo.[index_id]
                     AND keyColumn.[key_ordinal] > 0) = 2
              AND NOT EXISTS (
                  SELECT expected.[Ordinal], expected.[ColumnName], expected.[IsDescending]
                  FROM (VALUES
                      (1, N'StoreCode', CAST(0 AS bit)),
                      (2, N'CreatedAtUtc', CAST(1 AS bit)))
                      AS expected([Ordinal], [ColumnName], [IsDescending])
                  LEFT JOIN sys.index_columns AS keyColumn
                      ON keyColumn.[object_id] = indexInfo.[object_id]
                     AND keyColumn.[index_id] = indexInfo.[index_id]
                     AND keyColumn.[key_ordinal] = expected.[Ordinal]
                     AND keyColumn.[is_descending_key] = expected.[IsDescending]
                  LEFT JOIN sys.columns AS columnInfo
                      ON columnInfo.[object_id] = keyColumn.[object_id]
                     AND columnInfo.[column_id] = keyColumn.[column_id]
                     AND columnInfo.[name] = expected.[ColumnName]
                  WHERE columnInfo.[column_id] IS NULL)
              AND (SELECT COUNT(1)
                   FROM sys.index_columns AS includedColumn
                   WHERE includedColumn.[object_id] = indexInfo.[object_id]
                     AND includedColumn.[index_id] = indexInfo.[index_id]
                     AND includedColumn.[is_included_column] = 1) = 4
              AND NOT EXISTS (
                  SELECT expected.[ColumnName]
                  FROM (VALUES
                      (N'DeviceSystem'),
                      (N'ExpiresAtUtc'),
                      (N'RevokedAtUtc'),
                      (N'ConsumedAtUtc')) AS expected([ColumnName])
                  WHERE NOT EXISTS (
                      SELECT 1
                      FROM sys.index_columns AS includedColumn
                      INNER JOIN sys.columns AS columnInfo
                          ON columnInfo.[object_id] = includedColumn.[object_id]
                         AND columnInfo.[column_id] = includedColumn.[column_id]
                      WHERE includedColumn.[object_id] = indexInfo.[object_id]
                        AND includedColumn.[index_id] = indexInfo.[index_id]
                        AND includedColumn.[is_included_column] = 1
                        AND columnInfo.[name] = expected.[ColumnName])))
            THROW 51105, 'Device activation store-created index is missing or incompatible.', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes AS indexInfo
            WHERE indexInfo.[object_id] = @DeviceActivationTableId
              AND indexInfo.[name] = N'IX_POSM_DeviceActivationGrant_Usable'
              AND indexInfo.[type] = 2
              AND indexInfo.[is_unique] = 0
              AND indexInfo.[is_primary_key] = 0
              AND indexInfo.[is_unique_constraint] = 0
              AND indexInfo.[is_disabled] = 0
              AND indexInfo.[is_hypothetical] = 0
              AND indexInfo.[has_filter] = 1
              AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    indexInfo.[filter_definition],
                    N' ', N''), N'[', N''), N']', N''), N'(', N''), N')', N''),
                    CHAR(9), N''), CHAR(10), N''), CHAR(13), N''))
                  = N'REVOKEDATUTCISNULLANDCONSUMEDATUTCISNULL'
              AND (SELECT COUNT(1)
                   FROM sys.index_columns AS keyColumn
                   WHERE keyColumn.[object_id] = indexInfo.[object_id]
                     AND keyColumn.[index_id] = indexInfo.[index_id]
                     AND keyColumn.[key_ordinal] > 0) = 3
              AND NOT EXISTS (
                  SELECT expected.[Ordinal], expected.[ColumnName]
                  FROM (VALUES
                      (1, N'StoreCode'),
                      (2, N'DeviceSystem'),
                      (3, N'ExpiresAtUtc')) AS expected([Ordinal], [ColumnName])
                  LEFT JOIN sys.index_columns AS keyColumn
                      ON keyColumn.[object_id] = indexInfo.[object_id]
                     AND keyColumn.[index_id] = indexInfo.[index_id]
                     AND keyColumn.[key_ordinal] = expected.[Ordinal]
                     AND keyColumn.[is_descending_key] = 0
                  LEFT JOIN sys.columns AS columnInfo
                      ON columnInfo.[object_id] = keyColumn.[object_id]
                     AND columnInfo.[column_id] = keyColumn.[column_id]
                     AND columnInfo.[name] = expected.[ColumnName]
                  WHERE columnInfo.[column_id] IS NULL)
              AND (SELECT COUNT(1)
                   FROM sys.index_columns AS includedColumn
                   WHERE includedColumn.[object_id] = indexInfo.[object_id]
                     AND includedColumn.[index_id] = indexInfo.[index_id]
                     AND includedColumn.[is_included_column] = 1) = 2
              AND NOT EXISTS (
                  SELECT expected.[ColumnName]
                  FROM (VALUES
                      (N'GrantId'),
                      (N'SecretHash')) AS expected([ColumnName])
                  WHERE NOT EXISTS (
                      SELECT 1
                      FROM sys.index_columns AS includedColumn
                      INNER JOIN sys.columns AS columnInfo
                          ON columnInfo.[object_id] = includedColumn.[object_id]
                         AND columnInfo.[column_id] = includedColumn.[column_id]
                      WHERE includedColumn.[object_id] = indexInfo.[object_id]
                        AND includedColumn.[index_id] = indexInfo.[index_id]
                        AND includedColumn.[is_included_column] = 1
                        AND columnInfo.[name] = expected.[ColumnName])))
            THROW 51106, 'Device activation usable index is missing or incompatible.', 1;

        IF (SELECT COUNT(1)
            FROM sys.check_constraints AS checkInfo
            WHERE checkInfo.[parent_object_id] = @DeviceActivationTableId) <> 4
            THROW 51107, 'Device activation check constraint set is incompatible.', 1;

        DECLARE @ExpectedExpiryDefinition nvarchar(max) =
            N'(EXPIRESATUTC>CREATEDATUTC)';
        DECLARE @ExpectedRevocationDefinition nvarchar(max) =
            N'(REVOKEDATUTCISNULLANDREVOKEDBYISNULLANDREVOKEREASONISNULLORREVOKEDATUTCISNOTNULLANDREVOKEDBYISNOTNULLANDREVOKEREASONISNOTNULL)';
        DECLARE @ExpectedConsumptionPrefix nvarchar(max) = CONCAT(
            N'(CONSUMEDATUTCISNULLANDCONSUMEDHARDWAREIDISNULLANDCONSUMEDDEVICECODEISNULL',
            N'ANDCONSUMEDDEVICEREGISTRATIONIDISNULLANDCONSUMEDAUTHORIZATIONHASHISNULL',
            N'ANDCONSUMEDDEVICESYSTEMISNULLANDCONSUMPTIONKINDISNULL',
            N'ANDPREVIOUSSTORECODEISNULLANDPREVIOUSDEVICECODEISNULL',
            N'ORCONSUMEDATUTCISNOTNULLANDCONSUMEDHARDWAREIDISNOTNULL',
            N'ANDCONSUMEDDEVICECODEISNOTNULLANDCONSUMEDDEVICEREGISTRATIONIDISNOTNULL',
            N'ANDCONSUMEDAUTHORIZATIONHASHISNOTNULLANDCONSUMEDDEVICESYSTEMISNOTNULL',
            N'ANDCONSUMPTIONKINDISNOTNULLAND');
        DECLARE @ExpectedConsumptionSuffix nvarchar(max) = CONCAT(
            N'AND(CONSUMPTIONKIND=''INITIAL''ANDPREVIOUSSTORECODEISNULLANDPREVIOUSDEVICECODEISNULL',
            N'ORCONSUMPTIONKIND=''REBIND''ANDPREVIOUSSTORECODEISNOTNULLANDPREVIOUSDEVICECODEISNOTNULL))');
        DECLARE @ExpectedConsumedDefinitionRebindFirst nvarchar(max) = CONCAT(
            @ExpectedConsumptionPrefix,
            N'(CONSUMPTIONKIND=''REBIND''ORCONSUMPTIONKIND=''INITIAL'')',
            @ExpectedConsumptionSuffix);
        DECLARE @ExpectedConsumedDefinitionInitialFirst nvarchar(max) = CONCAT(
            @ExpectedConsumptionPrefix,
            N'(CONSUMPTIONKIND=''INITIAL''ORCONSUMPTIONKIND=''REBIND'')',
            @ExpectedConsumptionSuffix);
        DECLARE @ExpectedConsumedDefinitionInInitialFirst nvarchar(max) = CONCAT(
            @ExpectedConsumptionPrefix,
            N'CONSUMPTIONKINDIN(''INITIAL'',''REBIND'')',
            @ExpectedConsumptionSuffix);
        DECLARE @ExpectedConsumedDefinitionInRebindFirst nvarchar(max) = CONCAT(
            @ExpectedConsumptionPrefix,
            N'CONSUMPTIONKINDIN(''REBIND'',''INITIAL'')',
            @ExpectedConsumptionSuffix);
        DECLARE @ExpectedExclusiveDefinition nvarchar(max) =
            N'(REVOKEDATUTCISNULLORCONSUMEDATUTCISNULL)';

        IF EXISTS (
            SELECT required.[ConstraintName]
            FROM (VALUES
                (N'CK_POSM_DeviceActivationGrant_Expiry'),
                (N'CK_POSM_DeviceActivationGrant_Revocation'),
                (N'CK_POSM_DeviceActivationGrant_Consumption'),
                (N'CK_POSM_DeviceActivationGrant_RevokedConsumedExclusive'))
                AS required([ConstraintName])
            LEFT JOIN sys.check_constraints AS checkInfo
                ON checkInfo.[parent_object_id] = @DeviceActivationTableId
               AND checkInfo.[name] = required.[ConstraintName]
               AND checkInfo.[is_disabled] = 0
               AND checkInfo.[is_not_trusted] = 0
            OUTER APPLY (
                SELECT REPLACE(REPLACE(REPLACE(REPLACE(
                    UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        checkInfo.[definition],
                        N' ', N''), N'[', N''), N']', N''), CHAR(9), N''), CHAR(10), N'')),
                    CHAR(13), N''),
                    N'N''INITIAL''', N'''INITIAL'''),
                    N'N''REBIND''', N'''REBIND'''),
                    N'(''INITIAL'')', N'''INITIAL''')
                    AS [WithoutRebindLiteralParentheses]) AS firstNormalization
            OUTER APPLY (
                SELECT REPLACE(
                    firstNormalization.[WithoutRebindLiteralParentheses],
                    N'(''REBIND'')', N'''REBIND''') AS [NormalizedDefinition]) AS normalized
            LEFT JOIN (VALUES
                (N'CK_POSM_DeviceActivationGrant_Expiry', @ExpectedExpiryDefinition),
                (N'CK_POSM_DeviceActivationGrant_Revocation', @ExpectedRevocationDefinition),
                (N'CK_POSM_DeviceActivationGrant_Consumption', @ExpectedConsumedDefinitionRebindFirst),
                (N'CK_POSM_DeviceActivationGrant_Consumption', @ExpectedConsumedDefinitionInitialFirst),
                (N'CK_POSM_DeviceActivationGrant_Consumption', @ExpectedConsumedDefinitionInInitialFirst),
                (N'CK_POSM_DeviceActivationGrant_Consumption', @ExpectedConsumedDefinitionInRebindFirst),
                (N'CK_POSM_DeviceActivationGrant_RevokedConsumedExclusive', @ExpectedExclusiveDefinition))
                AS expectedDefinition([ConstraintName], [NormalizedDefinition])
                ON expectedDefinition.[ConstraintName] = required.[ConstraintName]
               AND expectedDefinition.[NormalizedDefinition] = normalized.[NormalizedDefinition]
            WHERE checkInfo.[object_id] IS NULL
               OR expectedDefinition.[ConstraintName] IS NULL)
            THROW 51107, 'Device activation check constraints are missing, untrusted, or incompatible.', 1;
        """;

    public const string EnsureSql = """
        SET XACT_ABORT ON;
        BEGIN TRY
            BEGIN TRANSACTION;

            DECLARE @DeviceActivationSchemaLockResult int;
            EXEC @DeviceActivationSchemaLockResult = sys.sp_getapplock
                @Resource = N'HBPOS:Schema:DeviceActivationGrant',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 30000;
            IF @DeviceActivationSchemaLockResult < 0
                THROW 51001, 'Could not acquire device activation schema lock.', 1;

            DECLARE @DeviceActivationTableWasCreated bit = 0;
            IF OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[POSM_DeviceActivationGrant]
                (
                    [GrantId] UNIQUEIDENTIFIER NOT NULL,
                    [SecretHash] BINARY(32) NOT NULL,
                    [StoreCode] VARCHAR(50) NOT NULL,
                    [DeviceSystem] VARCHAR(20) NOT NULL,
                    [CreatedAtUtc] DATETIME2(7) NOT NULL,
                    [CreatedBy] NVARCHAR(128) NOT NULL,
                    [Reason] NVARCHAR(200) NOT NULL,
                    [ExpiresAtUtc] DATETIME2(7) NOT NULL,
                    [RevokedAtUtc] DATETIME2(7) NULL,
                    [RevokedBy] NVARCHAR(128) NULL,
                    [RevokeReason] NVARCHAR(200) NULL,
                    [ConsumedAtUtc] DATETIME2(7) NULL,
                    [ConsumedHardwareId] VARCHAR(100) NULL,
                    [ConsumedDeviceCode] VARCHAR(50) NULL,
                    [ConsumedDeviceRegistrationId] INT NULL,
                    [ConsumedAuthorizationHash] BINARY(32) NULL,
                    [ConsumedDeviceSystem] VARCHAR(20) NULL,
                    [ConsumptionKind] VARCHAR(10) NULL,
                    [PreviousStoreCode] VARCHAR(50) NULL,
                    [PreviousDeviceCode] VARCHAR(50) NULL,
                    [RowVersion] ROWVERSION NOT NULL,
                    CONSTRAINT [PK_POSM_DeviceActivationGrant] PRIMARY KEY ([GrantId]),
                    CONSTRAINT [CK_POSM_DeviceActivationGrant_Expiry] CHECK ([ExpiresAtUtc] > [CreatedAtUtc]),
                    CONSTRAINT [CK_POSM_DeviceActivationGrant_Revocation] CHECK
                    (
                        ([RevokedAtUtc] IS NULL AND [RevokedBy] IS NULL AND [RevokeReason] IS NULL)
                        OR
                        ([RevokedAtUtc] IS NOT NULL AND [RevokedBy] IS NOT NULL AND [RevokeReason] IS NOT NULL)
                    ),
                    CONSTRAINT [CK_POSM_DeviceActivationGrant_Consumption] CHECK
                    (
                        ([ConsumedAtUtc] IS NULL
                            AND [ConsumedHardwareId] IS NULL
                            AND [ConsumedDeviceCode] IS NULL
                            AND [ConsumedDeviceRegistrationId] IS NULL
                            AND [ConsumedAuthorizationHash] IS NULL
                            AND [ConsumedDeviceSystem] IS NULL
                            AND [ConsumptionKind] IS NULL
                            AND [PreviousStoreCode] IS NULL
                            AND [PreviousDeviceCode] IS NULL)
                        OR
                        ([ConsumedAtUtc] IS NOT NULL
                            AND [ConsumedHardwareId] IS NOT NULL
                            AND [ConsumedDeviceCode] IS NOT NULL
                            AND [ConsumedDeviceRegistrationId] IS NOT NULL
                            AND [ConsumedAuthorizationHash] IS NOT NULL
                            AND [ConsumedDeviceSystem] IS NOT NULL
                            -- CHECK 会接受 UNKNOWN，必须先显式排除 NULL 再校验枚举值。
                            AND [ConsumptionKind] IS NOT NULL
                            AND [ConsumptionKind] IN ('Initial', 'Rebind')
                            AND (([ConsumptionKind] = 'Initial'
                                    AND [PreviousStoreCode] IS NULL
                                    AND [PreviousDeviceCode] IS NULL)
                                OR ([ConsumptionKind] = 'Rebind'
                                    AND [PreviousStoreCode] IS NOT NULL
                                    AND [PreviousDeviceCode] IS NOT NULL)))
                    ),
                    CONSTRAINT [CK_POSM_DeviceActivationGrant_RevokedConsumedExclusive]
                        CHECK ([RevokedAtUtc] IS NULL OR [ConsumedAtUtc] IS NULL)
                );
                SET @DeviceActivationTableWasCreated = 1;
            END;

            IF @DeviceActivationTableWasCreated = 1
            BEGIN
                CREATE UNIQUE NONCLUSTERED INDEX [UX_POSM_DeviceActivationGrant_SecretHash]
                    ON [dbo].[POSM_DeviceActivationGrant] ([SecretHash]);
                CREATE NONCLUSTERED INDEX [IX_POSM_DeviceActivationGrant_StoreCreated]
                    ON [dbo].[POSM_DeviceActivationGrant] ([StoreCode], [CreatedAtUtc] DESC)
                    INCLUDE ([DeviceSystem], [ExpiresAtUtc], [RevokedAtUtc], [ConsumedAtUtc]);
                CREATE NONCLUSTERED INDEX [IX_POSM_DeviceActivationGrant_Usable]
                    ON [dbo].[POSM_DeviceActivationGrant] ([StoreCode], [DeviceSystem], [ExpiresAtUtc])
                    INCLUDE ([GrantId], [SecretHash])
                    WHERE [RevokedAtUtc] IS NULL AND [ConsumedAtUtc] IS NULL;
            END;

            -- 已有表不做静默 ALTER 或历史回填；任何关键结构漂移都在启动时失败关闭。
            IF COL_LENGTH(N'dbo.POSM_DeviceActivationGrant', N'ActivationCode') IS NOT NULL
                THROW 51002, 'Device activation grant table must not store plaintext activation codes.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM sys.columns AS columnInfo
                WHERE columnInfo.[object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
                  AND columnInfo.[name] = N'GrantId'
                  AND TYPE_NAME(columnInfo.[system_type_id]) = N'uniqueidentifier'
                  AND columnInfo.[is_nullable] = 0)
                THROW 51003, 'Device activation GrantId column is missing or incompatible.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM sys.columns AS columnInfo
                WHERE columnInfo.[object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
                  AND columnInfo.[name] = N'SecretHash'
                  AND TYPE_NAME(columnInfo.[system_type_id]) = N'binary'
                  AND columnInfo.[max_length] = 32
                  AND columnInfo.[is_nullable] = 0)
                THROW 51004, 'Device activation SecretHash column is missing or incompatible.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM sys.columns AS columnInfo
                WHERE columnInfo.[object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
                  AND columnInfo.[name] = N'ConsumedDeviceRegistrationId'
                  AND TYPE_NAME(columnInfo.[system_type_id]) = N'int'
                  AND columnInfo.[is_nullable] = 1)
                THROW 51005, 'Device activation consumption registration column is missing or incompatible.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM sys.columns AS columnInfo
                WHERE columnInfo.[object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
                  AND columnInfo.[name] = N'RowVersion'
                  AND TYPE_NAME(columnInfo.[system_type_id]) = N'timestamp'
                  AND columnInfo.[is_nullable] = 0)
                THROW 51006, 'Device activation rowversion column is missing or incompatible.', 1;

            IF EXISTS (
                SELECT expected.[ColumnName]
                FROM (VALUES
                    (N'StoreCode', N'varchar', CAST(50 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)),
                    (N'DeviceSystem', N'varchar', CAST(20 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)),
                    (N'CreatedAtUtc', N'datetime2', CAST(8 AS smallint), CAST(0 AS bit), CAST(7 AS tinyint)),
                    (N'CreatedBy', N'nvarchar', CAST(256 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)),
                    (N'Reason', N'nvarchar', CAST(400 AS smallint), CAST(0 AS bit), CAST(NULL AS tinyint)),
                    (N'ExpiresAtUtc', N'datetime2', CAST(8 AS smallint), CAST(0 AS bit), CAST(7 AS tinyint)),
                    (N'RevokedAtUtc', N'datetime2', CAST(8 AS smallint), CAST(1 AS bit), CAST(7 AS tinyint)),
                    (N'RevokedBy', N'nvarchar', CAST(256 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                    (N'RevokeReason', N'nvarchar', CAST(400 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                    (N'ConsumedAtUtc', N'datetime2', CAST(8 AS smallint), CAST(1 AS bit), CAST(7 AS tinyint)),
                    (N'ConsumedHardwareId', N'varchar', CAST(100 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                    (N'ConsumedDeviceCode', N'varchar', CAST(50 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                    (N'ConsumedAuthorizationHash', N'binary', CAST(32 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                    (N'ConsumedDeviceSystem', N'varchar', CAST(20 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                    (N'ConsumptionKind', N'varchar', CAST(10 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                    (N'PreviousStoreCode', N'varchar', CAST(50 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)),
                    (N'PreviousDeviceCode', N'varchar', CAST(50 AS smallint), CAST(1 AS bit), CAST(NULL AS tinyint)))
                    AS expected([ColumnName], [TypeName], [MaxLength], [IsNullable], [Scale])
                LEFT JOIN sys.columns AS columnInfo
                    ON columnInfo.[object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
                   AND columnInfo.[name] = expected.[ColumnName]
                LEFT JOIN sys.types AS typeInfo
                    ON typeInfo.[user_type_id] = columnInfo.[user_type_id]
                WHERE columnInfo.[column_id] IS NULL
                   OR typeInfo.[name] <> expected.[TypeName]
                   OR columnInfo.[max_length] <> expected.[MaxLength]
                   OR columnInfo.[is_nullable] <> expected.[IsNullable]
                   OR (expected.[Scale] IS NOT NULL AND columnInfo.[scale] <> expected.[Scale]))
                THROW 51007, 'Device activation grant columns are missing or incompatible.', 1;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.key_constraints AS keyConstraint
                INNER JOIN sys.indexes AS indexInfo
                    ON indexInfo.[object_id] = keyConstraint.[parent_object_id]
                   AND indexInfo.[index_id] = keyConstraint.[unique_index_id]
                INNER JOIN sys.index_columns AS keyColumn
                    ON keyColumn.[object_id] = keyConstraint.[parent_object_id]
                   AND keyColumn.[index_id] = keyConstraint.[unique_index_id]
                   AND keyColumn.[key_ordinal] = 1
                INNER JOIN sys.columns AS columnInfo
                    ON columnInfo.[object_id] = keyColumn.[object_id]
                   AND columnInfo.[column_id] = keyColumn.[column_id]
                WHERE keyConstraint.[parent_object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
                  AND keyConstraint.[type] = N'PK'
                  AND indexInfo.[is_disabled] = 0
                  AND columnInfo.[name] = N'GrantId'
                  AND (SELECT COUNT(1)
                       FROM sys.index_columns AS allKeyColumns
                       WHERE allKeyColumns.[object_id] = keyConstraint.[parent_object_id]
                         AND allKeyColumns.[index_id] = keyConstraint.[unique_index_id]
                         AND allKeyColumns.[key_ordinal] > 0) = 1)
                THROW 51008, 'Device activation primary key is missing or incompatible.', 1;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes AS indexInfo
                INNER JOIN sys.index_columns AS keyColumn
                    ON keyColumn.[object_id] = indexInfo.[object_id]
                   AND keyColumn.[index_id] = indexInfo.[index_id]
                   AND keyColumn.[key_ordinal] = 1
                INNER JOIN sys.columns AS columnInfo
                    ON columnInfo.[object_id] = keyColumn.[object_id]
                   AND columnInfo.[column_id] = keyColumn.[column_id]
                WHERE indexInfo.[object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
                  AND indexInfo.[name] = N'UX_POSM_DeviceActivationGrant_SecretHash'
                  AND indexInfo.[is_unique] = 1
                  AND indexInfo.[is_disabled] = 0
                  AND indexInfo.[has_filter] = 0
                  AND columnInfo.[name] = N'SecretHash'
                  AND (SELECT COUNT(1)
                       FROM sys.index_columns AS allColumns
                       WHERE allColumns.[object_id] = indexInfo.[object_id]
                         AND allColumns.[index_id] = indexInfo.[index_id]
                         AND (allColumns.[key_ordinal] > 0 OR allColumns.[is_included_column] = 1)) = 1)
                THROW 51009, 'Device activation secret hash unique index is missing or incompatible.', 1;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes AS indexInfo
                WHERE indexInfo.[object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
                  AND indexInfo.[name] = N'IX_POSM_DeviceActivationGrant_StoreCreated'
                  AND indexInfo.[is_disabled] = 0
                  AND indexInfo.[has_filter] = 0
                  AND (SELECT COUNT(1) FROM sys.index_columns AS keys
                       WHERE keys.[object_id] = indexInfo.[object_id]
                         AND keys.[index_id] = indexInfo.[index_id]
                         AND keys.[key_ordinal] > 0) = 2
                  AND EXISTS (
                      SELECT 1 FROM sys.index_columns AS keyColumn
                      INNER JOIN sys.columns AS columnInfo
                          ON columnInfo.[object_id] = keyColumn.[object_id]
                         AND columnInfo.[column_id] = keyColumn.[column_id]
                      WHERE keyColumn.[object_id] = indexInfo.[object_id]
                        AND keyColumn.[index_id] = indexInfo.[index_id]
                        AND keyColumn.[key_ordinal] = 1
                        AND keyColumn.[is_descending_key] = 0
                        AND columnInfo.[name] = N'StoreCode')
                  AND EXISTS (
                      SELECT 1 FROM sys.index_columns AS keyColumn
                      INNER JOIN sys.columns AS columnInfo
                          ON columnInfo.[object_id] = keyColumn.[object_id]
                         AND columnInfo.[column_id] = keyColumn.[column_id]
                      WHERE keyColumn.[object_id] = indexInfo.[object_id]
                        AND keyColumn.[index_id] = indexInfo.[index_id]
                        AND keyColumn.[key_ordinal] = 2
                        AND keyColumn.[is_descending_key] = 1
                        AND columnInfo.[name] = N'CreatedAtUtc')
                  AND (SELECT COUNT(1) FROM sys.index_columns AS included
                       WHERE included.[object_id] = indexInfo.[object_id]
                         AND included.[index_id] = indexInfo.[index_id]
                         AND included.[is_included_column] = 1) = 4
                  AND NOT EXISTS (
                      SELECT 1 FROM sys.index_columns AS included
                      INNER JOIN sys.columns AS columnInfo
                          ON columnInfo.[object_id] = included.[object_id]
                         AND columnInfo.[column_id] = included.[column_id]
                      WHERE included.[object_id] = indexInfo.[object_id]
                        AND included.[index_id] = indexInfo.[index_id]
                        AND included.[is_included_column] = 1
                        AND columnInfo.[name] NOT IN
                            (N'DeviceSystem', N'ExpiresAtUtc', N'RevokedAtUtc', N'ConsumedAtUtc')))
                THROW 51010, 'Device activation store-created index is missing or incompatible.', 1;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes AS indexInfo
                WHERE indexInfo.[object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
                  AND indexInfo.[name] = N'IX_POSM_DeviceActivationGrant_Usable'
                  AND indexInfo.[is_disabled] = 0
                  AND indexInfo.[has_filter] = 1
                  AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        indexInfo.[filter_definition], N' ', N''), N'[', N''), N']', N''), N'(', N''), N')', N'')
                      = N'RevokedAtUtcISNULLANDConsumedAtUtcISNULL'
                  AND (SELECT COUNT(1) FROM sys.index_columns AS keys
                       WHERE keys.[object_id] = indexInfo.[object_id]
                         AND keys.[index_id] = indexInfo.[index_id]
                         AND keys.[key_ordinal] > 0) = 3
                  AND NOT EXISTS (
                      SELECT expected.[Ordinal], expected.[ColumnName]
                      FROM (VALUES
                          (1, N'StoreCode'),
                          (2, N'DeviceSystem'),
                          (3, N'ExpiresAtUtc')) AS expected([Ordinal], [ColumnName])
                      LEFT JOIN sys.index_columns AS keyColumn
                          ON keyColumn.[object_id] = indexInfo.[object_id]
                         AND keyColumn.[index_id] = indexInfo.[index_id]
                         AND keyColumn.[key_ordinal] = expected.[Ordinal]
                         AND keyColumn.[is_descending_key] = 0
                      LEFT JOIN sys.columns AS columnInfo
                          ON columnInfo.[object_id] = keyColumn.[object_id]
                         AND columnInfo.[column_id] = keyColumn.[column_id]
                         AND columnInfo.[name] = expected.[ColumnName]
                      WHERE columnInfo.[column_id] IS NULL)
                  AND (SELECT COUNT(1) FROM sys.index_columns AS included
                       WHERE included.[object_id] = indexInfo.[object_id]
                         AND included.[index_id] = indexInfo.[index_id]
                         AND included.[is_included_column] = 1) = 2
                  AND NOT EXISTS (
                      SELECT 1 FROM sys.index_columns AS included
                      INNER JOIN sys.columns AS columnInfo
                          ON columnInfo.[object_id] = included.[object_id]
                         AND columnInfo.[column_id] = included.[column_id]
                      WHERE included.[object_id] = indexInfo.[object_id]
                        AND included.[index_id] = indexInfo.[index_id]
                        AND included.[is_included_column] = 1
                        AND columnInfo.[name] NOT IN (N'GrantId', N'SecretHash')))
                THROW 51011, 'Device activation usable index is missing or incompatible.', 1;

            IF EXISTS (
                SELECT required.[ConstraintName]
                FROM (VALUES
                    (N'CK_POSM_DeviceActivationGrant_Expiry'),
                    (N'CK_POSM_DeviceActivationGrant_Revocation'),
                    (N'CK_POSM_DeviceActivationGrant_Consumption'),
                    (N'CK_POSM_DeviceActivationGrant_RevokedConsumedExclusive'))
                    AS required([ConstraintName])
                LEFT JOIN sys.check_constraints AS checkInfo
                    ON checkInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
                   AND checkInfo.[name] = required.[ConstraintName]
                   AND checkInfo.[is_disabled] = 0
                   AND checkInfo.[is_not_trusted] = 0
                WHERE checkInfo.[object_id] IS NULL
                   OR EXISTS (
                       SELECT expected.[ColumnName]
                       FROM (VALUES
                           (N'CK_POSM_DeviceActivationGrant_Expiry', N'ExpiresAtUtc'),
                           (N'CK_POSM_DeviceActivationGrant_Expiry', N'CreatedAtUtc'),
                           (N'CK_POSM_DeviceActivationGrant_Revocation', N'RevokedAtUtc'),
                           (N'CK_POSM_DeviceActivationGrant_Revocation', N'RevokedBy'),
                           (N'CK_POSM_DeviceActivationGrant_Revocation', N'RevokeReason'),
                           (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedAtUtc'),
                           (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedHardwareId'),
                           (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedDeviceCode'),
                           (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedDeviceRegistrationId'),
                           (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedAuthorizationHash'),
                           (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedDeviceSystem'),
                           (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumptionKind'),
                           (N'CK_POSM_DeviceActivationGrant_Consumption', N'PreviousStoreCode'),
                           (N'CK_POSM_DeviceActivationGrant_Consumption', N'PreviousDeviceCode'),
                           (N'CK_POSM_DeviceActivationGrant_RevokedConsumedExclusive', N'RevokedAtUtc'),
                           (N'CK_POSM_DeviceActivationGrant_RevokedConsumedExclusive', N'ConsumedAtUtc'))
                           AS expected([ConstraintName], [ColumnName])
                       WHERE expected.[ConstraintName] = required.[ConstraintName]
                         AND NOT EXISTS (
                             SELECT 1
                             WHERE PATINDEX(
                                 N'%[^0-9A-Za-z_]' + expected.[ColumnName] + N'[^0-9A-Za-z_]%',
                                 N' ' + checkInfo.[definition] + N' ') > 0))
                   OR EXISTS (
                       SELECT 1
                       FROM sys.columns AS referencedColumn
                       WHERE referencedColumn.[object_id] = checkInfo.[parent_object_id]
                         AND PATINDEX(
                             N'%[^0-9A-Za-z_]' + referencedColumn.[name] + N'[^0-9A-Za-z_]%',
                             N' ' + checkInfo.[definition] + N' ') > 0
                         AND NOT EXISTS (
                             SELECT 1
                             FROM (VALUES
                                 (N'CK_POSM_DeviceActivationGrant_Expiry', N'ExpiresAtUtc'),
                                 (N'CK_POSM_DeviceActivationGrant_Expiry', N'CreatedAtUtc'),
                                 (N'CK_POSM_DeviceActivationGrant_Revocation', N'RevokedAtUtc'),
                                 (N'CK_POSM_DeviceActivationGrant_Revocation', N'RevokedBy'),
                                 (N'CK_POSM_DeviceActivationGrant_Revocation', N'RevokeReason'),
                                 (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedAtUtc'),
                                 (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedHardwareId'),
                                 (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedDeviceCode'),
                                 (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedDeviceRegistrationId'),
                                 (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedAuthorizationHash'),
                                 (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumedDeviceSystem'),
                                 (N'CK_POSM_DeviceActivationGrant_Consumption', N'ConsumptionKind'),
                                 (N'CK_POSM_DeviceActivationGrant_Consumption', N'PreviousStoreCode'),
                                 (N'CK_POSM_DeviceActivationGrant_Consumption', N'PreviousDeviceCode'),
                                 (N'CK_POSM_DeviceActivationGrant_RevokedConsumedExclusive', N'RevokedAtUtc'),
                                 (N'CK_POSM_DeviceActivationGrant_RevokedConsumedExclusive', N'ConsumedAtUtc'))
                                 AS expected([ConstraintName], [ColumnName])
                             WHERE expected.[ConstraintName] = required.[ConstraintName]
                               AND expected.[ColumnName] = referencedColumn.[name])))
                THROW 51012, 'Device activation check constraints are missing or incompatible.', 1;

            -- SQL Server 会重写 CHECK 表达式文本，因此不能通过字符串等值判断约束语义。
            -- 逐字段覆盖所有非法空值组合；每次在保存点内回滚，启动校验不会留下探针数据。
            DECLARE @DeviceActivationProbeCases TABLE
            (
                [ProbeCase] tinyint NOT NULL PRIMARY KEY,
                [ExpectedConstraint] sysname NOT NULL,
                [ExpiresOffsetMinutes] int NOT NULL,
                [HasRevokedAt] bit NOT NULL,
                [HasRevokedBy] bit NOT NULL,
                [HasRevokeReason] bit NOT NULL,
                [HasConsumedAt] bit NOT NULL,
                [HasConsumedHardware] bit NOT NULL,
                [HasConsumedDevice] bit NOT NULL,
                [HasConsumedRegistration] bit NOT NULL,
                [HasConsumedAuthorizationHash] bit NOT NULL,
                [HasConsumedSystem] bit NOT NULL,
                [ConsumptionKind] varchar(10) NULL,
                [HasPreviousStore] bit NOT NULL,
                [HasPreviousDevice] bit NOT NULL
            );
            INSERT INTO @DeviceActivationProbeCases VALUES
                (1,  N'CK_POSM_DeviceActivationGrant_Expiry', 0,  0,0,0, 0,0,0,0,0,0, NULL,      0,0),
                (2,  N'CK_POSM_DeviceActivationGrant_Revocation', 10, 1,0,0, 0,0,0,0,0,0, NULL,    0,0),
                (3,  N'CK_POSM_DeviceActivationGrant_Revocation', 10, 0,1,0, 0,0,0,0,0,0, NULL,    0,0),
                (4,  N'CK_POSM_DeviceActivationGrant_Revocation', 10, 0,0,1, 0,0,0,0,0,0, NULL,    0,0),
                (5,  N'CK_POSM_DeviceActivationGrant_Revocation', 10, 1,1,0, 0,0,0,0,0,0, NULL,    0,0),
                (6,  N'CK_POSM_DeviceActivationGrant_Revocation', 10, 1,0,1, 0,0,0,0,0,0, NULL,    0,0),
                (7,  N'CK_POSM_DeviceActivationGrant_Revocation', 10, 0,1,1, 0,0,0,0,0,0, NULL,    0,0),
                (8,  N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 0,1,1,1,1,1, 'Initial', 0,0),
                (9,  N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,0,1,1,1,1, 'Initial', 0,0),
                (10, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,0,1,1,1, 'Initial', 0,0),
                (11, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,0,1,1, 'Initial', 0,0),
                (12, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,1,0,1, 'Initial', 0,0),
                (13, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,1,1,0, 'Initial', 0,0),
                (14, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,1,1,1, NULL,      0,0),
                (15, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,0,0,0,0,0, NULL,      0,0),
                (16, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 0,1,0,0,0,0, NULL,      0,0),
                (17, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 0,0,1,0,0,0, NULL,      0,0),
                (18, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 0,0,0,1,0,0, NULL,      0,0),
                (19, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 0,0,0,0,1,0, NULL,      0,0),
                (20, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 0,0,0,0,0,1, NULL,      0,0),
                (21, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 0,0,0,0,0,0, 'Initial', 0,0),
                (22, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,1,1,1, 'Other',   0,0),
                (23, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,1,1,1, 'Initial', 1,0),
                (24, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,1,1,1, 'Initial', 0,1),
                (25, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,1,1,1, 'Initial', 1,1),
                (26, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,1,1,1, 'Rebind',  0,0),
                (27, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,1,1,1, 'Rebind',  1,0),
                (28, N'CK_POSM_DeviceActivationGrant_Consumption', 10, 0,0,0, 1,1,1,1,1,1, 'Rebind',  0,1),
                (29, N'CK_POSM_DeviceActivationGrant_RevokedConsumedExclusive', 10, 1,1,1, 1,1,1,1,1,1, 'Initial', 0,0);

            SET XACT_ABORT OFF;
            DECLARE @DeviceActivationProbeCase tinyint = 1;
            WHILE @DeviceActivationProbeCase <= 29
            BEGIN
                DECLARE @DeviceActivationProbeRejected bit = 0;
                DECLARE @DeviceActivationProbeUnexpectedErrorNumber int = NULL;
                DECLARE @DeviceActivationProbeAt datetime2(7) = SYSUTCDATETIME();
                DECLARE @DeviceActivationProbeGrantId uniqueidentifier = NEWID();
                DECLARE @DeviceActivationExpectedConstraint sysname;
                DECLARE @DeviceActivationExpiresOffsetMinutes int;
                DECLARE @DeviceActivationHasRevokedAt bit;
                DECLARE @DeviceActivationHasRevokedBy bit;
                DECLARE @DeviceActivationHasRevokeReason bit;
                DECLARE @DeviceActivationHasConsumedAt bit;
                DECLARE @DeviceActivationHasConsumedHardware bit;
                DECLARE @DeviceActivationHasConsumedDevice bit;
                DECLARE @DeviceActivationHasConsumedRegistration bit;
                DECLARE @DeviceActivationHasConsumedAuthorizationHash bit;
                DECLARE @DeviceActivationHasConsumedSystem bit;
                DECLARE @DeviceActivationConsumptionKind varchar(10);
                DECLARE @DeviceActivationHasPreviousStore bit;
                DECLARE @DeviceActivationHasPreviousDevice bit;
                SELECT
                    @DeviceActivationExpectedConstraint = [ExpectedConstraint],
                    @DeviceActivationExpiresOffsetMinutes = [ExpiresOffsetMinutes],
                    @DeviceActivationHasRevokedAt = [HasRevokedAt],
                    @DeviceActivationHasRevokedBy = [HasRevokedBy],
                    @DeviceActivationHasRevokeReason = [HasRevokeReason],
                    @DeviceActivationHasConsumedAt = [HasConsumedAt],
                    @DeviceActivationHasConsumedHardware = [HasConsumedHardware],
                    @DeviceActivationHasConsumedDevice = [HasConsumedDevice],
                    @DeviceActivationHasConsumedRegistration = [HasConsumedRegistration],
                    @DeviceActivationHasConsumedAuthorizationHash = [HasConsumedAuthorizationHash],
                    @DeviceActivationHasConsumedSystem = [HasConsumedSystem],
                    @DeviceActivationConsumptionKind = [ConsumptionKind],
                    @DeviceActivationHasPreviousStore = [HasPreviousStore],
                    @DeviceActivationHasPreviousDevice = [HasPreviousDevice]
                FROM @DeviceActivationProbeCases
                WHERE [ProbeCase] = @DeviceActivationProbeCase;
                DECLARE @DeviceActivationExpectedConstraintPattern nvarchar(512) =
                    @DeviceActivationExpectedConstraint;
                SET @DeviceActivationExpectedConstraintPattern = REPLACE(@DeviceActivationExpectedConstraintPattern, N'[', N'[[]');
                SET @DeviceActivationExpectedConstraintPattern = REPLACE(@DeviceActivationExpectedConstraintPattern, N'%', N'[%]');
                SET @DeviceActivationExpectedConstraintPattern = REPLACE(@DeviceActivationExpectedConstraintPattern, N'_', N'[_]');

                SAVE TRANSACTION DeviceActivationCheckProbe;
                BEGIN TRY
                    INSERT INTO [dbo].[POSM_DeviceActivationGrant]
                    (
                        [GrantId],
                        [SecretHash],
                        [StoreCode],
                        [DeviceSystem],
                        [CreatedAtUtc],
                        [CreatedBy],
                        [Reason],
                        [ExpiresAtUtc],
                        [RevokedAtUtc],
                        [RevokedBy],
                        [RevokeReason],
                        [ConsumedAtUtc],
                        [ConsumedHardwareId],
                        [ConsumedDeviceCode],
                        [ConsumedDeviceRegistrationId],
                        [ConsumedAuthorizationHash],
                        [ConsumedDeviceSystem],
                        [ConsumptionKind],
                        [PreviousStoreCode],
                        [PreviousDeviceCode]
                    )
                    VALUES
                    (
                        @DeviceActivationProbeGrantId,
                        HASHBYTES('SHA2_256', CONVERT(varchar(36), @DeviceActivationProbeGrantId)),
                        '__SCHEMA_PROBE__',
                        'Windows',
                        @DeviceActivationProbeAt,
                        N'HBPOS_SCHEMA_PROBE',
                        N'CHECK constraint semantic validation',
                        DATEADD(minute, @DeviceActivationExpiresOffsetMinutes, @DeviceActivationProbeAt),
                        CASE WHEN @DeviceActivationHasRevokedAt = 1
                            THEN @DeviceActivationProbeAt ELSE NULL END,
                        CASE WHEN @DeviceActivationHasRevokedBy = 1
                            THEN N'HBPOS_SCHEMA_PROBE' ELSE NULL END,
                        CASE WHEN @DeviceActivationHasRevokeReason = 1
                            THEN N'CHECK constraint semantic validation' ELSE NULL END,
                        CASE WHEN @DeviceActivationHasConsumedAt = 1
                            THEN @DeviceActivationProbeAt ELSE NULL END,
                        CASE WHEN @DeviceActivationHasConsumedHardware = 1
                            THEN 'HW-SCHEMA-PROBE' ELSE NULL END,
                        CASE WHEN @DeviceActivationHasConsumedDevice = 1
                            THEN 'DEVICE-SCHEMA-PROBE' ELSE NULL END,
                        CASE WHEN @DeviceActivationHasConsumedRegistration = 1
                            THEN -2147480000 + @DeviceActivationProbeCase ELSE NULL END,
                        CASE WHEN @DeviceActivationHasConsumedAuthorizationHash = 1
                            THEN HASHBYTES('SHA2_256', CONVERT(varchar(36), NEWID())) ELSE NULL END,
                        CASE WHEN @DeviceActivationHasConsumedSystem = 1
                            THEN 'Windows' ELSE NULL END,
                        @DeviceActivationConsumptionKind,
                        CASE WHEN @DeviceActivationHasPreviousStore = 1
                            THEN 'PREVIOUS-SCHEMA-PROBE' ELSE NULL END,
                        CASE WHEN @DeviceActivationHasPreviousDevice = 1
                            THEN 'PREVIOUS-DEVICE-SCHEMA-PROBE' ELSE NULL END
                    );
                END TRY
                BEGIN CATCH
                    DECLARE @DeviceActivationProbeCaughtErrorNumber int = ERROR_NUMBER();
                    DECLARE @DeviceActivationProbeCaughtErrorMessage nvarchar(4000) = ERROR_MESSAGE();

                    -- 错误正文会随 SQL Server 语言变化；约束标识符本身不会本地化。
                    -- 用标识符边界匹配完整名称，避免其他 CHECK/FK 的 547 掩盖目标弱约束。
                    IF @DeviceActivationProbeCaughtErrorNumber = 547
                       AND PATINDEX(
                            N'%[^0-9A-Za-z_]' + @DeviceActivationExpectedConstraintPattern + N'[^0-9A-Za-z_]%',
                            N' ' + @DeviceActivationProbeCaughtErrorMessage + N' ') > 0
                        SET @DeviceActivationProbeRejected = 1;
                    ELSE
                        SET @DeviceActivationProbeUnexpectedErrorNumber =
                            @DeviceActivationProbeCaughtErrorNumber;
                END CATCH;

                IF XACT_STATE() <> 1
                BEGIN
                    SET XACT_ABORT ON;
                    THROW 51013, 'Device activation semantic probe left the schema transaction unusable.', 1;
                END;

                ROLLBACK TRANSACTION DeviceActivationCheckProbe;
                IF @DeviceActivationProbeUnexpectedErrorNumber IS NOT NULL
                BEGIN
                    DECLARE @DeviceActivationUnexpectedProbeMessage nvarchar(2048) = CONCAT(
                        N'Device activation semantic probe case ',
                        @DeviceActivationProbeCase,
                        N' hit unexpected database error ',
                        @DeviceActivationProbeUnexpectedErrorNumber,
                        N' instead of constraint ',
                        @DeviceActivationExpectedConstraint,
                        N'.');
                    SET XACT_ABORT ON;
                    THROW 51013, @DeviceActivationUnexpectedProbeMessage, 1;
                END;

                IF @DeviceActivationProbeRejected = 0
                BEGIN
                    DECLARE @DeviceActivationProbeMessage nvarchar(2048) = CONCAT(
                        N'Device activation check constraint semantic probe accepted invalid case ',
                        @DeviceActivationProbeCase,
                        N'.');
                    SET XACT_ABORT ON;
                    THROW 51013, @DeviceActivationProbeMessage, 1;
                END;

                SET @DeviceActivationProbeCase += 1;
            END;

            -- 负例只能证明约束不够宽；再用四种合法状态证明约束没有被收紧到破坏正常开通或重绑。
            DECLARE @DeviceActivationPositiveProbeCases TABLE
            (
                [ProbeCase] tinyint NOT NULL PRIMARY KEY,
                [HasRevocation] bit NOT NULL,
                [ConsumptionKind] varchar(10) NULL,
                [HasPreviousIdentity] bit NOT NULL
            );
            INSERT INTO @DeviceActivationPositiveProbeCases VALUES
                (1, 0, NULL,      0),
                (2, 1, NULL,      0),
                (3, 0, 'Initial', 0),
                (4, 0, 'Rebind',  1);

            DECLARE @DeviceActivationPositiveProbeCase tinyint = 1;
            WHILE @DeviceActivationPositiveProbeCase <= 4
            BEGIN
                DECLARE @DeviceActivationPositiveHasRevocation bit;
                DECLARE @DeviceActivationPositiveConsumptionKind varchar(10);
                DECLARE @DeviceActivationPositiveHasPreviousIdentity bit;
                DECLARE @DeviceActivationPositiveProbeAt datetime2(7) = SYSUTCDATETIME();
                DECLARE @DeviceActivationPositiveProbeGrantId uniqueidentifier = NEWID();
                DECLARE @DeviceActivationPositiveProbeErrorNumber int = NULL;
                DECLARE @DeviceActivationPositiveProbeErrorMessage nvarchar(4000) = NULL;
                SELECT
                    @DeviceActivationPositiveHasRevocation = [HasRevocation],
                    @DeviceActivationPositiveConsumptionKind = [ConsumptionKind],
                    @DeviceActivationPositiveHasPreviousIdentity = [HasPreviousIdentity]
                FROM @DeviceActivationPositiveProbeCases
                WHERE [ProbeCase] = @DeviceActivationPositiveProbeCase;

                SAVE TRANSACTION DeviceActivationPositiveProbe;
                BEGIN TRY
                    INSERT INTO [dbo].[POSM_DeviceActivationGrant]
                    (
                        [GrantId],
                        [SecretHash],
                        [StoreCode],
                        [DeviceSystem],
                        [CreatedAtUtc],
                        [CreatedBy],
                        [Reason],
                        [ExpiresAtUtc],
                        [RevokedAtUtc],
                        [RevokedBy],
                        [RevokeReason],
                        [ConsumedAtUtc],
                        [ConsumedHardwareId],
                        [ConsumedDeviceCode],
                        [ConsumedDeviceRegistrationId],
                        [ConsumedAuthorizationHash],
                        [ConsumedDeviceSystem],
                        [ConsumptionKind],
                        [PreviousStoreCode],
                        [PreviousDeviceCode]
                    )
                    VALUES
                    (
                        @DeviceActivationPositiveProbeGrantId,
                        HASHBYTES('SHA2_256', CONVERT(varchar(36), @DeviceActivationPositiveProbeGrantId)),
                        '__POSITIVE_SCHEMA_PROBE__',
                        'Windows',
                        @DeviceActivationPositiveProbeAt,
                        N'HBPOS_SCHEMA_PROBE',
                        N'Valid CHECK constraint semantic validation',
                        DATEADD(minute, 10, @DeviceActivationPositiveProbeAt),
                        CASE WHEN @DeviceActivationPositiveHasRevocation = 1
                            THEN @DeviceActivationPositiveProbeAt ELSE NULL END,
                        CASE WHEN @DeviceActivationPositiveHasRevocation = 1
                            THEN N'HBPOS_SCHEMA_PROBE' ELSE NULL END,
                        CASE WHEN @DeviceActivationPositiveHasRevocation = 1
                            THEN N'Valid revoked state' ELSE NULL END,
                        CASE WHEN @DeviceActivationPositiveConsumptionKind IS NOT NULL
                            THEN @DeviceActivationPositiveProbeAt ELSE NULL END,
                        CASE WHEN @DeviceActivationPositiveConsumptionKind IS NOT NULL
                            THEN 'HW-POSITIVE-SCHEMA-PROBE' ELSE NULL END,
                        CASE WHEN @DeviceActivationPositiveConsumptionKind IS NOT NULL
                            THEN 'DEVICE-POSITIVE-SCHEMA-PROBE' ELSE NULL END,
                        CASE WHEN @DeviceActivationPositiveConsumptionKind IS NOT NULL
                            THEN -2147470000 + @DeviceActivationPositiveProbeCase ELSE NULL END,
                        CASE WHEN @DeviceActivationPositiveConsumptionKind IS NOT NULL
                            THEN HASHBYTES('SHA2_256', CONVERT(varchar(36), NEWID())) ELSE NULL END,
                        CASE WHEN @DeviceActivationPositiveConsumptionKind IS NOT NULL
                            THEN 'Windows' ELSE NULL END,
                        @DeviceActivationPositiveConsumptionKind,
                        CASE WHEN @DeviceActivationPositiveHasPreviousIdentity = 1
                            THEN 'PREVIOUS-POSITIVE-SCHEMA-PROBE' ELSE NULL END,
                        CASE WHEN @DeviceActivationPositiveHasPreviousIdentity = 1
                            THEN 'PREVIOUS-DEVICE-POSITIVE-PROBE' ELSE NULL END
                    );
                END TRY
                BEGIN CATCH
                    SET @DeviceActivationPositiveProbeErrorNumber = ERROR_NUMBER();
                    SET @DeviceActivationPositiveProbeErrorMessage = ERROR_MESSAGE();
                END CATCH;

                IF XACT_STATE() <> 1
                BEGIN
                    SET XACT_ABORT ON;
                    THROW 51014, 'Device activation positive semantic probe left the schema transaction unusable.', 1;
                END;

                ROLLBACK TRANSACTION DeviceActivationPositiveProbe;
                IF @DeviceActivationPositiveProbeErrorNumber IS NOT NULL
                BEGIN
                    DECLARE @DeviceActivationPositiveProbeFailure nvarchar(2048) = CONCAT(
                        N'Device activation valid semantic probe case ',
                        @DeviceActivationPositiveProbeCase,
                        N' was rejected by database error ',
                        @DeviceActivationPositiveProbeErrorNumber,
                        N': ',
                        @DeviceActivationPositiveProbeErrorMessage);
                    SET XACT_ABORT ON;
                    THROW 51014, @DeviceActivationPositiveProbeFailure, 1;
                END;

                SET @DeviceActivationPositiveProbeCase += 1;
            END;
            SET XACT_ABORT ON;

            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            SET XACT_ABORT ON;
            IF XACT_STATE() <> 0
                ROLLBACK TRANSACTION;
            THROW;
        END CATCH;
        """;
}
