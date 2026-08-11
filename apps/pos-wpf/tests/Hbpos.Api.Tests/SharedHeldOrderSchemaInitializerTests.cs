using Hbpos.Api.Services;
using SqlSugar;

namespace Hbpos.Api.Tests;

public sealed class SharedHeldOrderSchemaInitializerTests
{
    [Fact]
    public async Task Initializer_creates_held_order_and_claim_tables_without_request_time_ddl()
    {
        var executor = new CapturingExecutor();
        var initializer = new SqlSugarSharedHeldOrderSchemaInitializer(executor);

        await initializer.InitializeAsync(CancellationToken.None);

        var sql = Assert.Single(executor.Commands);
        Assert.Contains("SET XACT_ABORT ON", sql);
        Assert.Contains("BEGIN TRANSACTION", sql);
        Assert.Contains("sys.sp_getapplock", sql);
        Assert.Contains("N'Hbpos.SharedHeldOrder.Schema.v1'", sql);
        Assert.Contains("[dbo].[POSM_SharedHeldOrder]", sql);
        Assert.Contains("[dbo].[POSM_SharedHeldOrderClaim]", sql);
        Assert.Contains("[PayloadCiphertext] NVARCHAR(MAX) NOT NULL", sql);
        Assert.Contains("[DiscountCents] BIGINT NOT NULL", sql);
        Assert.Contains("[ActualCents] BIGINT NOT NULL", sql);
        Assert.Contains("N'Pending', N'Claimed', N'Completed'", sql);
        Assert.Contains("N'Prepared', N'Active', N'Released', N'Completed', N'Superseded'", sql);
        Assert.Contains("[Revision] BIGINT NOT NULL", sql);
        Assert.Contains("UX_POSM_SharedHeldOrder_Idempotency", sql);
        Assert.Contains("UX_POSM_SharedHeldOrderClaim_Idempotency", sql);
        Assert.Contains("UX_POSM_SharedHeldOrderClaim_Blocking", sql);
        Assert.Contains("IX_POSM_SharedHeldOrder_Store_Status_CreatedAt", sql);
        Assert.Contains("IX_POSM_SharedHeldOrderClaim_Device_Blocking_CreatedAt", sql);
        Assert.Contains("WHERE [IsBlocking] = 1", sql);
        Assert.DoesNotContain(
            "INCLUDE ([PayloadCiphertext]",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ForceReleased] BIT NOT NULL", sql);
        Assert.Contains("[ForceReleaseReason] NVARCHAR(500) NULL", sql);
        Assert.Contains("[dbo].[POSM_SharedHeldOrderAssociation]", sql);
        Assert.Contains("UX_POSM_SharedHeldOrderAssociation_Primary", sql);
        Assert.Contains("WHERE [Disposition] = N'Primary'", sql);
        Assert.Contains("[Disposition] NVARCHAR(32) NOT NULL", sql);
        Assert.Contains("[OrderGuid] UNIQUEIDENTIFIER NOT NULL", sql);
        Assert.Contains("COMMIT TRANSACTION", sql);
    }

