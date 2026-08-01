using Hbpos.Api.Data;

namespace Hbpos.Api.Services;

public interface ILinklySettlementSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface ILinklySettlementSchemaSqlExecutor
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}

public sealed class SqlSugarLinklySettlementSchemaInitializer(
    ILinklySettlementSchemaSqlExecutor sqlExecutor) : ILinklySettlementSchemaInitializer
{
    internal const string EnsureTableSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        DECLARE @SchemaLockResult INT;
        EXEC @SchemaLockResult = sys.sp_getapplock
            @Resource = N'Hbpos.LinklySettlement.Schema.v1',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 60000;
        IF @SchemaLockResult < 0
            THROW 51000, 'Could not acquire the Linkly settlement schema lock.', 1;

        IF OBJECT_ID(N'[dbo].[POSM_LinklySettlement]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_LinklySettlement] (
                [Id] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_POSM_LinklySettlement] PRIMARY KEY,
                [SettlementGuid] UNIQUEIDENTIFIER NOT NULL,
                [StoreCode] NVARCHAR(32) NOT NULL,
                [DeviceCode] NVARCHAR(64) NOT NULL,
                [BusinessDate] DATE NOT NULL,
                [ConnectionMode] NVARCHAR(32) NOT NULL,
                [Environment] NVARCHAR(32) NOT NULL,
                [ProviderSessionId] NVARCHAR(64) NULL,
                [ProviderSubmissionState] NVARCHAR(32) NULL,
                [CloudBackendSessionId] BIGINT NULL,
                [Status] NVARCHAR(32) NOT NULL,
                [ResponseCode] NVARCHAR(32) NULL,
                [ResponseText] NVARCHAR(512) NULL,
                [SettlementData] NVARCHAR(MAX) NULL,
                [ReceiptTextsJson] NVARCHAR(MAX) NOT NULL,
                [RequestedAtUtc] DATETIME2(7) NOT NULL,
                [CompletedAtUtc] DATETIME2(7) NULL,
                [FirstPrintedAtUtc] DATETIME2(7) NULL,
                [LastPrintedAtUtc] DATETIME2(7) NULL,
                [PrintCount] INT NOT NULL,
                [LastPrintError] NVARCHAR(512) NULL,
                [ClientRevision] BIGINT NOT NULL,
                [ReceivedAtUtc] DATETIME2(7) NOT NULL CONSTRAINT [DF_POSM_LinklySettlement_ReceivedAtUtc] DEFAULT (SYSUTCDATETIME()),
                [UpdatedAtUtc] DATETIME2(7) NOT NULL CONSTRAINT [DF_POSM_LinklySettlement_UpdatedAtUtc] DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT [CK_POSM_LinklySettlement_ConnectionMode]
                    CHECK ([ConnectionMode] IN (N'LocalIp', N'CloudDirectSync', N'CloudBackendAsync')),
                CONSTRAINT [CK_POSM_LinklySettlement_Environment]
                    CHECK ([Environment] IN (N'Production', N'Sandbox')),
                CONSTRAINT [CK_POSM_LinklySettlement_Status]
                    CHECK ([Status] IN (N'Pending', N'Unknown', N'Succeeded', N'Failed')),
                CONSTRAINT [CK_POSM_LinklySettlement_ProviderSubmissionState]
                    CHECK ([ProviderSubmissionState] IS NULL OR [ProviderSubmissionState] IN (N'NotSubmitted', N'Submitted', N'Unknown')),
                CONSTRAINT [CK_POSM_LinklySettlement_PrintCount] CHECK ([PrintCount] >= 0),
                CONSTRAINT [CK_POSM_LinklySettlement_ClientRevision] CHECK ([ClientRevision] > 0)
            );
        END;

        IF COL_LENGTH(N'dbo.POSM_LinklySettlement', N'ProviderSubmissionState') IS NULL
        BEGIN
            ALTER TABLE [dbo].[POSM_LinklySettlement]
                ADD [ProviderSubmissionState] NVARCHAR(32) NULL;
        END;

        -- 只回填已存在的 CloudBackendAsync 快照，避免推断其他模式的银行提交事实。
        UPDATE [dbo].[POSM_LinklySettlement]
        SET [ProviderSubmissionState] = CASE
            WHEN [Status] IN (N'Pending', N'Unknown') THEN N'Unknown'
            WHEN [Status] = N'Failed' AND [ProviderSessionId] IS NULL THEN N'NotSubmitted'
            WHEN [Status] IN (N'Succeeded', N'Failed') AND [ProviderSessionId] IS NOT NULL THEN N'Submitted'
            ELSE N'Unknown'
        END
        WHERE [ProviderSubmissionState] IS NULL
          AND [ConnectionMode] = N'CloudBackendAsync';

        IF NOT EXISTS (
            SELECT 1 FROM sys.check_constraints
            WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[POSM_LinklySettlement]', N'U')
              AND [name] = N'CK_POSM_LinklySettlement_ProviderSubmissionState'
        )
        BEGIN
            ALTER TABLE [dbo].[POSM_LinklySettlement]
                ADD CONSTRAINT [CK_POSM_LinklySettlement_ProviderSubmissionState]
                CHECK ([ProviderSubmissionState] IS NULL OR [ProviderSubmissionState] IN (N'NotSubmitted', N'Submitted', N'Unknown'));
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklySettlement]', N'U')
              AND [name] = N'UX_POSM_LinklySettlement_ScopeGuid')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_LinklySettlement_ScopeGuid]
                ON [dbo].[POSM_LinklySettlement] ([StoreCode], [DeviceCode], [SettlementGuid]);
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklySettlement]', N'U')
              AND [name] = N'UX_POSM_LinklySettlement_ProviderSession')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_LinklySettlement_ProviderSession]
                ON [dbo].[POSM_LinklySettlement]
                    ([ConnectionMode], [Environment], [StoreCode], [DeviceCode], [ProviderSessionId])
                WHERE [ProviderSessionId] IS NOT NULL;
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklySettlement]', N'U')
              AND [name] = N'UX_POSM_LinklySettlement_CloudBackendSession')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_LinklySettlement_CloudBackendSession]
                ON [dbo].[POSM_LinklySettlement] ([CloudBackendSessionId])
                WHERE [CloudBackendSessionId] IS NOT NULL;
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_LinklySettlement]', N'U')
              AND [name] = N'IX_POSM_LinklySettlement_ScopeBusinessDate')
        BEGIN
            CREATE INDEX [IX_POSM_LinklySettlement_ScopeBusinessDate]
                ON [dbo].[POSM_LinklySettlement]
                    ([StoreCode], [DeviceCode], [BusinessDate], [RequestedAtUtc] DESC);
        END;

        COMMIT TRANSACTION;
        """;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return sqlExecutor.ExecuteAsync(EnsureTableSql, cancellationToken);
    }
}

public sealed class SqlSugarLinklySettlementSchemaSqlExecutor(
    HbposSqlSugarContext dbContext) : ILinklySettlementSchemaSqlExecutor
{
    public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return dbContext.PosmDb.Ado.ExecuteCommandAsync(sql);
    }
}
