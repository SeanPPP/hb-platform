using Hbpos.Api.Data;

namespace Hbpos.Api.Services;

public interface ISharedHeldOrderSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface ISharedHeldOrderSchemaSqlExecutor
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}

/// <summary>
/// POSM schema initializer：仅实现不注册（本阶段不动 Program/ServiceRegistration）。
/// 建表只发生在启动期，请求路径绝不执行 DDL。
/// </summary>
public sealed class SqlSugarSharedHeldOrderSchemaInitializer(
    ISharedHeldOrderSchemaSqlExecutor sqlExecutor)
    : ISharedHeldOrderSchemaInitializer
{
    internal const string EnsureTableSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;

        DECLARE @SchemaLockResult INT;
        EXEC @SchemaLockResult = sys.sp_getapplock
            @Resource = N'Hbpos.SharedHeldOrder.Schema.v1',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 60000;
        IF @SchemaLockResult < 0
            THROW 51000, 'Could not acquire the shared held order schema lock.', 1;

        IF OBJECT_ID(N'[dbo].[POSM_SharedHeldOrder]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_SharedHeldOrder] (
                [HoldGuid] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_POSM_SharedHeldOrder] PRIMARY KEY,
                [StoreCode] NVARCHAR(50) NOT NULL,
                [DeviceCode] NVARCHAR(50) NOT NULL,
                [CashierId] NVARCHAR(50) NOT NULL,
                [CashierName] NVARCHAR(100) NOT NULL,
                [PayloadVersion] INT NOT NULL CONSTRAINT [DF_POSM_SharedHeldOrder_PayloadVersion] DEFAULT 1,
                [PayloadCiphertext] NVARCHAR(MAX) NOT NULL,
                [Fingerprint] CHAR(64) NOT NULL,
                [IdempotencyKey] NVARCHAR(100) NOT NULL,
                [Status] NVARCHAR(32) NOT NULL,
                [Revision] BIGINT NOT NULL,
                [CreatedAtUtc] DATETIME2(7) NOT NULL,
                [UpdatedAtUtc] DATETIME2(7) NOT NULL,
                [HeldAtUtc] DATETIME2(7) NOT NULL,
                [LineCount] INT NOT NULL,
                [TotalCents] BIGINT NOT NULL,
                [DiscountCents] BIGINT NOT NULL,
                [ActualCents] BIGINT NOT NULL,
                CONSTRAINT [CK_POSM_SharedHeldOrder_Status]
                    CHECK ([Status] IN (N'Pending', N'Claimed', N'Completed', N'Cancelled')),
                CONSTRAINT [CK_POSM_SharedHeldOrder_PayloadVersion]
                    CHECK ([PayloadVersion] IN (1, 2)),
                CONSTRAINT [CK_POSM_SharedHeldOrder_Revision] CHECK ([Revision] > 0),
                CONSTRAINT [CK_POSM_SharedHeldOrder_LineCount] CHECK ([LineCount] > 0),
                CONSTRAINT [CK_POSM_SharedHeldOrder_TotalCents] CHECK ([TotalCents] >= 0),
                CONSTRAINT [CK_POSM_SharedHeldOrder_DiscountCents] CHECK ([DiscountCents] >= 0),
                CONSTRAINT [CK_POSM_SharedHeldOrder_ActualCents] CHECK ([ActualCents] >= 0)
            );
        END;

        -- PayloadVersion：旧表幂等加列默认 1，旧密文保持原样可读，绝不重写。
        IF COL_LENGTH(N'[dbo].[POSM_SharedHeldOrder]', N'PayloadVersion') IS NULL
        BEGIN
            ALTER TABLE [dbo].[POSM_SharedHeldOrder]
                ADD [PayloadVersion] INT NOT NULL
                    CONSTRAINT [DF_POSM_SharedHeldOrder_PayloadVersion] DEFAULT 1
                    WITH VALUES;
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.check_constraints
            WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[POSM_SharedHeldOrder]', N'U')
              AND [name] = N'CK_POSM_SharedHeldOrder_PayloadVersion')
        BEGIN
            ALTER TABLE [dbo].[POSM_SharedHeldOrder]
                ADD CONSTRAINT [CK_POSM_SharedHeldOrder_PayloadVersion]
                CHECK ([PayloadVersion] IN (1, 2));
        END;

        -- 旧库可能仍带有不允许 Cancelled 的状态约束；在同一启动事务内安全替换，失败则由 XACT_ABORT 回滚。
        IF OBJECT_ID(N'[dbo].[POSM_SharedHeldOrder]', N'U') IS NOT NULL
        BEGIN
            IF EXISTS (
                SELECT 1 FROM sys.check_constraints
                WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[POSM_SharedHeldOrder]', N'U')
                  AND [name] = N'CK_POSM_SharedHeldOrder_Status')
               AND NOT EXISTS (
                SELECT 1 FROM sys.check_constraints
                WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[POSM_SharedHeldOrder]', N'U')
                  AND [name] = N'CK_POSM_SharedHeldOrder_Status'
                  AND [definition] LIKE N'%Cancelled%')
            BEGIN
                ALTER TABLE [dbo].[POSM_SharedHeldOrder]
                    DROP CONSTRAINT [CK_POSM_SharedHeldOrder_Status];
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.check_constraints
                WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[POSM_SharedHeldOrder]', N'U')
                  AND [name] = N'CK_POSM_SharedHeldOrder_Status')
            BEGIN
                ALTER TABLE [dbo].[POSM_SharedHeldOrder]
                    ADD CONSTRAINT [CK_POSM_SharedHeldOrder_Status]
                    CHECK ([Status] IN (N'Pending', N'Claimed', N'Completed', N'Cancelled'));
            END;
        END;

        IF OBJECT_ID(N'[dbo].[POSM_SharedHeldOrderClaim]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_SharedHeldOrderClaim] (
                [ClaimGuid] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_POSM_SharedHeldOrderClaim] PRIMARY KEY,
                [HoldGuid] UNIQUEIDENTIFIER NOT NULL,
                [StoreCode] NVARCHAR(50) NOT NULL,
                [ClaimantDeviceCode] NVARCHAR(50) NOT NULL,
                [CashierId] NVARCHAR(50) NOT NULL,
                [CashierName] NVARCHAR(100) NOT NULL,
                [IdempotencyKey] NVARCHAR(100) NOT NULL,
                [Fingerprint] CHAR(64) NOT NULL,
                [Status] NVARCHAR(32) NOT NULL,
                [IsBlocking] BIT NOT NULL,
                [Revision] BIGINT NOT NULL,
                [CreatedAtUtc] DATETIME2(7) NOT NULL,
                [UpdatedAtUtc] DATETIME2(7) NOT NULL,
                [ExpiresAtUtc] DATETIME2(7) NULL,
                [ActivatedAtUtc] DATETIME2(7) NULL,
                [ReleasedAtUtc] DATETIME2(7) NULL,
                [ForceReleased] BIT NOT NULL,
                [ForceReleaseReason] NVARCHAR(500) NULL,
                [ForceReleaseCashierId] NVARCHAR(50) NULL,
                [ForceReleaseCashierName] NVARCHAR(100) NULL,
                [ForceReleaseCashierUserGuid] NVARCHAR(50) NULL,
                [ForceReleasedAtUtc] DATETIME2(7) NULL,
                CONSTRAINT [CK_POSM_SharedHeldOrderClaim_Status]
                    CHECK ([Status] IN (N'Prepared', N'Active', N'Released', N'Completed', N'Superseded')),
                CONSTRAINT [CK_POSM_SharedHeldOrderClaim_Blocking]
                    CHECK (([IsBlocking] = 1 AND [Status] IN (N'Prepared', N'Active')) OR
                           ([IsBlocking] = 0 AND [Status] IN (N'Released', N'Completed', N'Superseded'))),
                CONSTRAINT [CK_POSM_SharedHeldOrderClaim_Revision] CHECK ([Revision] > 0)
            );
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_SharedHeldOrder]', N'U')
              AND [name] = N'UX_POSM_SharedHeldOrder_Idempotency')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_SharedHeldOrder_Idempotency]
                ON [dbo].[POSM_SharedHeldOrder] ([StoreCode], [IdempotencyKey]);
        END;

        -- 页面可见时每 10 秒轮询；按店铺和状态读取，禁止把密文带进覆盖索引。
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_SharedHeldOrder]', N'U')
              AND [name] = N'IX_POSM_SharedHeldOrder_Store_Status_CreatedAt')
        BEGIN
            CREATE INDEX [IX_POSM_SharedHeldOrder_Store_Status_CreatedAt]
                ON [dbo].[POSM_SharedHeldOrder]
                   ([StoreCode], [Status], [CreatedAtUtc], [HoldGuid])
                INCLUDE ([DeviceCode], [CashierId], [CashierName], [HeldAtUtc], [UpdatedAtUtc],
                         [LineCount], [TotalCents], [DiscountCents], [ActualCents], [Revision], [PayloadVersion]);
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_SharedHeldOrderClaim]', N'U')
              AND [name] = N'UX_POSM_SharedHeldOrderClaim_Idempotency')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_SharedHeldOrderClaim_Idempotency]
                ON [dbo].[POSM_SharedHeldOrderClaim] ([HoldGuid], [IdempotencyKey]);
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_SharedHeldOrderClaim]', N'U')
              AND [name] = N'IX_POSM_SharedHeldOrderClaim_Device_Blocking_CreatedAt')
        BEGIN
            CREATE INDEX [IX_POSM_SharedHeldOrderClaim_Device_Blocking_CreatedAt]
                ON [dbo].[POSM_SharedHeldOrderClaim]
                   ([StoreCode], [ClaimantDeviceCode], [CreatedAtUtc], [ClaimGuid])
                WHERE [IsBlocking] = 1;
        END;

        -- filtered unique index 保证同一 hold 同时最多一个 blocking claim（Prepared/Active）。
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_SharedHeldOrderClaim]', N'U')
              AND [name] = N'UX_POSM_SharedHeldOrderClaim_Blocking')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_SharedHeldOrderClaim_Blocking]
                ON [dbo].[POSM_SharedHeldOrderClaim] ([HoldGuid])
                WHERE [IsBlocking] = 1;
        END;

        IF OBJECT_ID(N'[dbo].[POSM_SharedHeldOrderAssociation]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[POSM_SharedHeldOrderAssociation] (
                [OrderGuid] UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT [PK_POSM_SharedHeldOrderAssociation] PRIMARY KEY,
                [HoldGuid] UNIQUEIDENTIFIER NOT NULL,
                [StoreCode] NVARCHAR(50) NOT NULL,
                [ClaimGuid] UNIQUEIDENTIFIER NULL,
                [Disposition] NVARCHAR(32) NOT NULL,
                [CreatedAtUtc] DATETIME2(7) NOT NULL,
                CONSTRAINT [CK_POSM_SharedHeldOrderAssociation_Disposition]
                    CHECK ([Disposition] IN (N'Primary', N'Duplicate', N'Unmatched'))
            );
        END;

        -- OrderGuid 主键保证同一订单唯一幂等；filtered unique index 保证同一 hold 恰好一个 Primary。
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[POSM_SharedHeldOrderAssociation]', N'U')
              AND [name] = N'UX_POSM_SharedHeldOrderAssociation_Primary')
        BEGIN
            CREATE UNIQUE INDEX [UX_POSM_SharedHeldOrderAssociation_Primary]
                ON [dbo].[POSM_SharedHeldOrderAssociation] ([HoldGuid])
                WHERE [Disposition] = N'Primary';
        END;

        COMMIT TRANSACTION;
        """;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return sqlExecutor.ExecuteAsync(EnsureTableSql, cancellationToken);
    }
}

public sealed class SqlSugarSharedHeldOrderSchemaSqlExecutor(
    HbposSqlSugarContext dbContext) : ISharedHeldOrderSchemaSqlExecutor
{
    public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return dbContext.PosmDb.Ado.ExecuteCommandAsync(sql);
    }
}
