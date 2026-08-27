using Hbpos.Api.Data;
using Microsoft.Extensions.Hosting;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IInstallmentSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IInstallmentSchemaSqlExecutor
{
    Task ExecuteAsync(
        string acquireLockSql,
        string repairSql,
        CancellationToken cancellationToken = default);
}

public sealed class InstallmentSchemaInitializationState
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;

    public async Task EnsureInitializedAsync(
        Func<CancellationToken, Task> initialize,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref initialized))
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            await initialize(cancellationToken);
            Volatile.Write(ref initialized, true);
        }
        finally
        {
            gate.Release();
        }
    }
}

public sealed class SqlSugarInstallmentSchemaInitializer(
    IInstallmentSchemaSqlExecutor sqlExecutor,
    InstallmentSchemaInitializationState state) : IInstallmentSchemaInitializer
{
    internal const string AcquireSchemaLockSql = """
        DECLARE @SchemaLockResult INT;
        EXEC @SchemaLockResult = sys.sp_getapplock
            @Resource = N'Hbpos.Installment.Schema.v2',
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 60000;
        IF @SchemaLockResult < 0
            THROW 51000, 'Could not acquire the installment schema lock.', 1;
        """;

    // 兼容早期 CodeFirst 结构；该 DDL 只允许在启动 initializer 的数据库锁内执行。
    internal const string EnsureNullableLifecycleColumnsSql = """
        IF OBJECT_ID(N'[dbo].[InstallmentOrder]', N'U') IS NOT NULL
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]', N'U')
                  AND [name] = N'PickedUpAt'
                  AND [is_nullable] = 0)
            BEGIN
                ALTER TABLE [dbo].[InstallmentOrder]
                    ALTER COLUMN [PickedUpAt] DATETIME2 NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]', N'U')
                  AND [name] = N'CancellationKind'
                  AND [is_nullable] = 0)
            BEGIN
                ALTER TABLE [dbo].[InstallmentOrder]
                    ALTER COLUMN [CancellationKind] INT NULL;
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]', N'U')
                  AND [name] = N'CancelledAt'
                  AND [is_nullable] = 0)
            BEGIN
                ALTER TABLE [dbo].[InstallmentOrder]
                    ALTER COLUMN [CancelledAt] DATETIME2 NULL;
            END;

            IF COL_LENGTH(N'[dbo].[InstallmentOrder]', N'PickupOperationGuid') IS NULL
                ALTER TABLE [dbo].[InstallmentOrder] ADD [PickupOperationGuid] NVARCHAR(36) NULL;
            IF COL_LENGTH(N'[dbo].[InstallmentOrder]', N'PickupIdempotencyKey') IS NULL
                ALTER TABLE [dbo].[InstallmentOrder] ADD [PickupIdempotencyKey] NVARCHAR(100) NULL;
            IF COL_LENGTH(N'[dbo].[InstallmentOrder]', N'PickupFingerprint') IS NULL
                ALTER TABLE [dbo].[InstallmentOrder] ADD [PickupFingerprint] NVARCHAR(80) NULL;
            IF COL_LENGTH(N'[dbo].[InstallmentOrder]', N'PickupExecutingDeviceCode') IS NULL
                ALTER TABLE [dbo].[InstallmentOrder] ADD [PickupExecutingDeviceCode] NVARCHAR(50) NULL;
            IF COL_LENGTH(N'[dbo].[InstallmentOrder]', N'PickupCashierId') IS NULL
                ALTER TABLE [dbo].[InstallmentOrder] ADD [PickupCashierId] NVARCHAR(50) NULL;
            IF COL_LENGTH(N'[dbo].[InstallmentOrder]', N'CancellationOperationGuid') IS NULL
                ALTER TABLE [dbo].[InstallmentOrder] ADD [CancellationOperationGuid] NVARCHAR(36) NULL;
            IF COL_LENGTH(N'[dbo].[InstallmentOrder]', N'CancellationFingerprint') IS NULL
                ALTER TABLE [dbo].[InstallmentOrder] ADD [CancellationFingerprint] NVARCHAR(80) NULL;
            IF COL_LENGTH(N'[dbo].[InstallmentOrder]', N'CancellationExecutingDeviceCode') IS NULL
                ALTER TABLE [dbo].[InstallmentOrder] ADD [CancellationExecutingDeviceCode] NVARCHAR(50) NULL;
            IF COL_LENGTH(N'[dbo].[InstallmentOrder]', N'CancellationCashierId') IS NULL
                ALTER TABLE [dbo].[InstallmentOrder] ADD [CancellationCashierId] NVARCHAR(50) NULL;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]', N'U')
                  AND [name] = N'IX_InstallmentOrder_HistoryScope')
            BEGIN
                CREATE INDEX [IX_InstallmentOrder_HistoryScope]
                    ON [dbo].[InstallmentOrder] ([StoreCode], [CreatedAt] DESC, [InstallmentGuid] DESC);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrder]', N'U')
                  AND [name] = N'IX_InstallmentOrder_HistoryUpdatedScope')
            BEGIN
                CREATE INDEX [IX_InstallmentOrder_HistoryUpdatedScope]
                    ON [dbo].[InstallmentOrder] ([StoreCode], [UpdatedAt] DESC, [InstallmentGuid] DESC);
            END;
        END;

        IF OBJECT_ID(N'[dbo].[InstallmentOrderLine]', N'U') IS NOT NULL
        BEGIN
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrderLine]', N'U')
                  AND [name] = N'IX_InstallmentOrderLine_HistoryLookup')
            BEGIN
                CREATE INDEX [IX_InstallmentOrderLine_HistoryLookup]
                    ON [dbo].[InstallmentOrderLine] ([InstallmentGuid], [ItemNumber], [LookupCode]);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrderLine]', N'U')
                  AND [name] = N'IX_InstallmentOrderLine_ItemNumberLookup')
            BEGIN
                CREATE INDEX [IX_InstallmentOrderLine_ItemNumberLookup]
                    ON [dbo].[InstallmentOrderLine] ([ItemNumber], [InstallmentGuid]);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrderLine]', N'U')
                  AND [name] = N'IX_InstallmentOrderLine_BarcodeLookup')
            BEGIN
                CREATE INDEX [IX_InstallmentOrderLine_BarcodeLookup]
                    ON [dbo].[InstallmentOrderLine] ([LookupCode], [InstallmentGuid]);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentOrderLine]', N'U')
                  AND [name] = N'IX_InstallmentOrderLine_ProductCodeLookup')
            BEGIN
                CREATE INDEX [IX_InstallmentOrderLine_ProductCodeLookup]
                    ON [dbo].[InstallmentOrderLine] ([ProductCode], [InstallmentGuid]);
            END;
        END;

        IF OBJECT_ID(N'[dbo].[InstallmentPayment]', N'U') IS NOT NULL
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[dbo].[InstallmentPayment]', N'U')
                  AND [name] = N'CardTransactionsJson'
                  AND ([system_type_id] <> TYPE_ID(N'nvarchar')
                       OR [max_length] <> -1
                       OR [is_nullable] = 0))
            BEGIN
                ALTER TABLE [dbo].[InstallmentPayment]
                    ALTER COLUMN [CardTransactionsJson] NVARCHAR(MAX) NULL;
            END;
        END;

        IF OBJECT_ID(N'[dbo].[StoreVoucherReservation]', N'U') IS NOT NULL
        BEGIN
            -- 动态 DDL 避免旧表缺列时，同一批次后续 ALTER 在编译阶段先失败。
            IF COL_LENGTH(N'[dbo].[StoreVoucherReservation]', N'ConsumedAtUtc') IS NULL
                EXEC(N'ALTER TABLE [dbo].[StoreVoucherReservation] ADD [ConsumedAtUtc] DATETIME2 NULL;');
            IF COL_LENGTH(N'[dbo].[StoreVoucherReservation]', N'ConsumedByReference') IS NULL
                EXEC(N'ALTER TABLE [dbo].[StoreVoucherReservation] ADD [ConsumedByReference] NVARCHAR(100) NULL;');

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[dbo].[StoreVoucherReservation]', N'U')
                  AND [name] = N'ConsumedAtUtc'
                  AND ([system_type_id] <> TYPE_ID(N'datetime2') OR [is_nullable] = 0))
            BEGIN
                EXEC(N'ALTER TABLE [dbo].[StoreVoucherReservation] ALTER COLUMN [ConsumedAtUtc] DATETIME2 NULL;');
            END;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[dbo].[StoreVoucherReservation]', N'U')
                  AND [name] = N'ConsumedByReference'
                  AND ([system_type_id] <> TYPE_ID(N'nvarchar')
                       OR [max_length] <> 200
                       OR [is_nullable] = 0))
            BEGIN
                EXEC(N'ALTER TABLE [dbo].[StoreVoucherReservation] ALTER COLUMN [ConsumedByReference] NVARCHAR(100) NULL;');
            END;
        END;
        """;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        state.EnsureInitializedAsync(
            token => sqlExecutor.ExecuteAsync(
                AcquireSchemaLockSql,
                EnsureNullableLifecycleColumnsSql,
                token),
            cancellationToken);
}