    [Fact]
    public void Association_store_uses_transaction_scoped_applock_updlock_and_unique_primary_contract()
    {
        Assert.Contains("POSM_SharedHeldOrderAssociation", SharedHeldOrderAssociationStore.AssociationTable);
        Assert.Contains("WITH (UPDLOCK)", SharedHeldOrderAssociationStore.HasPrimarySql);
        Assert.Contains("N'Primary'", SharedHeldOrderAssociationStore.HasPrimarySql);
        Assert.Contains("[Disposition]", SharedHeldOrderAssociationStore.InsertAssociationSql);
        Assert.Contains("N'Completed'", SharedHeldOrderAssociationStore.CompleteHoldSql);
        Assert.Contains("[Revision] = [Revision] + 1", SharedHeldOrderAssociationStore.CompleteHoldSql);
        Assert.Contains("[Status] = N'Completed'", SharedHeldOrderAssociationStore.CompleteClaimSql);
        Assert.Contains("[IsBlocking] = 0", SharedHeldOrderAssociationStore.CompleteClaimSql);
        Assert.Contains("N'Superseded'", SharedHeldOrderAssociationStore.SupersedeBlockingClaimsSql);

        // 原子 claim+hold 迁移：同一事务内 applock + 两行 UPDLOCK + 双 revision CAS。
        Assert.Contains("WITH (UPDLOCK)", SharedHeldOrderMutationLock.LockClaimRowSql);
        Assert.Contains(
            "AND [Revision] = @ExpectedRevision",
            SqlSugarSharedHeldOrderRepository.UpdateHoldSql);
        Assert.Contains(
            "AND [Revision] = @ExpectedRevision",
            SqlSugarSharedHeldOrderRepository.UpdateClaimSql);
        Assert.Contains("sys.sp_getapplock", SharedHeldOrderMutationLock.AppLockSql);
        Assert.Contains("N'Transaction'", SharedHeldOrderMutationLock.AppLockSql);

        // 请求路径绝不执行 DDL；关联写入全部在既有事务内完成。
        var requestSql = string.Join(
            '\n',
            SharedHeldOrderAssociationStore.GetDispositionSql,
            SharedHeldOrderAssociationStore.HasPrimarySql,
            SharedHeldOrderAssociationStore.InsertAssociationSql,
            SharedHeldOrderAssociationStore.CompleteHoldSql,
            SharedHeldOrderAssociationStore.CompleteClaimSql,
            SharedHeldOrderAssociationStore.SupersedeBlockingClaimsSql,
            SharedHeldOrderMutationLock.LockClaimRowSql);
        Assert.DoesNotContain("CREATE TABLE", requestSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", requestSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repository_write_path_uses_serializable_applock_updlock_and_revision_cas_without_ddl()
    {
        Assert.Contains("sys.sp_getapplock", SharedHeldOrderMutationLock.AppLockSql);
        Assert.Contains("N'Exclusive'", SharedHeldOrderMutationLock.AppLockSql);
        Assert.Contains("N'Transaction'", SharedHeldOrderMutationLock.AppLockSql);
        Assert.Contains("WITH (UPDLOCK)", SharedHeldOrderMutationLock.LockHoldRowSql);
        Assert.Contains("POSM_SharedHeldOrder", SharedHeldOrderMutationLock.LockHoldRowSql);

        Assert.Contains(
            "AND [Revision] = @ExpectedRevision",
            SqlSugarSharedHeldOrderRepository.UpdateHoldSql);
        Assert.Contains(
            "AND [Revision] = @ExpectedRevision",
            SqlSugarSharedHeldOrderRepository.UpdateClaimSql);
        Assert.Contains(
            "[PayloadCiphertext]",
            SqlSugarSharedHeldOrderRepository.InsertHoldSql);
        Assert.Contains(
            "[IsBlocking]",
            SqlSugarSharedHeldOrderRepository.InsertClaimSql);

        // 请求路径绝不执行 DDL；建表只允许出现在启动期 initializer。
        var requestSql = string.Join(
            '\n',
            SqlSugarSharedHeldOrderRepository.InsertHoldSql,
            SqlSugarSharedHeldOrderRepository.InsertClaimSql,
            SqlSugarSharedHeldOrderRepository.UpdateHoldSql,
            SqlSugarSharedHeldOrderRepository.UpdateClaimSql,
            SharedHeldOrderMutationLock.AppLockSql,
            SharedHeldOrderMutationLock.LockHoldRowSql);
        Assert.DoesNotContain("CREATE TABLE", requestSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", requestSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repository_removes_sql_server_row_lock_hint_for_non_sql_server_claim_updates()
    {
        Assert.Contains(
            "WITH (UPDLOCK)",
            SharedHeldOrderMutationLock.GetClaimLockSql(DbType.SqlServer));
        Assert.DoesNotContain(
            "WITH (UPDLOCK)",
            SharedHeldOrderMutationLock.GetClaimLockSql(DbType.Sqlite));
    }

    [Fact]
    public async Task Non_sql_server_lock_sql_has_no_top_dbo_or_updlock_and_executes_on_sqlite()
    {
        var holdSql = SharedHeldOrderMutationLock.GetHoldLockSql(DbType.Sqlite);
        var claimSql = SharedHeldOrderMutationLock.GetClaimLockSql(DbType.Sqlite);

        Assert.DoesNotContain("TOP", holdSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[dbo]", holdSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDLOCK", holdSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOP", claimSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[dbo]", claimSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDLOCK", claimSql, StringComparison.OrdinalIgnoreCase);

        // SQL Server 路径保持原方言契约，仅非 SQL Server 走可移植 SQL。
        Assert.Contains("SELECT TOP 1", SharedHeldOrderMutationLock.GetHoldLockSql(DbType.SqlServer));
        Assert.Contains("[dbo]", SharedHeldOrderMutationLock.GetHoldLockSql(DbType.SqlServer));
        Assert.Contains("SELECT TOP 1", SharedHeldOrderMutationLock.GetClaimLockSql(DbType.SqlServer));
        Assert.Contains("[dbo]", SharedHeldOrderMutationLock.GetClaimLockSql(DbType.SqlServer));

        await using var fixture = new LockSqliteFixture();
        await fixture.CreateSchemaAsync();
        Assert.Null(await SharedHeldOrderMutationLock.LockHoldAsync(
            fixture.Client,
            Guid.NewGuid(),
            CancellationToken.None));
        Assert.Null(await fixture.Client.Ado.SqlQuerySingleAsync<SharedHeldOrderMutationLock.ClaimLockRow>(
            claimSql,
            new SugarParameter("@ClaimGuid", Guid.NewGuid())));
    }

    private sealed class CapturingExecutor : ISharedHeldOrderSchemaSqlExecutor
    {
        public List<string> Commands { get; } = [];

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
        {
            Commands.Add(sql);
            return Task.CompletedTask;
        }
    }

    private sealed class LockSqliteFixture : IAsyncDisposable
    {
        private readonly string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"hbpos-shared-held-lock-{Guid.NewGuid():N}.db");

        public LockSqliteFixture()
        {
            Client = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = $"Data Source={databasePath}",
                DbType = DbType.Sqlite,
                InitKeyType = InitKeyType.Attribute,
                IsAutoCloseConnection = true
            });
        }

        public SqlSugarClient Client { get; }

        public async Task CreateSchemaAsync()
        {
            await Client.Ado.ExecuteCommandAsync(
                "CREATE TABLE POSM_SharedHeldOrder (" +
                "[HoldGuid] TEXT NOT NULL PRIMARY KEY, " +
                "[StoreCode] TEXT NOT NULL, [Status] TEXT NOT NULL, [Revision] INTEGER NOT NULL);");
            await Client.Ado.ExecuteCommandAsync(
                "CREATE TABLE POSM_SharedHeldOrderClaim (" +
                "[ClaimGuid] TEXT NOT NULL PRIMARY KEY, [HoldGuid] TEXT NOT NULL, " +
                "[StoreCode] TEXT NOT NULL, [Status] TEXT NOT NULL, [Revision] INTEGER NOT NULL);");
        }

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            try
            {
                File.Delete(databasePath);
            }
            catch (IOException)
            {
                // SQLite 可能短暂占用测试数据库文件，不影响断言结果。
            }

            return ValueTask.CompletedTask;
        }
    }
}
