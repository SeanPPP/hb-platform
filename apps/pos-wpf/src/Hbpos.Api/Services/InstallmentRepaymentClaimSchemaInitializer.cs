using Hbpos.Api.Data;

namespace Hbpos.Api.Services;

public interface IInstallmentRepaymentClaimSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IInstallmentRepaymentClaimSchemaSqlExecutor
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}

public sealed class SqlSugarInstallmentRepaymentClaimSchemaInitializer(
    IInstallmentRepaymentClaimSchemaSqlExecutor sqlExecutor)
    : IInstallmentRepaymentClaimSchemaInitializer
{
    internal const string EnsureTableSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        DECLARE @SchemaLockResult INT;
        EXEC @SchemaLockResult = sys.sp_getapplock
            @Resource = N'Hbpos.InstallmentRepaymentClaim.Schema.v1',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 60000;
        IF @SchemaLockResult < 0
            THROW 51000, 'Could not acquire the installment repayment claim schema lock.', 1;

        IF OBJECT_ID(N'[dbo].[POSM_InstallmentRepaymentClaim]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_InstallmentRepaymentClaim] (
                [OperationGuid] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_POSM_InstallmentRepaymentClaim] PRIMARY KEY,
                [InstallmentGuid] UNIQUEIDENTIFIER NOT NULL,
                [PaymentGuid] UNIQUEIDENTIFIER NOT NULL,
                [StoreCode] NVARCHAR(50) NOT NULL,
                [ClaimantDeviceCode] NVARCHAR(50) NOT NULL,
                [CashierId] NVARCHAR(50) NOT NULL,
                [CashierName] NVARCHAR(100) NOT NULL,
                [Amount] DECIMAL(18,2) NOT NULL,
                [Method] INT NOT NULL,
                [IdempotencyKey] NVARCHAR(100) NOT NULL,
                [Fingerprint] CHAR(64) NOT NULL,
                [Status] NVARCHAR(32) NOT NULL,
                [IsBlocking] BIT NOT NULL,
                [Provider] NVARCHAR(32) NULL,
                [ProviderAttemptId] NVARCHAR(128) NULL,
                [CreatedAtUtc] DATETIME2(7) NOT NULL,
                [UpdatedAtUtc] DATETIME2(7) NOT NULL,
                [ExpiresAtUtc] DATETIME2(7) NULL,
                [CommittedAtUtc] DATETIME2(7) NULL,
                [CommitResponseJson] NVARCHAR(MAX) NULL,
                [LastRecoveryCashierId] NVARCHAR(50) NULL,
                [LastRecoveryCashierName] NVARCHAR(100) NULL,
                [LastRecoveryCashierUserGuid] NVARCHAR(50) NULL,
                [RecoveredAtUtc] DATETIME2(7) NULL,
                [Revision] BIGINT NOT NULL,
                CONSTRAINT [CK_POSM_InstallmentRepaymentClaim_Amount] CHECK ([Amount] > 0),
                CONSTRAINT [CK_POSM_InstallmentRepaymentClaim_Method] CHECK ([Method] IN (1, 2, 3)),
                CONSTRAINT [CK_POSM_InstallmentRepaymentClaim_Status]
                    CHECK ([Status] IN (N'Prepared', N'ProviderPending', N'Committed', N'Released', N'Declined', N'Unknown')),
                CONSTRAINT [CK_POSM_InstallmentRepaymentClaim_Blocking]
                    CHECK (([IsBlocking] = 1 AND [Status] IN (N'Prepared', N'ProviderPending', N'Unknown')) OR
                           ([IsBlocking] = 0 AND [Status] IN (N'Committed', N'Released', N'Declined'))),
                CONSTRAINT [CK_POSM_InstallmentRepaymentClaim_Revision] CHECK ([Revision] > 0)
            );
        END;

        -- v1 可在首建或灰度期间重复执行；已存在表也必须补齐 commit 快照与恢复者审计列。
        IF OBJECT_ID(N'[dbo].[POSM_InstallmentRepaymentClaim]', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.POSM_InstallmentRepaymentClaim', N'CommitResponseJson') IS NULL
        BEGIN
            ALTER TABLE [dbo].[POSM_InstallmentRepaymentClaim]
                ADD [CommitResponseJson] NVARCHAR(MAX) NULL;
        END;

        IF OBJECT_ID(N'[dbo].[POSM_InstallmentRepaymentClaim]', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.POSM_InstallmentRepaymentClaim', N'LastRecoveryCashierId') IS NULL
        BEGIN
            ALTER TABLE [dbo].[POSM_InstallmentRepaymentClaim]
                ADD [LastRecoveryCashierId] NVARCHAR(50) NULL;
        END;

        IF OBJECT_ID(N'[dbo].[POSM_InstallmentRepaymentClaim]', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.POSM_InstallmentRepaymentClaim', N'LastRecoveryCashierName') IS NULL
        BEGIN
            ALTER TABLE [dbo].[POSM_InstallmentRepaymentClaim]
                ADD [LastRecoveryCashierName] NVARCHAR(100) NULL;
        END;

        IF OBJECT_ID(N'[dbo].[POSM_InstallmentRepaymentClaim]', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.POSM_InstallmentRepaymentClaim', N'LastRecoveryCashierUserGuid') IS NULL
        BEGIN
            ALTER TABLE [dbo].[POSM_InstallmentRepaymentClaim]
                ADD [LastRecoveryCashierUserGuid] NVARCHAR(50) NULL;
        END;

        IF OBJECT_ID(N'[dbo].[POSM_InstallmentRepaymentClaim]', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.POSM_InstallmentRepaymentClaim', N'RecoveredAtUtc') IS NULL
        BEGIN
            ALTER TABLE [dbo].[POSM_InstallmentRepaymentClaim]
                ADD [RecoveredAtUtc] DATETIME2(7) NULL;
        END;

        IF OBJECT_ID(N'[dbo].[InstallmentPayment]', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.InstallmentPayment', N'CashierName') IS NULL
        BEGIN
            ALTER TABLE [dbo].[InstallmentPayment]
                ADD [CashierName] NVARCHAR(100) NULL;
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_InstallmentRepaymentClaim]', N'U')
              AND [name] = N'UX_POSM_InstallmentRepaymentClaim_PaymentGuid')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_InstallmentRepaymentClaim_PaymentGuid]
                ON [dbo].[POSM_InstallmentRepaymentClaim] ([PaymentGuid]);
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_InstallmentRepaymentClaim]', N'U')
              AND [name] = N'UX_POSM_InstallmentRepaymentClaim_Idempotency')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_InstallmentRepaymentClaim_Idempotency]
                ON [dbo].[POSM_InstallmentRepaymentClaim] ([InstallmentGuid], [IdempotencyKey]);
        END;

        -- IsBlocking 是状态机维护的持久列，使 SQL Server 能用 filtered unique index 原子保证单分期单 claim。
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_InstallmentRepaymentClaim]', N'U')
              AND [name] = N'UX_POSM_InstallmentRepaymentClaim_Blocking')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_InstallmentRepaymentClaim_Blocking]
                ON [dbo].[POSM_InstallmentRepaymentClaim] ([InstallmentGuid])
                WHERE [IsBlocking] = 1;
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_InstallmentRepaymentClaim]', N'U')
              AND [name] = N'UX_POSM_InstallmentRepaymentClaim_ProviderAttempt')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_InstallmentRepaymentClaim_ProviderAttempt]
                ON [dbo].[POSM_InstallmentRepaymentClaim] ([Provider], [ProviderAttemptId])
                WHERE [Provider] IS NOT NULL AND [ProviderAttemptId] IS NOT NULL;
        END;

        COMMIT TRANSACTION;
        """;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return sqlExecutor.ExecuteAsync(EnsureTableSql, cancellationToken);
    }
}

public sealed class SqlSugarInstallmentRepaymentClaimSchemaSqlExecutor(
    HbposSqlSugarContext dbContext) : IInstallmentRepaymentClaimSchemaSqlExecutor
{
    public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return dbContext.PosmDb.Ado.ExecuteCommandAsync(sql);
    }
}
