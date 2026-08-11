using Hbpos.Api.Services;
using Hbpos.Contracts.HeldOrders;
using Hbpos.Contracts.Orders;
using SqlSugar;

namespace Hbpos.Api.Tests;

/// <summary>
/// SharedHeldOrderRepository 关联写入在真实 SQLite provider 上的可移植性测试：
/// AssociateAsync 必须按 provider 选择 SQL（复用 GetClaimLockSql），不能直接执行
/// SQL Server 方言的 LockClaimRowSql（TOP/[dbo]/UPDLOCK 在非 SQL Server 下不可执行）。
/// </summary>
public sealed class SharedHeldOrderRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-10T01:00:00Z");

    [Fact]
    public void Cancel_path_locks_hold_and_blocking_claim_before_revision_cas()
    {
        Assert.Contains("DeviceCode", SharedHeldOrderMutationLock.LockCancelHoldRowSql);
        Assert.Contains("WITH (UPDLOCK)", SharedHeldOrderMutationLock.LockBlockingClaimRowSql);
        Assert.Contains("[IsBlocking] = 1", SharedHeldOrderMutationLock.LockBlockingClaimRowSql);
        Assert.Contains("[StoreCode] = @StoreCode", SqlSugarSharedHeldOrderRepository.CancelHoldSql);
        Assert.Contains("[DeviceCode] = @DeviceCode", SqlSugarSharedHeldOrderRepository.CancelHoldSql);
        Assert.Contains("[Revision] = @ExpectedRevision", SqlSugarSharedHeldOrderRepository.CancelHoldSql);
        Assert.Contains("[Status] = @ExpectedStatus", SqlSugarSharedHeldOrderRepository.CancelHoldSql);
    }

    [Fact]
    public async Task AssociateAsync_remote_claim_on_sqlite_records_primary_with_portable_claim_lock_sql()
    {
        await using var fixture = new AssociationSqliteFixture();
        await fixture.CreateSchemaAsync();

        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var orderGuid = Guid.NewGuid();
        const string storeCode = "ST001";

        await fixture.SeedPendingHoldAsync(holdGuid, storeCode, revision: 1);
        await fixture.SeedActiveClaimAsync(claimGuid, holdGuid, storeCode, revision: 1);

        await fixture.Client.Ado.BeginTranAsync();
        try
        {
            // 与生产调用方（OrderSyncRepository 事务内）相同：RemoteClaim 路径会执行 claim 行锁查询。
            var disposition = await SharedHeldOrderAssociationStore.AssociateAsync(
                fixture.Client,
                orderGuid,
                storeCode,
                new HeldOrderSourceDto(holdGuid, claimGuid, HeldOrderSourceKind.RemoteClaim),
                Now,
                CancellationToken.None);
            await fixture.Client.Ado.CommitTranAsync();

            Assert.Equal(HeldOrderDisposition.Primary, disposition);
        }
        catch
        {
            await fixture.Client.Ado.RollbackTranAsync();
            throw;
        }

        Assert.Equal(
            SharedHeldOrderStatus.Completed.ToString(),
            await fixture.GetHoldStatusAsync(holdGuid));
        Assert.Equal(
            SharedHeldOrderClaimStatus.Completed.ToString(),
            await fixture.GetClaimStatusAsync(claimGuid));
        Assert.Equal(
            HeldOrderDisposition.Primary.ToString(),
            await fixture.GetAssociationDispositionAsync(orderGuid));
        Assert.Equal(
            HeldOrderDisposition.Primary,
            await SharedHeldOrderAssociationStore.GetDispositionAsync(
                fixture.Client,
                orderGuid,
                CancellationToken.None));
    }

    [Fact]
    public async Task AssociateAsync_prepared_remote_claim_first_order_becomes_primary_and_supersedes_claim()
    {
        await using var fixture = new AssociationSqliteFixture();
        await fixture.CreateSchemaAsync();

        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var orderGuid = Guid.NewGuid();
        const string storeCode = "ST001";

        await fixture.SeedPendingHoldAsync(holdGuid, storeCode, revision: 1);
        await fixture.SeedPreparedClaimAsync(claimGuid, holdGuid, storeCode, revision: 1);

        await fixture.Client.Ado.BeginTranAsync();
        try
        {
            // 服务端 claim 仍为 Prepared（设备已 prepare 但尚未 activate）时，
            // 首笔真实订单必须成为 Primary，并在同一事务内完成 hold、将 claim 推进为 Superseded。
            var disposition = await SharedHeldOrderAssociationStore.AssociateAsync(
                fixture.Client,
                orderGuid,
                storeCode,
                new HeldOrderSourceDto(holdGuid, claimGuid, HeldOrderSourceKind.RemoteClaim),
                Now,
                CancellationToken.None);
            await fixture.Client.Ado.CommitTranAsync();

            Assert.Equal(HeldOrderDisposition.Primary, disposition);
        }
        catch
        {
            await fixture.Client.Ado.RollbackTranAsync();
            throw;
        }

        Assert.Equal(
            SharedHeldOrderStatus.Completed.ToString(),
            await fixture.GetHoldStatusAsync(holdGuid));
        Assert.Equal(
            SharedHeldOrderClaimStatus.Superseded.ToString(),
            await fixture.GetClaimStatusAsync(claimGuid));
        Assert.False(await fixture.GetClaimBlockingAsync(claimGuid));
        Assert.Equal(
            HeldOrderDisposition.Primary.ToString(),
            await fixture.GetAssociationDispositionAsync(orderGuid));
        Assert.Equal(
            HeldOrderDisposition.Primary,
            await SharedHeldOrderAssociationStore.GetDispositionAsync(
                fixture.Client,
                orderGuid,
                CancellationToken.None));
    }

    [Fact]
    public async Task AssociateAsync_prepared_remote_claim_second_order_is_duplicate()
    {
        await using var fixture = new AssociationSqliteFixture();
        await fixture.CreateSchemaAsync();

        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        const string storeCode = "ST001";

        await fixture.SeedPendingHoldAsync(holdGuid, storeCode, revision: 1);
        await fixture.SeedPreparedClaimAsync(claimGuid, holdGuid, storeCode, revision: 1);

        await fixture.Client.Ado.BeginTranAsync();
        try
        {
            var first = await SharedHeldOrderAssociationStore.AssociateAsync(
                fixture.Client,
                Guid.NewGuid(),
                storeCode,
                new HeldOrderSourceDto(holdGuid, claimGuid, HeldOrderSourceKind.RemoteClaim),
                Now,
                CancellationToken.None);
            var second = await SharedHeldOrderAssociationStore.AssociateAsync(
                fixture.Client,
                Guid.NewGuid(),
                storeCode,
                new HeldOrderSourceDto(holdGuid, claimGuid, HeldOrderSourceKind.RemoteClaim),
                Now,
                CancellationToken.None);
            await fixture.Client.Ado.CommitTranAsync();

            Assert.Equal(HeldOrderDisposition.Primary, first);
            Assert.Equal(HeldOrderDisposition.Duplicate, second);
        }
        catch
        {
            await fixture.Client.Ado.RollbackTranAsync();
            throw;
        }

        Assert.Equal(
            SharedHeldOrderStatus.Completed.ToString(),
            await fixture.GetHoldStatusAsync(holdGuid));
        Assert.Equal(
            SharedHeldOrderClaimStatus.Superseded.ToString(),
            await fixture.GetClaimStatusAsync(claimGuid));
    }

    [Fact]
    public async Task AssociateAsync_same_order_guid_retry_keeps_original_primary_disposition()
    {
        await using var fixture = new AssociationSqliteFixture();
        await fixture.CreateSchemaAsync();

        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        var orderGuid = Guid.NewGuid();
        const string storeCode = "ST001";

        await fixture.SeedPendingHoldAsync(holdGuid, storeCode, revision: 1);
        await fixture.SeedPreparedClaimAsync(claimGuid, holdGuid, storeCode, revision: 1);

        await fixture.Client.Ado.BeginTranAsync();
        try
        {
            var first = await SharedHeldOrderAssociationStore.AssociateAsync(
                fixture.Client,
                orderGuid,
                storeCode,
                new HeldOrderSourceDto(holdGuid, claimGuid, HeldOrderSourceKind.RemoteClaim),
                Now,
                CancellationToken.None);
            // 同一 orderGuid 重试必须保持原 disposition，不能二次写入或改写状态。
            var retry = await SharedHeldOrderAssociationStore.AssociateAsync(
                fixture.Client,
                orderGuid,
                storeCode,
                new HeldOrderSourceDto(holdGuid, claimGuid, HeldOrderSourceKind.RemoteClaim),
                Now,
                CancellationToken.None);
            await fixture.Client.Ado.CommitTranAsync();

            Assert.Equal(HeldOrderDisposition.Primary, first);
            Assert.Equal(HeldOrderDisposition.Primary, retry);
            Assert.Equal(1, await fixture.GetAssociationCountAsync(orderGuid));
        }
        catch
        {
            await fixture.Client.Ado.RollbackTranAsync();
            throw;
        }
    }

    [Theory]
    [InlineData(SharedHeldOrderClaimStatus.Released)]
    [InlineData(SharedHeldOrderClaimStatus.Completed)]
    [InlineData(SharedHeldOrderClaimStatus.Superseded)]
    public async Task AssociateAsync_terminal_remote_claim_is_unmatched_without_state_change(
        SharedHeldOrderClaimStatus terminalStatus)
    {
        await using var fixture = new AssociationSqliteFixture();
        await fixture.CreateSchemaAsync();

        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        const string storeCode = "ST001";

        await fixture.SeedPendingHoldAsync(holdGuid, storeCode, revision: 1);
        await fixture.SeedClaimAsync(
            claimGuid,
            holdGuid,
            storeCode,
            terminalStatus,
            revision: 1,
            isBlocking: false);

        await fixture.Client.Ado.BeginTranAsync();
        try
        {
            var disposition = await SharedHeldOrderAssociationStore.AssociateAsync(
                fixture.Client,
                Guid.NewGuid(),
                storeCode,
                new HeldOrderSourceDto(holdGuid, claimGuid, HeldOrderSourceKind.RemoteClaim),
                Now,
                CancellationToken.None);
            await fixture.Client.Ado.CommitTranAsync();

            Assert.Equal(HeldOrderDisposition.Unmatched, disposition);
        }
        catch
        {
            await fixture.Client.Ado.RollbackTranAsync();
            throw;
        }

        Assert.Equal(
            SharedHeldOrderStatus.Pending.ToString(),
            await fixture.GetHoldStatusAsync(holdGuid));
        Assert.Equal(
            terminalStatus.ToString(),
            await fixture.GetClaimStatusAsync(claimGuid));
    }

    [Fact]
    public async Task AssociateAsync_cross_store_remote_claim_is_unmatched()
    {
        await using var fixture = new AssociationSqliteFixture();
        await fixture.CreateSchemaAsync();

        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        const string holdStoreCode = "ST001";

        await fixture.SeedPendingHoldAsync(holdGuid, holdStoreCode, revision: 1);
        await fixture.SeedPreparedClaimAsync(claimGuid, holdGuid, holdStoreCode, revision: 1);

        await fixture.Client.Ado.BeginTranAsync();
        try
        {
            // 订单归属另一门店：claim 即使归属同 hold 也判 Unmatched，且不改状态。
            var disposition = await SharedHeldOrderAssociationStore.AssociateAsync(
                fixture.Client,
                Guid.NewGuid(),
                "ST002",
                new HeldOrderSourceDto(holdGuid, claimGuid, HeldOrderSourceKind.RemoteClaim),
                Now,
                CancellationToken.None);
            await fixture.Client.Ado.CommitTranAsync();

            Assert.Equal(HeldOrderDisposition.Unmatched, disposition);
        }
        catch
        {
            await fixture.Client.Ado.RollbackTranAsync();
            throw;
        }

        Assert.Equal(
            SharedHeldOrderStatus.Pending.ToString(),
            await fixture.GetHoldStatusAsync(holdGuid));
        Assert.Equal(
            SharedHeldOrderClaimStatus.Prepared.ToString(),
            await fixture.GetClaimStatusAsync(claimGuid));
    }

    [Fact]
    public async Task AssociateAsync_active_remote_claim_second_order_is_duplicate()
    {
        await using var fixture = new AssociationSqliteFixture();
        await fixture.CreateSchemaAsync();

        var holdGuid = Guid.NewGuid();
        var claimGuid = Guid.NewGuid();
        const string storeCode = "ST001";

        await fixture.SeedPendingHoldAsync(holdGuid, storeCode, revision: 1);
        await fixture.SeedActiveClaimAsync(claimGuid, holdGuid, storeCode, revision: 1);

        await fixture.Client.Ado.BeginTranAsync();
        try
        {
            var first = await SharedHeldOrderAssociationStore.AssociateAsync(
                fixture.Client,
                Guid.NewGuid(),
                storeCode,
                new HeldOrderSourceDto(holdGuid, claimGuid, HeldOrderSourceKind.RemoteClaim),
                Now,
                CancellationToken.None);
            var second = await SharedHeldOrderAssociationStore.AssociateAsync(
                fixture.Client,
                Guid.NewGuid(),
                storeCode,
                new HeldOrderSourceDto(holdGuid, claimGuid, HeldOrderSourceKind.RemoteClaim),
                Now,
                CancellationToken.None);
            await fixture.Client.Ado.CommitTranAsync();

            Assert.Equal(HeldOrderDisposition.Primary, first);
            Assert.Equal(HeldOrderDisposition.Duplicate, second);
        }
        catch
        {
            await fixture.Client.Ado.RollbackTranAsync();
            throw;
        }

        Assert.Equal(
            SharedHeldOrderStatus.Completed.ToString(),
            await fixture.GetHoldStatusAsync(holdGuid));
        Assert.Equal(
            SharedHeldOrderClaimStatus.Completed.ToString(),
            await fixture.GetClaimStatusAsync(claimGuid));
    }

    private sealed class AssociationSqliteFixture : IAsyncDisposable
    {
        private readonly string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"hbpos-shared-held-association-{Guid.NewGuid():N}.db");

        public AssociationSqliteFixture()
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
                "[HoldGuid] TEXT NOT NULL PRIMARY KEY, [StoreCode] TEXT NOT NULL, " +
                "[Status] TEXT NOT NULL, [Revision] INTEGER NOT NULL, [UpdatedAtUtc] TEXT NOT NULL);");
            await Client.Ado.ExecuteCommandAsync(
                "CREATE TABLE POSM_SharedHeldOrderClaim (" +
                "[ClaimGuid] TEXT NOT NULL PRIMARY KEY, [HoldGuid] TEXT NOT NULL, " +
                "[StoreCode] TEXT NOT NULL, [Status] TEXT NOT NULL, [IsBlocking] INTEGER NOT NULL, " +
                "[Revision] INTEGER NOT NULL, [UpdatedAtUtc] TEXT NOT NULL);");
            await Client.Ado.ExecuteCommandAsync(
                "CREATE TABLE POSM_SharedHeldOrderAssociation (" +
                "[OrderGuid] TEXT NOT NULL PRIMARY KEY, [HoldGuid] TEXT NOT NULL, " +
                "[StoreCode] TEXT NOT NULL, [ClaimGuid] TEXT NULL, [Disposition] TEXT NOT NULL, " +
                "[CreatedAtUtc] TEXT NOT NULL);");
        }

        public Task SeedPendingHoldAsync(Guid holdGuid, string storeCode, long revision)
        {
            return Client.Ado.ExecuteCommandAsync(
                "INSERT INTO POSM_SharedHeldOrder " +
                "([HoldGuid], [StoreCode], [Status], [Revision], [UpdatedAtUtc]) " +
                "VALUES (@HoldGuid, @StoreCode, @Status, @Revision, @UpdatedAtUtc);",
                new SugarParameter("@HoldGuid", holdGuid.ToString("D")),
                new SugarParameter("@StoreCode", storeCode),
                new SugarParameter("@Status", SharedHeldOrderStatus.Pending.ToString()),
                new SugarParameter("@Revision", revision),
                new SugarParameter("@UpdatedAtUtc", Now.UtcDateTime));
        }

        public Task SeedActiveClaimAsync(Guid claimGuid, Guid holdGuid, string storeCode, long revision)
        {
            return SeedClaimAsync(
                claimGuid,
                holdGuid,
                storeCode,
                SharedHeldOrderClaimStatus.Active,
                revision,
                isBlocking: true);
        }

        public Task SeedPreparedClaimAsync(Guid claimGuid, Guid holdGuid, string storeCode, long revision)
        {
            return SeedClaimAsync(
                claimGuid,
                holdGuid,
                storeCode,
                SharedHeldOrderClaimStatus.Prepared,
                revision,
                isBlocking: true);
        }

        public Task SeedClaimAsync(
            Guid claimGuid,
            Guid holdGuid,
            string storeCode,
            SharedHeldOrderClaimStatus status,
            long revision,
            bool isBlocking)
        {
            return Client.Ado.ExecuteCommandAsync(
                "INSERT INTO POSM_SharedHeldOrderClaim " +
                "([ClaimGuid], [HoldGuid], [StoreCode], [Status], [IsBlocking], [Revision], [UpdatedAtUtc]) " +
                "VALUES (@ClaimGuid, @HoldGuid, @StoreCode, @Status, @IsBlocking, @Revision, @UpdatedAtUtc);",
                new SugarParameter("@ClaimGuid", claimGuid.ToString("D")),
                new SugarParameter("@HoldGuid", holdGuid.ToString("D")),
                new SugarParameter("@StoreCode", storeCode),
                new SugarParameter("@Status", status.ToString()),
                new SugarParameter("@IsBlocking", isBlocking),
                new SugarParameter("@Revision", revision),
                new SugarParameter("@UpdatedAtUtc", Now.UtcDateTime));
        }

        public async Task<string?> GetHoldStatusAsync(Guid holdGuid)
        {
            var row = await Client.Ado.SqlQuerySingleAsync<StatusRow>(
                "SELECT [Status] FROM POSM_SharedHeldOrder WHERE [HoldGuid] = @HoldGuid;",
                new SugarParameter("@HoldGuid", holdGuid.ToString("D")));
            return row?.Status;
        }

        public async Task<string?> GetClaimStatusAsync(Guid claimGuid)
        {
            var row = await Client.Ado.SqlQuerySingleAsync<StatusRow>(
                "SELECT [Status] FROM POSM_SharedHeldOrderClaim WHERE [ClaimGuid] = @ClaimGuid;",
                new SugarParameter("@ClaimGuid", claimGuid.ToString("D")));
            return row?.Status;
        }

        public async Task<bool> GetClaimBlockingAsync(Guid claimGuid)
        {
            var row = await Client.Ado.SqlQuerySingleAsync<BlockingRow>(
                "SELECT [IsBlocking] FROM POSM_SharedHeldOrderClaim WHERE [ClaimGuid] = @ClaimGuid;",
                new SugarParameter("@ClaimGuid", claimGuid.ToString("D")));
            return row?.IsBlocking ?? false;
        }

        public async Task<string?> GetAssociationDispositionAsync(Guid orderGuid)
        {
            var row = await Client.Ado.SqlQuerySingleAsync<DispositionRow>(
                "SELECT [Disposition] FROM POSM_SharedHeldOrderAssociation WHERE [OrderGuid] = @OrderGuid;",
                new SugarParameter("@OrderGuid", orderGuid.ToString("D")));
            return row?.Disposition;
        }

        public async Task<int> GetAssociationCountAsync(Guid orderGuid)
        {
            var row = await Client.Ado.SqlQuerySingleAsync<CountRow>(
                "SELECT COUNT(*) AS [Count] FROM POSM_SharedHeldOrderAssociation WHERE [OrderGuid] = @OrderGuid;",
                new SugarParameter("@OrderGuid", orderGuid.ToString("D")));
            return row?.Count ?? 0;
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

        private sealed class StatusRow
        {
            public string Status { get; set; } = string.Empty;
        }

        private sealed class BlockingRow
        {
            public bool IsBlocking { get; set; }
        }

        private sealed class DispositionRow
        {
            public string Disposition { get; set; } = string.Empty;
        }

        private sealed class CountRow
        {
            public int Count { get; set; }
        }
    }
}
