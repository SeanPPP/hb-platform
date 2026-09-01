using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

[SugarTable("POSM_MobileDeviceActivationGrant")]
public sealed class MobileDeviceActivationGrant
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid GrantId { get; set; }

    [SugarColumn(ColumnDataType = "binary(32)")]
    public byte[] SecretHash { get; set; } = Array.Empty<byte>();

    [SugarColumn(Length = 50)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(Length = 20)]
    public string DeviceSystem { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string TargetUserGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 128)]
    public string TargetUsernameSnapshot { get; set; } = string.Empty;

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? TargetFullNameSnapshot { get; set; }

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

    public Guid? ConsumedBindingId { get; set; }

    [SugarColumn(Length = 20, IsNullable = true)]
    public string? ConsumedDeviceSystem { get; set; }

    [SugarColumn(Length = 10, IsNullable = true)]
    public string? ConsumptionKind { get; set; }

    public Guid? PreviousBindingId { get; set; }

    [SugarColumn(ColumnDataType = "rowversion", IsOnlyIgnoreInsert = true)]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

[SugarTable("POSM_MobileDeviceAccountBinding")]
public sealed class MobileDeviceAccountBinding
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid BindingId { get; set; }

    public int DeviceRegistrationId { get; set; }

    [SugarColumn(Length = 100)]
    public string HardwareId { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string DeviceCode { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(Length = 20)]
    public string DeviceSystem { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string TargetUserGuid { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "binary(32)")]
    public byte[] CredentialVerifier { get; set; } = Array.Empty<byte>();

    public int Version { get; set; } = 1;

    public DateTime BoundAtUtc { get; set; }

    public DateTime? LastSessionExchangeAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? RevokedBy { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? RevokeReason { get; set; }

    public Guid? ReplacedByBindingId { get; set; }

    [SugarColumn(ColumnDataType = "rowversion", IsOnlyIgnoreInsert = true)]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public static class MobileDeviceActivationSchema
{
    public const string EnsureSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        DECLARE @Result int;
        EXEC @Result = sys.sp_getapplock
            @Resource = N'HBPOS:Schema:MobileDeviceActivation',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 30000;
        IF @Result < 0
        BEGIN
            ;THROW 51400, 'Could not acquire mobile device activation schema lock.', 1;
        END;

        IF OBJECT_ID(N'[dbo].[POSM_MobileDeviceActivationGrant]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_MobileDeviceActivationGrant]
            (
                [GrantId] UNIQUEIDENTIFIER NOT NULL,
                [SecretHash] BINARY(32) NOT NULL,
                [StoreCode] VARCHAR(50) NOT NULL,
                [DeviceSystem] VARCHAR(20) NOT NULL,
                [TargetUserGuid] VARCHAR(64) NOT NULL,
                [TargetUsernameSnapshot] NVARCHAR(128) NOT NULL,
                [TargetFullNameSnapshot] NVARCHAR(200) NULL,
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
                [ConsumedBindingId] UNIQUEIDENTIFIER NULL,
                [ConsumedDeviceSystem] VARCHAR(20) NULL,
                [ConsumptionKind] VARCHAR(10) NULL,
                [PreviousBindingId] UNIQUEIDENTIFIER NULL,
                [RowVersion] ROWVERSION NOT NULL,
                CONSTRAINT [PK_POSM_MobileDeviceActivationGrant]
                    PRIMARY KEY CLUSTERED ([GrantId]),
                CONSTRAINT [CK_POSM_MobileDeviceActivationGrant_System]
                    CHECK ([DeviceSystem] IN ('Android', 'iOS')),
                CONSTRAINT [CK_POSM_MobileDeviceActivationGrant_Expiry]
                    CHECK ([ExpiresAtUtc] > [CreatedAtUtc]),
                CONSTRAINT [CK_POSM_MobileDeviceActivationGrant_State]
                    CHECK (
                        ([RevokedAtUtc] IS NULL AND [RevokedBy] IS NULL AND [RevokeReason] IS NULL)
                        OR
                        ([RevokedAtUtc] IS NOT NULL AND [RevokedBy] IS NOT NULL AND [RevokeReason] IS NOT NULL)),
                CONSTRAINT [CK_POSM_MobileDeviceActivationGrant_RevokedConsumedExclusive]
                    CHECK ([RevokedAtUtc] IS NULL OR [ConsumedAtUtc] IS NULL),
                CONSTRAINT [CK_POSM_MobileDeviceActivationGrant_Consumption]
                    CHECK (
                        ([ConsumedAtUtc] IS NULL
                         AND [ConsumedHardwareId] IS NULL
                         AND [ConsumedDeviceCode] IS NULL
                         AND [ConsumedDeviceRegistrationId] IS NULL
                         AND [ConsumedBindingId] IS NULL
                         AND [ConsumedDeviceSystem] IS NULL
                         AND [ConsumptionKind] IS NULL
                         AND [PreviousBindingId] IS NULL)
                        OR
                        ([ConsumedAtUtc] IS NOT NULL
                         AND [ConsumedHardwareId] IS NOT NULL
                         AND [ConsumedDeviceCode] IS NOT NULL
                         AND [ConsumedDeviceRegistrationId] IS NOT NULL
                         AND [ConsumedBindingId] IS NOT NULL
                         AND [ConsumedDeviceSystem] IS NOT NULL
                         AND [ConsumptionKind] IN ('Initial', 'Rebind')
                         AND (([ConsumptionKind] = 'Initial' AND [PreviousBindingId] IS NULL)
                              OR ([ConsumptionKind] = 'Rebind' AND [PreviousBindingId] IS NOT NULL))))
            );
        END;

        IF OBJECT_ID(N'[dbo].[POSM_MobileDeviceAccountBinding]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_MobileDeviceAccountBinding]
            (
                [BindingId] UNIQUEIDENTIFIER NOT NULL,
                [DeviceRegistrationId] INT NOT NULL,
                [HardwareId] VARCHAR(100) NOT NULL,
                [DeviceCode] VARCHAR(50) NOT NULL,
                [StoreCode] VARCHAR(50) NOT NULL,
                [DeviceSystem] VARCHAR(20) NOT NULL,
                [TargetUserGuid] VARCHAR(64) NOT NULL,
                [CredentialVerifier] BINARY(32) NOT NULL,
                [Version] INT NOT NULL CONSTRAINT [DF_POSM_MobileDeviceAccountBinding_Version] DEFAULT (1),
                [BoundAtUtc] DATETIME2(7) NOT NULL,
                [LastSessionExchangeAtUtc] DATETIME2(7) NULL,
                [RevokedAtUtc] DATETIME2(7) NULL,
                [RevokedBy] NVARCHAR(128) NULL,
                [RevokeReason] NVARCHAR(200) NULL,
                [ReplacedByBindingId] UNIQUEIDENTIFIER NULL,
                [RowVersion] ROWVERSION NOT NULL,
                CONSTRAINT [PK_POSM_MobileDeviceAccountBinding]
                    PRIMARY KEY CLUSTERED ([BindingId]),
                CONSTRAINT [CK_POSM_MobileDeviceAccountBinding_System]
                    CHECK ([DeviceSystem] IN ('Android', 'iOS')),
                CONSTRAINT [CK_POSM_MobileDeviceAccountBinding_Version]
                    CHECK ([Version] > 0),
                CONSTRAINT [CK_POSM_MobileDeviceAccountBinding_Revocation]
                    CHECK (
                        ([RevokedAtUtc] IS NULL AND [RevokedBy] IS NULL AND [RevokeReason] IS NULL AND [ReplacedByBindingId] IS NULL)
                        OR
                        ([RevokedAtUtc] IS NOT NULL AND [RevokedBy] IS NOT NULL AND [RevokeReason] IS NOT NULL))
            );
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_MobileDeviceActivationGrant]')
              AND [name] = N'UX_POSM_MobileDeviceActivationGrant_SecretHash')
            CREATE UNIQUE INDEX [UX_POSM_MobileDeviceActivationGrant_SecretHash]
                ON [dbo].[POSM_MobileDeviceActivationGrant] ([SecretHash]);

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_MobileDeviceActivationGrant]')
              AND [name] = N'IX_POSM_MobileDeviceActivationGrant_StoreCreated')
            CREATE INDEX [IX_POSM_MobileDeviceActivationGrant_StoreCreated]
                ON [dbo].[POSM_MobileDeviceActivationGrant] ([StoreCode], [CreatedAtUtc] DESC);

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_MobileDeviceAccountBinding]')
              AND [name] = N'UX_POSM_MobileDeviceAccountBinding_ActiveHardware')
            CREATE UNIQUE INDEX [UX_POSM_MobileDeviceAccountBinding_ActiveHardware]
                ON [dbo].[POSM_MobileDeviceAccountBinding] ([HardwareId])
                WHERE [RevokedAtUtc] IS NULL;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_MobileDeviceAccountBinding]')
              AND [name] = N'UX_POSM_MobileDeviceAccountBinding_ActiveRegistration')
            CREATE UNIQUE INDEX [UX_POSM_MobileDeviceAccountBinding_ActiveRegistration]
                ON [dbo].[POSM_MobileDeviceAccountBinding] ([DeviceRegistrationId])
                WHERE [RevokedAtUtc] IS NULL;

        -- MOBILE_DEVICE_ACTIVATION_VERIFY_START
        DECLARE @GrantObjectId int = OBJECT_ID(N'[dbo].[POSM_MobileDeviceActivationGrant]', N'U');
        DECLARE @BindingObjectId int = OBJECT_ID(N'[dbo].[POSM_MobileDeviceAccountBinding]', N'U');

        -- 两张表由本领域独占；若历史部署中存在同名但不兼容结构，启动迁移必须失败关闭。
        IF @GrantObjectId IS NULL OR @BindingObjectId IS NULL
        BEGIN
            ;THROW 51401, 'Existing mobile device activation schema is incompatible: required table missing.', 1;
        END;

        -- 完整列清单同时锁定类型、字节长度、nullability 与 datetime2 scale，防止同名错型漂移。
        IF EXISTS (
            SELECT 1
            FROM (VALUES
                (@GrantObjectId, N'GrantId', N'uniqueidentifier', 16, 0, -1),
                (@GrantObjectId, N'SecretHash', N'binary', 32, 0, -1),
                (@GrantObjectId, N'StoreCode', N'varchar', 50, 0, -1),
                (@GrantObjectId, N'DeviceSystem', N'varchar', 20, 0, -1),
                (@GrantObjectId, N'TargetUserGuid', N'varchar', 64, 0, -1),
                (@GrantObjectId, N'TargetUsernameSnapshot', N'nvarchar', 256, 0, -1),
                (@GrantObjectId, N'TargetFullNameSnapshot', N'nvarchar', 400, 1, -1),
                (@GrantObjectId, N'CreatedAtUtc', N'datetime2', 8, 0, 7),
                (@GrantObjectId, N'CreatedBy', N'nvarchar', 256, 0, -1),
                (@GrantObjectId, N'Reason', N'nvarchar', 400, 0, -1),
                (@GrantObjectId, N'ExpiresAtUtc', N'datetime2', 8, 0, 7),
                (@GrantObjectId, N'RevokedAtUtc', N'datetime2', 8, 1, 7),
                (@GrantObjectId, N'RevokedBy', N'nvarchar', 256, 1, -1),
                (@GrantObjectId, N'RevokeReason', N'nvarchar', 400, 1, -1),
                (@GrantObjectId, N'ConsumedAtUtc', N'datetime2', 8, 1, 7),
                (@GrantObjectId, N'ConsumedHardwareId', N'varchar', 100, 1, -1),
                (@GrantObjectId, N'ConsumedDeviceCode', N'varchar', 50, 1, -1),
                (@GrantObjectId, N'ConsumedDeviceRegistrationId', N'int', 4, 1, -1),
                (@GrantObjectId, N'ConsumedBindingId', N'uniqueidentifier', 16, 1, -1),
                (@GrantObjectId, N'ConsumedDeviceSystem', N'varchar', 20, 1, -1),
                (@GrantObjectId, N'ConsumptionKind', N'varchar', 10, 1, -1),
                (@GrantObjectId, N'PreviousBindingId', N'uniqueidentifier', 16, 1, -1),
                (@GrantObjectId, N'RowVersion', N'timestamp', 8, 0, -1),
                (@BindingObjectId, N'BindingId', N'uniqueidentifier', 16, 0, -1),
                (@BindingObjectId, N'DeviceRegistrationId', N'int', 4, 0, -1),
                (@BindingObjectId, N'HardwareId', N'varchar', 100, 0, -1),
                (@BindingObjectId, N'DeviceCode', N'varchar', 50, 0, -1),
                (@BindingObjectId, N'StoreCode', N'varchar', 50, 0, -1),
                (@BindingObjectId, N'DeviceSystem', N'varchar', 20, 0, -1),
                (@BindingObjectId, N'TargetUserGuid', N'varchar', 64, 0, -1),
                (@BindingObjectId, N'CredentialVerifier', N'binary', 32, 0, -1),
                (@BindingObjectId, N'Version', N'int', 4, 0, -1),
                (@BindingObjectId, N'BoundAtUtc', N'datetime2', 8, 0, 7),
                (@BindingObjectId, N'LastSessionExchangeAtUtc', N'datetime2', 8, 1, 7),
                (@BindingObjectId, N'RevokedAtUtc', N'datetime2', 8, 1, 7),
                (@BindingObjectId, N'RevokedBy', N'nvarchar', 256, 1, -1),
                (@BindingObjectId, N'RevokeReason', N'nvarchar', 400, 1, -1),
                (@BindingObjectId, N'ReplacedByBindingId', N'uniqueidentifier', 16, 1, -1),
                (@BindingObjectId, N'RowVersion', N'timestamp', 8, 0, -1))
                AS expected(
                    [ObjectId], [Name], [TypeName], [MaxLength], [IsNullable], [Scale])
            LEFT JOIN sys.columns AS actual
              ON actual.[object_id] = expected.[ObjectId]
             AND actual.[name] = expected.[Name]
            WHERE actual.[column_id] IS NULL
               OR TYPE_NAME(actual.[user_type_id]) <> expected.[TypeName]
               OR actual.[max_length] <> expected.[MaxLength]
               OR actual.[is_nullable] <> expected.[IsNullable]
               OR (expected.[Scale] >= 0 AND actual.[scale] <> expected.[Scale]))
        BEGIN
            ;THROW 51402, 'Existing mobile device activation schema is incompatible: column manifest mismatch.', 1;
        END;

        IF COL_LENGTH(N'[dbo].[POSM_MobileDeviceActivationGrant]', N'ActivationCode') IS NOT NULL
           OR COL_LENGTH(N'[dbo].[POSM_MobileDeviceAccountBinding]', N'Credential') IS NOT NULL
        BEGIN
            ;THROW 51404, 'Existing mobile device activation schema is incompatible: plaintext secret column detected.', 1;
        END;

        -- 主键必须保持命名、clustered、唯一、启用且仅包含领域标识列。
        IF EXISTS (
            SELECT 1
            FROM (VALUES
                (@GrantObjectId, N'PK_POSM_MobileDeviceActivationGrant', N'GrantId'),
                (@BindingObjectId, N'PK_POSM_MobileDeviceAccountBinding', N'BindingId'))
                AS expected([ParentObjectId], [Name], [ColumnName])
            WHERE NOT EXISTS (
                SELECT 1
                FROM sys.key_constraints AS key_info
                INNER JOIN sys.indexes AS index_info
                  ON index_info.[object_id] = key_info.[parent_object_id]
                 AND index_info.[index_id] = key_info.[unique_index_id]
                INNER JOIN sys.index_columns AS index_column
                  ON index_column.[object_id] = index_info.[object_id]
                 AND index_column.[index_id] = index_info.[index_id]
                 AND index_column.[key_ordinal] = 1
                INNER JOIN sys.columns AS column_info
                  ON column_info.[object_id] = index_column.[object_id]
                 AND column_info.[column_id] = index_column.[column_id]
                WHERE key_info.[parent_object_id] = expected.[ParentObjectId]
                  AND key_info.[name] = expected.[Name]
                  AND key_info.[type] = N'PK'
                  AND index_info.[is_primary_key] = 1
                  AND index_info.[is_unique] = 1
                  AND index_info.[type] = 1
                  AND index_info.[is_disabled] = 0
                  AND index_info.[is_hypothetical] = 0
                  AND index_info.[has_filter] = 0
                  AND column_info.[name] = expected.[ColumnName]
                  AND NOT EXISTS (
                      SELECT 1
                      FROM sys.index_columns AS extra_key
                      WHERE extra_key.[object_id] = index_info.[object_id]
                        AND extra_key.[index_id] = index_info.[index_id]
                        AND extra_key.[key_ordinal] > 1)))
        BEGIN
            ;THROW 51405, 'Existing mobile device activation schema is incompatible: primary key shape.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.default_constraints AS default_info
            INNER JOIN sys.columns AS column_info
              ON column_info.[object_id] = default_info.[parent_object_id]
             AND column_info.[column_id] = default_info.[parent_column_id]
            CROSS APPLY (VALUES (REPLACE(REPLACE(REPLACE(
                LOWER(default_info.[definition]), N'(', N''), N')', N''), N' ', N'')))
                AS normalized([Definition])
            WHERE default_info.[parent_object_id] = @BindingObjectId
              AND default_info.[name] = N'DF_POSM_MobileDeviceAccountBinding_Version'
              AND column_info.[name] = N'Version'
              AND normalized.[Definition] = N'1')
        BEGIN
            ;THROW 51406, 'Existing mobile device activation schema is incompatible: binding version default.', 1;
        END;

        -- 规范化后只接受已知的完整表达式；额外 OR 1=1 或删改任一状态条件都会失败关闭。
        IF EXISTS (
            SELECT 1
            FROM (VALUES
                (@GrantObjectId, N'CK_POSM_MobileDeviceActivationGrant_System',
                    N'devicesystemin''android'',''ios''',
                    N'devicesystem=''android''ordevicesystem=''ios''',
                    N'devicesystem=''ios''ordevicesystem=''android'''),
                (@GrantObjectId, N'CK_POSM_MobileDeviceActivationGrant_Expiry',
                    N'expiresatutc>createdatutc', N'expiresatutc>createdatutc', N'expiresatutc>createdatutc'),
                (@GrantObjectId, N'CK_POSM_MobileDeviceActivationGrant_State',
                    N'revokedatutcisnullandrevokedbyisnullandrevokereasonisnullorrevokedatutcisnotnullandrevokedbyisnotnullandrevokereasonisnotnull',
                    N'revokedatutcisnullandrevokedbyisnullandrevokereasonisnullorrevokedatutcisnotnullandrevokedbyisnotnullandrevokereasonisnotnull',
                    N'revokedatutcisnullandrevokedbyisnullandrevokereasonisnullorrevokedatutcisnotnullandrevokedbyisnotnullandrevokereasonisnotnull'),
                (@GrantObjectId, N'CK_POSM_MobileDeviceActivationGrant_RevokedConsumedExclusive',
                    N'revokedatutcisnullorconsumedatutcisnull',
                    N'revokedatutcisnullorconsumedatutcisnull',
                    N'consumedatutcisnullorrevokedatutcisnull'),
                (@GrantObjectId, N'CK_POSM_MobileDeviceActivationGrant_Consumption',
                    N'consumedatutcisnullandconsumedhardwareidisnullandconsumeddevicecodeisnullandconsumeddeviceregistrationidisnullandconsumedbindingidisnullandconsumeddevicesystemisnullandconsumptionkindisnullandpreviousbindingidisnullorconsumedatutcisnotnullandconsumedhardwareidisnotnullandconsumeddevicecodeisnotnullandconsumeddeviceregistrationidisnotnullandconsumedbindingidisnotnullandconsumeddevicesystemisnotnullandconsumptionkindin''initial'',''rebind''andconsumptionkind=''initial''andpreviousbindingidisnullorconsumptionkind=''rebind''andpreviousbindingidisnotnull',
                    N'consumedatutcisnullandconsumedhardwareidisnullandconsumeddevicecodeisnullandconsumeddeviceregistrationidisnullandconsumedbindingidisnullandconsumeddevicesystemisnullandconsumptionkindisnullandpreviousbindingidisnullorconsumedatutcisnotnullandconsumedhardwareidisnotnullandconsumeddevicecodeisnotnullandconsumeddeviceregistrationidisnotnullandconsumedbindingidisnotnullandconsumeddevicesystemisnotnullandconsumptionkind=''initial''orconsumptionkind=''rebind''andconsumptionkind=''initial''andpreviousbindingidisnullorconsumptionkind=''rebind''andpreviousbindingidisnotnull',
                    N'consumedatutcisnullandconsumedhardwareidisnullandconsumeddevicecodeisnullandconsumeddeviceregistrationidisnullandconsumedbindingidisnullandconsumeddevicesystemisnullandconsumptionkindisnullandpreviousbindingidisnullorconsumedatutcisnotnullandconsumedhardwareidisnotnullandconsumeddevicecodeisnotnullandconsumeddeviceregistrationidisnotnullandconsumedbindingidisnotnullandconsumeddevicesystemisnotnullandconsumptionkind=''rebind''orconsumptionkind=''initial''andconsumptionkind=''initial''andpreviousbindingidisnullorconsumptionkind=''rebind''andpreviousbindingidisnotnull'),
                (@BindingObjectId, N'CK_POSM_MobileDeviceAccountBinding_System',
                    N'devicesystemin''android'',''ios''',
                    N'devicesystem=''android''ordevicesystem=''ios''',
                    N'devicesystem=''ios''ordevicesystem=''android'''),
                (@BindingObjectId, N'CK_POSM_MobileDeviceAccountBinding_Version',
                    N'version>0', N'version>0', N'version>0'),
                (@BindingObjectId, N'CK_POSM_MobileDeviceAccountBinding_Revocation',
                    N'revokedatutcisnullandrevokedbyisnullandrevokereasonisnullandreplacedbybindingidisnullorrevokedatutcisnotnullandrevokedbyisnotnullandrevokereasonisnotnull',
                    N'revokedatutcisnullandrevokedbyisnullandrevokereasonisnullandreplacedbybindingidisnullorrevokedatutcisnotnullandrevokedbyisnotnullandrevokereasonisnotnull',
                    N'revokedatutcisnullandrevokedbyisnullandrevokereasonisnullandreplacedbybindingidisnullorrevokedatutcisnotnullandrevokedbyisnotnullandrevokereasonisnotnull'))
                AS expected(
                    [ParentObjectId], [Name], [Definition1], [Definition2], [Definition3])
            LEFT JOIN sys.check_constraints AS actual
              ON actual.[parent_object_id] = expected.[ParentObjectId]
             AND actual.[name] = expected.[Name]
            CROSS APPLY (VALUES (
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    LOWER(actual.[definition]),
                    N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N''),
                    CHAR(9), N''), CHAR(10), N''), CHAR(13), N'')))
                AS normalized([Definition])
            WHERE actual.[object_id] IS NULL
               OR actual.[is_disabled] = 1
               OR actual.[is_not_trusted] = 1
               OR normalized.[Definition] NOT IN (
                    expected.[Definition1],
                    expected.[Definition2],
                    expected.[Definition3])
        )
        BEGIN
            ;THROW 51408, 'Existing mobile device activation schema is incompatible: check constraint missing or untrusted.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes AS index_info
            INNER JOIN sys.index_columns AS index_column
              ON index_column.[object_id] = index_info.[object_id]
             AND index_column.[index_id] = index_info.[index_id]
             AND index_column.[key_ordinal] = 1
            INNER JOIN sys.columns AS column_info
              ON column_info.[object_id] = index_column.[object_id]
             AND column_info.[column_id] = index_column.[column_id]
            WHERE index_info.[object_id] = @GrantObjectId
              AND index_info.[name] = N'UX_POSM_MobileDeviceActivationGrant_SecretHash'
              AND index_info.[is_unique] = 1
              AND index_info.[is_disabled] = 0
              AND index_info.[is_hypothetical] = 0
              AND index_info.[has_filter] = 0
              AND column_info.[name] = N'SecretHash'
              AND NOT EXISTS (
                  SELECT 1
                  FROM sys.index_columns AS extra_key
                  WHERE extra_key.[object_id] = index_info.[object_id]
                    AND extra_key.[index_id] = index_info.[index_id]
                    AND extra_key.[key_ordinal] > 1))
        BEGIN
            ;THROW 51409, 'Existing mobile device activation schema is incompatible: grant secret index.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes AS index_info
            INNER JOIN sys.index_columns AS index_column
              ON index_column.[object_id] = index_info.[object_id]
             AND index_column.[index_id] = index_info.[index_id]
             AND index_column.[key_ordinal] = 1
            INNER JOIN sys.columns AS column_info
              ON column_info.[object_id] = index_column.[object_id]
             AND column_info.[column_id] = index_column.[column_id]
            WHERE index_info.[object_id] = @BindingObjectId
              AND index_info.[name] = N'UX_POSM_MobileDeviceAccountBinding_ActiveHardware'
              AND index_info.[is_unique] = 1
              AND index_info.[is_disabled] = 0
              AND index_info.[is_hypothetical] = 0
              AND index_info.[has_filter] = 1
              AND REPLACE(REPLACE(REPLACE(REPLACE(
                    LOWER(index_info.[filter_definition]),
                    N'[', N''), N']', N''), N'(', N''), N')', N'') = N'revokedatutc is null'
              AND column_info.[name] = N'HardwareId'
              AND NOT EXISTS (
                  SELECT 1
                  FROM sys.index_columns AS extra_key
                  WHERE extra_key.[object_id] = index_info.[object_id]
                    AND extra_key.[index_id] = index_info.[index_id]
                    AND extra_key.[key_ordinal] > 1))
        BEGIN
            ;THROW 51410, 'Existing mobile device activation schema is incompatible: active hardware index.', 1;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes AS index_info
            INNER JOIN sys.index_columns AS index_column
              ON index_column.[object_id] = index_info.[object_id]
             AND index_column.[index_id] = index_info.[index_id]
             AND index_column.[key_ordinal] = 1
            INNER JOIN sys.columns AS column_info
              ON column_info.[object_id] = index_column.[object_id]
             AND column_info.[column_id] = index_column.[column_id]
            WHERE index_info.[object_id] = @BindingObjectId
              AND index_info.[name] = N'UX_POSM_MobileDeviceAccountBinding_ActiveRegistration'
              AND index_info.[is_unique] = 1
              AND index_info.[is_disabled] = 0
              AND index_info.[is_hypothetical] = 0
              AND index_info.[has_filter] = 1
              AND REPLACE(REPLACE(REPLACE(REPLACE(
                    LOWER(index_info.[filter_definition]),
                    N'[', N''), N']', N''), N'(', N''), N')', N'') = N'revokedatutc is null'
              AND column_info.[name] = N'DeviceRegistrationId'
              AND NOT EXISTS (
                  SELECT 1
                  FROM sys.index_columns AS extra_key
                  WHERE extra_key.[object_id] = index_info.[object_id]
                    AND extra_key.[index_id] = index_info.[index_id]
                    AND extra_key.[key_ordinal] > 1))
        BEGIN
            ;THROW 51411, 'Existing mobile device activation schema is incompatible: active registration index.', 1;
        END;
        -- MOBILE_DEVICE_ACTIVATION_VERIFY_END

        COMMIT TRANSACTION;
        """;

    private const string VerifyStartMarker = "-- MOBILE_DEVICE_ACTIVATION_VERIFY_START";
    private const string VerifyEndMarker = "-- MOBILE_DEVICE_ACTIVATION_VERIFY_END";

    public static string VerifySql { get; } = ExtractVerifySql();

    private static string ExtractVerifySql()
    {
        var start = EnsureSql.IndexOf(VerifyStartMarker, StringComparison.Ordinal);
        var end = EnsureSql.IndexOf(VerifyEndMarker, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException(
                "Mobile device activation schema verification markers are invalid.");
        }

        start += VerifyStartMarker.Length;
        var body = EnsureSql[start..end].Trim();
        return $"SET NOCOUNT ON;\nSET XACT_ABORT ON;\n{body}\n";
    }
}