public sealed class SqlSugarInstallmentSchemaSqlExecutor(IServiceScopeFactory scopeFactory)
    : IInstallmentSchemaSqlExecutor
{
    public async Task ExecuteAsync(
        string acquireLockSql,
        string repairSql,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HbposSqlSugarContext>().PosmDb;
        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            db.CodeFirst.InitTables<
                InstallmentOrderEntity,
                InstallmentOrderLineEntity,
                InstallmentPaymentEntity,
                StoreVoucherReservationEntity>();
            return;
        }

        await db.Ado.BeginTranAsync();
        try
        {
            // transaction-owned app lock 覆盖 CodeFirst 与兼容修补，串行同库多实例启动。
            await db.Ado.ExecuteCommandAsync(acquireLockSql);
            cancellationToken.ThrowIfCancellationRequested();
            db.CodeFirst.InitTables<
                InstallmentOrderEntity,
                InstallmentOrderLineEntity,
                InstallmentPaymentEntity,
                StoreVoucherReservationEntity>();
            cancellationToken.ThrowIfCancellationRequested();
            await db.Ado.ExecuteCommandAsync(repairSql);
            cancellationToken.ThrowIfCancellationRequested();
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await TryRollbackAsync(db);
            throw;
        }
    }

    private static async Task TryRollbackAsync(ISqlSugarClient db)
    {
        try
        {
            await db.Ado.RollbackTranAsync();
        }
        catch
        {
            // 保留导致启动失败的原始异常；连接释放时仍会清理未完成事务。
        }
    }
}

public sealed class InstallmentSchemaStartupService(
    IInstallmentSchemaInitializer initializer,
    IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("PosmConnection")) &&
            string.IsNullOrWhiteSpace(configuration.GetConnectionString("HBPOSMConnection")))
        {
            return Task.CompletedTask;
        }

        // Hosted service 启动失败会阻止 Kestrel 开始监听，不能带着半完成 schema 提供服务。
        return initializer.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
