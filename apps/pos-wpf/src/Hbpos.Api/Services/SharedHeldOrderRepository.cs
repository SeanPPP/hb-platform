using Hbpos.Api.Data;
using Hbpos.Contracts.HeldOrders;
using Hbpos.Contracts.Orders;
using SqlSugar;

namespace Hbpos.Api.Services;

public sealed record SharedHeldOrderRecord(
    Guid HoldGuid,
    string StoreCode,
    string DeviceCode,
    string CashierId,
    string CashierName,
    int PayloadVersion,
    string PayloadCiphertext,
    string Fingerprint,
    string IdempotencyKey,
    SharedHeldOrderStatus Status,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset HeldAtUtc,
    int LineCount,
    long TotalCents,
    long DiscountCents,
    long ActualCents);

public sealed record SharedHeldOrderClaimRecord(
    Guid ClaimGuid,
    Guid HoldGuid,
    string StoreCode,
    string ClaimantDeviceCode,
    string CashierId,
    string CashierName,
    string IdempotencyKey,
    string Fingerprint,
    SharedHeldOrderClaimStatus Status,
    bool IsBlocking,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    bool ForceReleased = false,
    string? ForceReleaseReason = null,
    string? ForceReleaseCashierId = null,
    string? ForceReleaseCashierName = null,
    string? ForceReleaseCashierUserGuid = null,
    DateTimeOffset? ForceReleasedAtUtc = null);

public interface ISharedHeldOrderRepository
{
    Task<SharedHeldOrderRecord?> GetHoldAsync(
        Guid holdGuid,
        CancellationToken cancellationToken);

    Task<SharedHeldOrderRecord?> GetHoldByIdempotencyAsync(
        string storeCode,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<bool> TryInsertHoldAsync(
        SharedHeldOrderRecord hold,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateHoldAsync(
        SharedHeldOrderRecord hold,
        long expectedRevision,
        CancellationToken cancellationToken);

    Task<bool> TryCancelHoldAsync(
        Guid holdGuid,
        string storeCode,
        string deviceCode,
        long expectedRevision,
        DateTimeOffset cancelledAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SharedHeldOrderRecord>> ListPendingAsync(
        string storeCode,
        CancellationToken cancellationToken);

    Task<SharedHeldOrderClaimRecord?> GetClaimAsync(
        Guid claimGuid,
        CancellationToken cancellationToken);

    Task<SharedHeldOrderClaimRecord?> GetBlockingClaimAsync(
        Guid holdGuid,
        CancellationToken cancellationToken);

    Task<bool> TryInsertClaimAsync(
        SharedHeldOrderClaimRecord claim,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateClaimAsync(
        SharedHeldOrderClaimRecord claim,
        long expectedRevision,
        CancellationToken cancellationToken);

    /// <summary>
    /// 同一事务内 CAS 更新 claim 与 hold 两行（激活/释放/强制释放），
    /// 消除 Active+hold Pending、Released+hold Claimed 的崩溃窗口。
    /// </summary>
    Task<bool> TryUpdateClaimAndHoldAsync(
        SharedHeldOrderClaimRecord claim,
        long expectedClaimRevision,
        SharedHeldOrderRecord hold,
        long expectedHoldRevision,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SharedHeldOrderClaimRecord>> ListMyClaimsAsync(
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken);
}

/// <summary>
/// SQL Server 写入路径使用 Serializable 事务 + applock + UPDLOCK 串行化同一 hold 的变更，
/// 参考 installment claim；非 SQL Server（测试库）仅保留进程内信号量。
/// </summary>
internal static class SharedHeldOrderMutationLock
{
    internal const string AppLockSql = """
        DECLARE @Result int;
        EXEC @Result = sys.sp_getapplock
            @Resource = @Resource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 15000;
        SELECT @Result;
        """;

    internal const string LockHoldRowSql = """
        SELECT TOP 1 [HoldGuid], [StoreCode], [Status], [Revision]
        FROM [dbo].[POSM_SharedHeldOrder] WITH (UPDLOCK)
        WHERE [HoldGuid] = @HoldGuid;
        """;

    internal const string LockCancelHoldRowSql = """
        SELECT TOP 1 [HoldGuid], [StoreCode], [DeviceCode], [Status], [Revision]
        FROM [dbo].[POSM_SharedHeldOrder] WITH (UPDLOCK)
        WHERE [HoldGuid] = @HoldGuid;
        """;

    internal const string LockClaimRowSql = """
        SELECT TOP 1 [ClaimGuid], [HoldGuid], [StoreCode], [Status], [Revision]
        FROM [dbo].[POSM_SharedHeldOrderClaim] WITH (UPDLOCK)
        WHERE [ClaimGuid] = @ClaimGuid;
        """;

    internal const string LockBlockingClaimRowSql = """
        SELECT TOP 1 [ClaimGuid], [HoldGuid], [StoreCode], [Status], [Revision]
        FROM [dbo].[POSM_SharedHeldOrderClaim] WITH (UPDLOCK)
        WHERE [HoldGuid] = @HoldGuid AND [IsBlocking] = 1;
        """;

    private static readonly object ProcessLockGate = new();
    private static readonly Dictionary<string, LockEntry> ProcessLocks = new(StringComparer.Ordinal);

    internal static string BuildResource(Guid holdGuid) =>
        $"hbpos-shared-held-order:{holdGuid:D}";

    internal static async ValueTask<IAsyncDisposable> AcquireProcessAsync(
        Guid holdGuid,
        CancellationToken cancellationToken)
    {
        var resource = BuildResource(holdGuid);
        LockEntry entry;
        lock (ProcessLockGate)
        {
            if (!ProcessLocks.TryGetValue(resource, out entry!))
            {
                entry = new LockEntry();
                ProcessLocks.Add(resource, entry);
            }

            entry.ReferenceCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(resource, entry);
        }
        catch
        {
            ReleaseReference(resource, entry, releaseSemaphore: false);
            throw;
        }
    }

    internal static async Task AcquireDatabaseAsync(
        ISqlSugarClient db,
        Guid holdGuid)
    {
        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            return;
        }

        var result = await db.Ado.SqlQuerySingleAsync<int>(
            AppLockSql,
            new SugarParameter("@Resource", BuildResource(holdGuid)));
        if (result < 0)
        {
            throw new SharedHeldOrderException(
                SharedHeldOrderErrorCodes.Busy,
                "Held order is busy with another claim operation.");
        }
    }

    internal static async Task<HoldLockRow?> LockHoldAsync(
        ISqlSugarClient db,
        Guid holdGuid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = GetHoldLockSql(db.CurrentConnectionConfig.DbType);
        return await db.Ado.SqlQuerySingleAsync<HoldLockRow>(
            sql,
            new SugarParameter("@HoldGuid", holdGuid));
    }

    internal static async Task<ClaimLockRow?> LockBlockingClaimAsync(
        ISqlSugarClient db,
        Guid holdGuid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = GetBlockingClaimLockSql(db.CurrentConnectionConfig.DbType);
        return await db.Ado.SqlQuerySingleAsync<ClaimLockRow>(
            sql,
            new SugarParameter("@HoldGuid", holdGuid));
    }

    internal static async Task<HoldLockRow?> LockCancelHoldAsync(
        ISqlSugarClient db,
        Guid holdGuid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = GetCancelHoldLockSql(db.CurrentConnectionConfig.DbType);
        return await db.Ado.SqlQuerySingleAsync<HoldLockRow>(
            sql,
            new SugarParameter("@HoldGuid", holdGuid));
    }

    internal static string GetHoldLockSql(DbType dbType) =>
        dbType == DbType.SqlServer
            ? LockHoldRowSql
            : ToNonSqlServerSql(LockHoldRowSql);

    internal static string GetCancelHoldLockSql(DbType dbType) =>
        dbType == DbType.SqlServer
            ? LockCancelHoldRowSql
            : ToNonSqlServerSql(LockCancelHoldRowSql);

    internal static string GetClaimLockSql(DbType dbType) =>
        dbType == DbType.SqlServer
            ? LockClaimRowSql
            : ToNonSqlServerSql(LockClaimRowSql);

    internal static string GetBlockingClaimLockSql(DbType dbType) =>
        dbType == DbType.SqlServer
            ? LockBlockingClaimRowSql
            : ToNonSqlServerSql(LockBlockingClaimRowSql);

    // 非 SQL Server（SQLite 等测试/开发 provider）不支持 TOP、[dbo] 前缀与 UPDLOCK 提示。
    internal static string ToNonSqlServerSql(string sql) =>
        sql.Replace("SELECT TOP 1 ", "SELECT ", StringComparison.Ordinal)
           .Replace("[dbo].", string.Empty, StringComparison.Ordinal)
           .Replace(" WITH (UPDLOCK)", string.Empty, StringComparison.Ordinal);

    private static void ReleaseReference(string resource, LockEntry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (ProcessLockGate)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0 &&
                ProcessLocks.TryGetValue(resource, out var current) &&
                ReferenceEquals(current, entry))
            {
                ProcessLocks.Remove(resource);
                entry.Semaphore.Dispose();
            }
        }
    }

    internal sealed class HoldLockRow
    {
        public Guid HoldGuid { get; set; }

        public string StoreCode { get; set; } = string.Empty;

        public string DeviceCode { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public long Revision { get; set; }
    }

    internal sealed class ClaimLockRow
    {
        public Guid ClaimGuid { get; set; }

        public Guid HoldGuid { get; set; }

        public string StoreCode { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public long Revision { get; set; }
    }

    private sealed class LockEntry
    {
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);

        internal int ReferenceCount { get; set; }
    }

    private sealed class Releaser(string resource, LockEntry entry) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ReleaseReference(resource, entry, releaseSemaphore: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}

public sealed class SqlSugarSharedHeldOrderRepository(
    HbposSqlSugarContext dbContext) : ISharedHeldOrderRepository
{
    private const string HoldSelectColumns = """
        [HoldGuid], [StoreCode], [DeviceCode], [CashierId], [CashierName], [PayloadVersion], [PayloadCiphertext],
        [Fingerprint], [IdempotencyKey], [Status], [Revision], [CreatedAtUtc], [UpdatedAtUtc],
        [HeldAtUtc], [LineCount], [TotalCents], [DiscountCents], [ActualCents]
        """;

    private const string ClaimSelectColumns = """
        [ClaimGuid], [HoldGuid], [StoreCode], [ClaimantDeviceCode], [CashierId], [CashierName],
        [IdempotencyKey], [Fingerprint], [Status], [IsBlocking], [Revision], [CreatedAtUtc],
        [UpdatedAtUtc], [ExpiresAtUtc], [ActivatedAtUtc], [ReleasedAtUtc],
        [ForceReleased], [ForceReleaseReason], [ForceReleaseCashierId],
        [ForceReleaseCashierName], [ForceReleaseCashierUserGuid], [ForceReleasedAtUtc]
        """;

    internal const string InsertHoldSql = """
        INSERT INTO [dbo].[POSM_SharedHeldOrder]
        ([HoldGuid], [StoreCode], [DeviceCode], [CashierId], [CashierName], [PayloadVersion], [PayloadCiphertext],
         [Fingerprint], [IdempotencyKey], [Status], [Revision], [CreatedAtUtc], [UpdatedAtUtc],
         [HeldAtUtc], [LineCount], [TotalCents], [DiscountCents], [ActualCents])
        VALUES
        (@HoldGuid, @StoreCode, @DeviceCode, @CashierId, @CashierName, @PayloadVersion, @PayloadCiphertext,
         @Fingerprint, @IdempotencyKey, @Status, @Revision, @CreatedAtUtc, @UpdatedAtUtc,
         @HeldAtUtc, @LineCount, @TotalCents, @DiscountCents, @ActualCents);
        """;

    internal const string InsertClaimSql = """
        INSERT INTO [dbo].[POSM_SharedHeldOrderClaim]
        ([ClaimGuid], [HoldGuid], [StoreCode], [ClaimantDeviceCode], [CashierId], [CashierName],
         [IdempotencyKey], [Fingerprint], [Status], [IsBlocking], [Revision], [CreatedAtUtc],
         [UpdatedAtUtc], [ExpiresAtUtc], [ActivatedAtUtc], [ReleasedAtUtc],
         [ForceReleased], [ForceReleaseReason], [ForceReleaseCashierId],
         [ForceReleaseCashierName], [ForceReleaseCashierUserGuid], [ForceReleasedAtUtc])
        VALUES
        (@ClaimGuid, @HoldGuid, @StoreCode, @ClaimantDeviceCode, @CashierId, @CashierName,
         @IdempotencyKey, @Fingerprint, @Status, @IsBlocking, @Revision, @CreatedAtUtc,
         @UpdatedAtUtc, @ExpiresAtUtc, @ActivatedAtUtc, @ReleasedAtUtc,
         @ForceReleased, @ForceReleaseReason, @ForceReleaseCashierId,
         @ForceReleaseCashierName, @ForceReleaseCashierUserGuid, @ForceReleasedAtUtc);
        """;

    internal const string UpdateHoldSql = """
        UPDATE [dbo].[POSM_SharedHeldOrder]
        SET [Status] = @Status, [UpdatedAtUtc] = @UpdatedAtUtc, [Revision] = @Revision
        WHERE [HoldGuid] = @HoldGuid AND [Revision] = @ExpectedRevision;
        """;

    internal const string CancelHoldSql = """
        UPDATE [dbo].[POSM_SharedHeldOrder]
        SET [Status] = @Status, [UpdatedAtUtc] = @UpdatedAtUtc, [Revision] = @Revision
        WHERE [HoldGuid] = @HoldGuid
          AND [StoreCode] = @StoreCode
          AND [DeviceCode] = @DeviceCode
          AND [Status] = @ExpectedStatus
          AND [Revision] = @ExpectedRevision;
        """;

    internal const string UpdateClaimSql = """
        UPDATE [dbo].[POSM_SharedHeldOrderClaim]
        SET [Status] = @Status, [IsBlocking] = @IsBlocking, [UpdatedAtUtc] = @UpdatedAtUtc,
            [ExpiresAtUtc] = @ExpiresAtUtc, [ActivatedAtUtc] = @ActivatedAtUtc,
            [ReleasedAtUtc] = @ReleasedAtUtc, [ForceReleased] = @ForceReleased,
            [ForceReleaseReason] = @ForceReleaseReason,
            [ForceReleaseCashierId] = @ForceReleaseCashierId,
            [ForceReleaseCashierName] = @ForceReleaseCashierName,
            [ForceReleaseCashierUserGuid] = @ForceReleaseCashierUserGuid,
            [ForceReleasedAtUtc] = @ForceReleasedAtUtc,
            [Revision] = @Revision
        WHERE [ClaimGuid] = @ClaimGuid AND [Revision] = @ExpectedRevision;
        """;

    public Task<SharedHeldOrderRecord?> GetHoldAsync(
        Guid holdGuid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return QueryHoldSingleAsync(
            $"""
             SELECT TOP 1 {HoldSelectColumns}
             FROM [dbo].[POSM_SharedHeldOrder]
             WHERE [HoldGuid] = @HoldGuid;
             """,
            [new SugarParameter("@HoldGuid", holdGuid)],
            cancellationToken);
    }

    public Task<SharedHeldOrderRecord?> GetHoldByIdempotencyAsync(
        string storeCode,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return QueryHoldSingleAsync(
            $"""
             SELECT TOP 1 {HoldSelectColumns}
             FROM [dbo].[POSM_SharedHeldOrder]
             WHERE [StoreCode] = @StoreCode AND [IdempotencyKey] = @IdempotencyKey;
             """,
            [
                new SugarParameter("@StoreCode", storeCode),
                new SugarParameter("@IdempotencyKey", idempotencyKey)
            ],
            cancellationToken);
    }

    public async Task<bool> TryInsertHoldAsync(
        SharedHeldOrderRecord hold,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = dbContext.PosmDb;
        await using var processLock = await SharedHeldOrderMutationLock.AcquireProcessAsync(
            hold.HoldGuid,
            cancellationToken);
        await db.Ado.BeginTranAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            await SharedHeldOrderMutationLock.AcquireDatabaseAsync(db, hold.HoldGuid);
            var locked = await SharedHeldOrderMutationLock.LockHoldAsync(
                db,
                hold.HoldGuid,
                cancellationToken);
            if (locked is not null)
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            var idempotencyHit = await db.Ado.SqlQuerySingleAsync<HoldIdRow>(
                """
                SELECT TOP 1 [HoldGuid]
                FROM [dbo].[POSM_SharedHeldOrder]
                WHERE [StoreCode] = @StoreCode AND [IdempotencyKey] = @IdempotencyKey;
                """,
                new SugarParameter("@StoreCode", hold.StoreCode),
                new SugarParameter("@IdempotencyKey", hold.IdempotencyKey));
            if (idempotencyHit is not null)
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            var inserted = await db.Ado.ExecuteCommandAsync(
                InsertHoldSql,
                ToHoldParameters(hold)) == 1;
            await db.Ado.CommitTranAsync();
            return inserted;
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            await db.Ado.RollbackTranAsync();
            return false;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<bool> TryUpdateHoldAsync(
        SharedHeldOrderRecord hold,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var updated = await dbContext.PosmDb.Ado.ExecuteCommandAsync(
            UpdateHoldSql,
            new SugarParameter("@Status", hold.Status.ToString()),
            new SugarParameter("@UpdatedAtUtc", hold.UpdatedAtUtc.UtcDateTime),
            new SugarParameter("@Revision", hold.Revision),
            new SugarParameter("@HoldGuid", hold.HoldGuid),
            new SugarParameter("@ExpectedRevision", expectedRevision));
        return updated == 1;
    }

    public async Task<bool> TryCancelHoldAsync(
        Guid holdGuid,
        string storeCode,
        string deviceCode,
        long expectedRevision,
        DateTimeOffset cancelledAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = dbContext.PosmDb;
        await using var processLock = await SharedHeldOrderMutationLock.AcquireProcessAsync(
            holdGuid,
            cancellationToken);
        await db.Ado.BeginTranAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            // 取消与 prepare 使用同一 hold 锁，避免先检查无 claim 后被并发 prepare 插入。
            await SharedHeldOrderMutationLock.AcquireDatabaseAsync(db, holdGuid);
            var lockedHold = await SharedHeldOrderMutationLock.LockCancelHoldAsync(
                db,
                holdGuid,
                cancellationToken);
            if (lockedHold is null ||
                lockedHold.Revision != expectedRevision ||
                !string.Equals(lockedHold.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(lockedHold.DeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    lockedHold.Status,
                    SharedHeldOrderStatus.Pending.ToString(),
                    StringComparison.Ordinal))
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            var blockingClaim = await SharedHeldOrderMutationLock.LockBlockingClaimAsync(
                db,
                holdGuid,
                cancellationToken);
            if (blockingClaim is not null)
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            var updated = await db.Ado.ExecuteCommandAsync(
                CancelHoldSql,
                new SugarParameter("@Status", SharedHeldOrderStatus.Cancelled.ToString()),
                new SugarParameter("@ExpectedStatus", SharedHeldOrderStatus.Pending.ToString()),
                new SugarParameter("@UpdatedAtUtc", cancelledAtUtc.UtcDateTime),
                new SugarParameter("@Revision", expectedRevision + 1),
                new SugarParameter("@HoldGuid", holdGuid),
                // 使用已锁定的原始值，兼容允许大小写不敏感的身份比较，同时仍由 CAS 校验数据库事实。
                new SugarParameter("@StoreCode", lockedHold.StoreCode),
                new SugarParameter("@DeviceCode", lockedHold.DeviceCode),
                new SugarParameter("@ExpectedRevision", expectedRevision));
            if (updated != 1)
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            await db.Ado.CommitTranAsync();
            return true;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public Task<IReadOnlyList<SharedHeldOrderRecord>> ListPendingAsync(
        string storeCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return QueryHoldListAsync(
            $"""
             SELECT {HoldSelectColumns}
             FROM [dbo].[POSM_SharedHeldOrder]
             WHERE [StoreCode] = @StoreCode AND [Status] = N'Pending'
             ORDER BY [CreatedAtUtc], [HoldGuid];
             """,
            [new SugarParameter("@StoreCode", storeCode)],
            cancellationToken);
    }

    public Task<SharedHeldOrderClaimRecord?> GetClaimAsync(
        Guid claimGuid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return QueryClaimSingleAsync(
            $"""
             SELECT TOP 1 {ClaimSelectColumns}
             FROM [dbo].[POSM_SharedHeldOrderClaim]
             WHERE [ClaimGuid] = @ClaimGuid;
             """,
            [new SugarParameter("@ClaimGuid", claimGuid)],
            cancellationToken);
    }

    public Task<SharedHeldOrderClaimRecord?> GetBlockingClaimAsync(
        Guid holdGuid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return QueryClaimSingleAsync(
            $"""
             SELECT TOP 1 {ClaimSelectColumns}
             FROM [dbo].[POSM_SharedHeldOrderClaim]
             WHERE [HoldGuid] = @HoldGuid AND [IsBlocking] = 1
             ORDER BY [CreatedAtUtc], [ClaimGuid];
             """,
            [new SugarParameter("@HoldGuid", holdGuid)],
            cancellationToken);
    }

    public async Task<bool> TryInsertClaimAsync(
        SharedHeldOrderClaimRecord claim,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = dbContext.PosmDb;
        await using var processLock = await SharedHeldOrderMutationLock.AcquireProcessAsync(
            claim.HoldGuid,
            cancellationToken);
        await db.Ado.BeginTranAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            await SharedHeldOrderMutationLock.AcquireDatabaseAsync(db, claim.HoldGuid);
            var locked = await SharedHeldOrderMutationLock.LockHoldAsync(
                db,
                claim.HoldGuid,
                cancellationToken);
            if (locked is null)
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            // Completed/Claimed 的 hold 不得再创建新 claim；仅 Pending 允许。
            if (!string.Equals(locked.Status, SharedHeldOrderStatus.Pending.ToString(), StringComparison.Ordinal))
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            var blocking = await db.Ado.SqlQuerySingleAsync<ClaimIdRow>(
                """
                SELECT TOP 1 [ClaimGuid]
                FROM [dbo].[POSM_SharedHeldOrderClaim]
                WHERE [HoldGuid] = @HoldGuid AND [IsBlocking] = 1;
                """,
                new SugarParameter("@HoldGuid", claim.HoldGuid));
            if (blocking is not null)
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            var inserted = await db.Ado.ExecuteCommandAsync(
                InsertClaimSql,
                ToClaimParameters(claim)) == 1;
            await db.Ado.CommitTranAsync();
            return inserted;
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            await db.Ado.RollbackTranAsync();
            return false;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<bool> TryUpdateClaimAsync(
        SharedHeldOrderClaimRecord claim,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var updated = await dbContext.PosmDb.Ado.ExecuteCommandAsync(
            UpdateClaimSql,
            new SugarParameter("@Status", claim.Status.ToString()),
            new SugarParameter("@IsBlocking", claim.IsBlocking),
            new SugarParameter("@UpdatedAtUtc", claim.UpdatedAtUtc.UtcDateTime),
            new SugarParameter("@ExpiresAtUtc", claim.ExpiresAtUtc?.UtcDateTime),
            new SugarParameter("@ActivatedAtUtc", claim.ActivatedAtUtc?.UtcDateTime),
            new SugarParameter("@ReleasedAtUtc", claim.ReleasedAtUtc?.UtcDateTime),
            new SugarParameter("@ForceReleased", claim.ForceReleased),
            new SugarParameter("@ForceReleaseReason", claim.ForceReleaseReason),
            new SugarParameter("@ForceReleaseCashierId", claim.ForceReleaseCashierId),
            new SugarParameter("@ForceReleaseCashierName", claim.ForceReleaseCashierName),
            new SugarParameter("@ForceReleaseCashierUserGuid", claim.ForceReleaseCashierUserGuid),
            new SugarParameter("@ForceReleasedAtUtc", claim.ForceReleasedAtUtc?.UtcDateTime),
            new SugarParameter("@Revision", claim.Revision),
            new SugarParameter("@ClaimGuid", claim.ClaimGuid),
            new SugarParameter("@ExpectedRevision", expectedRevision));
        return updated == 1;
    }

    public async Task<bool> TryUpdateClaimAndHoldAsync(
        SharedHeldOrderClaimRecord claim,
        long expectedClaimRevision,
        SharedHeldOrderRecord hold,
        long expectedHoldRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = dbContext.PosmDb;
        await using var processLock = await SharedHeldOrderMutationLock.AcquireProcessAsync(
            hold.HoldGuid,
            cancellationToken);
        await db.Ado.BeginTranAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            // 先取同一 hold 的 applock + UPDLOCK，串行化所有 claim/hold 变更。
            await SharedHeldOrderMutationLock.AcquireDatabaseAsync(db, hold.HoldGuid);
            var lockedHold = await SharedHeldOrderMutationLock.LockHoldAsync(
                db,
                hold.HoldGuid,
                cancellationToken);
            if (lockedHold is null || lockedHold.Revision != expectedHoldRevision)
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            var lockedClaim = await db.Ado.SqlQuerySingleAsync<SharedHeldOrderMutationLock.ClaimLockRow>(
                SharedHeldOrderMutationLock.GetClaimLockSql(db.CurrentConnectionConfig.DbType),
                new SugarParameter("@ClaimGuid", claim.ClaimGuid));
            if (lockedClaim is null ||
                lockedClaim.HoldGuid != hold.HoldGuid ||
                lockedClaim.Revision != expectedClaimRevision)
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            var claimUpdated = await db.Ado.ExecuteCommandAsync(
                UpdateClaimSql,
                ToClaimParameters(claim)
                    .Append(new SugarParameter("@ExpectedRevision", expectedClaimRevision))
                    .ToArray()) == 1;
            var holdUpdated = await db.Ado.ExecuteCommandAsync(
                UpdateHoldSql,
                new SugarParameter("@Status", hold.Status.ToString()),
                new SugarParameter("@UpdatedAtUtc", hold.UpdatedAtUtc.UtcDateTime),
                new SugarParameter("@Revision", hold.Revision),
                new SugarParameter("@HoldGuid", hold.HoldGuid),
                new SugarParameter("@ExpectedRevision", expectedHoldRevision)) == 1;
            if (!claimUpdated || !holdUpdated)
            {
                await db.Ado.RollbackTranAsync();
                return false;
            }

            await db.Ado.CommitTranAsync();
            return true;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public Task<IReadOnlyList<SharedHeldOrderClaimRecord>> ListMyClaimsAsync(
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return QueryClaimListAsync(
            $"""
             SELECT {ClaimSelectColumns}
             FROM [dbo].[POSM_SharedHeldOrderClaim]
             WHERE [StoreCode] = @StoreCode
               AND [ClaimantDeviceCode] = @DeviceCode
               AND [IsBlocking] = 1
             ORDER BY [CreatedAtUtc], [ClaimGuid];
             """,
            [
                new SugarParameter("@StoreCode", storeCode),
                new SugarParameter("@DeviceCode", deviceCode)
            ],
            cancellationToken);
    }

    private async Task<SharedHeldOrderRecord?> QueryHoldSingleAsync(
        string sql,
        SugarParameter[] parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<HoldRow>(sql, parameters);
        return row is null ? null : Map(row);
    }

    private async Task<IReadOnlyList<SharedHeldOrderRecord>> QueryHoldListAsync(
        string sql,
        SugarParameter[] parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await dbContext.PosmDb.Ado.SqlQueryAsync<HoldRow>(sql, parameters);
        return rows.Select(Map).ToArray();
    }

    private async Task<SharedHeldOrderClaimRecord?> QueryClaimSingleAsync(
        string sql,
        SugarParameter[] parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<ClaimRow>(sql, parameters);
        return row is null ? null : Map(row);
    }

    private async Task<IReadOnlyList<SharedHeldOrderClaimRecord>> QueryClaimListAsync(
        string sql,
        SugarParameter[] parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await dbContext.PosmDb.Ado.SqlQueryAsync<ClaimRow>(sql, parameters);
        return rows.Select(Map).ToArray();
    }

    private static SugarParameter[] ToHoldParameters(SharedHeldOrderRecord hold) =>
    [
        new("@HoldGuid", hold.HoldGuid),
        new("@StoreCode", hold.StoreCode),
        new("@DeviceCode", hold.DeviceCode),
        new("@CashierId", hold.CashierId),
        new("@CashierName", hold.CashierName),
        new("@PayloadVersion", hold.PayloadVersion),
        new("@PayloadCiphertext", hold.PayloadCiphertext),
        new("@Fingerprint", hold.Fingerprint),
        new("@IdempotencyKey", hold.IdempotencyKey),
        new("@Status", hold.Status.ToString()),
        new("@Revision", hold.Revision),
        new("@CreatedAtUtc", hold.CreatedAtUtc.UtcDateTime),
        new("@UpdatedAtUtc", hold.UpdatedAtUtc.UtcDateTime),
        new("@HeldAtUtc", hold.HeldAtUtc.UtcDateTime),
        new("@LineCount", hold.LineCount),
        new("@TotalCents", hold.TotalCents),
        new("@DiscountCents", hold.DiscountCents),
        new("@ActualCents", hold.ActualCents)
    ];

    private static SugarParameter[] ToClaimParameters(SharedHeldOrderClaimRecord claim) =>
    [
        new("@ClaimGuid", claim.ClaimGuid),
        new("@HoldGuid", claim.HoldGuid),
        new("@StoreCode", claim.StoreCode),
        new("@ClaimantDeviceCode", claim.ClaimantDeviceCode),
        new("@CashierId", claim.CashierId),
        new("@CashierName", claim.CashierName),
        new("@IdempotencyKey", claim.IdempotencyKey),
        new("@Fingerprint", claim.Fingerprint),
        new("@Status", claim.Status.ToString()),
        new("@IsBlocking", claim.IsBlocking),
        new("@Revision", claim.Revision),
        new("@CreatedAtUtc", claim.CreatedAtUtc.UtcDateTime),
        new("@UpdatedAtUtc", claim.UpdatedAtUtc.UtcDateTime),
        new("@ExpiresAtUtc", claim.ExpiresAtUtc?.UtcDateTime),
        new("@ActivatedAtUtc", claim.ActivatedAtUtc?.UtcDateTime),
        new("@ReleasedAtUtc", claim.ReleasedAtUtc?.UtcDateTime),
        new("@ForceReleased", claim.ForceReleased),
        new("@ForceReleaseReason", claim.ForceReleaseReason),
        new("@ForceReleaseCashierId", claim.ForceReleaseCashierId),
        new("@ForceReleaseCashierName", claim.ForceReleaseCashierName),
        new("@ForceReleaseCashierUserGuid", claim.ForceReleaseCashierUserGuid),
        new("@ForceReleasedAtUtc", claim.ForceReleasedAtUtc?.UtcDateTime)
    ];

    private static SharedHeldOrderRecord Map(HoldRow row) => new(
        row.HoldGuid,
        row.StoreCode,
        row.DeviceCode,
        row.CashierId,
        row.CashierName,
        row.PayloadVersion,
        row.PayloadCiphertext,
        row.Fingerprint,
        row.IdempotencyKey,
        Enum.Parse<SharedHeldOrderStatus>(row.Status, ignoreCase: true),
        row.Revision,
        ToUtc(row.CreatedAtUtc),
        ToUtc(row.UpdatedAtUtc),
        ToUtc(row.HeldAtUtc),
        row.LineCount,
        row.TotalCents,
        row.DiscountCents,
        row.ActualCents);

    private static SharedHeldOrderClaimRecord Map(ClaimRow row) => new(
        row.ClaimGuid,
        row.HoldGuid,
        row.StoreCode,
        row.ClaimantDeviceCode,
        row.CashierId,
        row.CashierName,
        row.IdempotencyKey,
        row.Fingerprint,
        Enum.Parse<SharedHeldOrderClaimStatus>(row.Status, ignoreCase: true),
        row.IsBlocking,
        row.Revision,
        ToUtc(row.CreatedAtUtc),
        ToUtc(row.UpdatedAtUtc),
        row.ExpiresAtUtc is null ? null : ToUtc(row.ExpiresAtUtc.Value),
        row.ActivatedAtUtc is null ? null : ToUtc(row.ActivatedAtUtc.Value),
        row.ReleasedAtUtc is null ? null : ToUtc(row.ReleasedAtUtc.Value),
        row.ForceReleased,
        row.ForceReleaseReason,
        row.ForceReleaseCashierId,
        row.ForceReleaseCashierName,
        row.ForceReleaseCashierUserGuid,
        row.ForceReleasedAtUtc is null ? null : ToUtc(row.ForceReleasedAtUtc.Value));

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("2601", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("2627", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("UX_POSM_SharedHeldOrder", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class HoldIdRow
    {
        public Guid HoldGuid { get; set; }
    }

    private sealed class ClaimIdRow
    {
        public Guid ClaimGuid { get; set; }
    }

    private sealed class HoldRow
    {
        public Guid HoldGuid { get; set; }

        public string StoreCode { get; set; } = string.Empty;

        public string DeviceCode { get; set; } = string.Empty;

        public string CashierId { get; set; } = string.Empty;

        public string CashierName { get; set; } = string.Empty;

        public int PayloadVersion { get; set; }

        public string PayloadCiphertext { get; set; } = string.Empty;

        public string Fingerprint { get; set; } = string.Empty;

        public string IdempotencyKey { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public long Revision { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public DateTime HeldAtUtc { get; set; }

        public int LineCount { get; set; }

        public long TotalCents { get; set; }

        public long DiscountCents { get; set; }

        public long ActualCents { get; set; }
    }

    private sealed class ClaimRow
    {
        public Guid ClaimGuid { get; set; }

        public Guid HoldGuid { get; set; }

        public string StoreCode { get; set; } = string.Empty;

        public string ClaimantDeviceCode { get; set; } = string.Empty;

        public string CashierId { get; set; } = string.Empty;

        public string CashierName { get; set; } = string.Empty;

        public string IdempotencyKey { get; set; } = string.Empty;

        public string Fingerprint { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public bool IsBlocking { get; set; }

        public long Revision { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public DateTime? ExpiresAtUtc { get; set; }

        public DateTime? ActivatedAtUtc { get; set; }

        public DateTime? ReleasedAtUtc { get; set; }

        public bool ForceReleased { get; set; }

        public string? ForceReleaseReason { get; set; }

        public string? ForceReleaseCashierId { get; set; }

        public string? ForceReleaseCashierName { get; set; }

        public string? ForceReleaseCashierUserGuid { get; set; }

        public DateTime? ForceReleasedAtUtc { get; set; }
    }
}

/// <summary>
/// 订单同步事务内的挂单关联：由 SqlSugarOrderRepository.InsertAsync 在既有事务中调用，
/// 使用 applock + UPDLOCK（调用方 Serializable）保证并发下单同一 hold 恰好一个 Primary。
/// 只记录 disposition 与关联键，绝不接触/返回 payload 明文。
/// </summary>
internal static class SharedHeldOrderAssociationStore
{
    internal const string AssociationTable = "[dbo].[POSM_SharedHeldOrderAssociation]";

    internal const string GetDispositionSql = """
        SELECT TOP 1 [Disposition]
        FROM [dbo].[POSM_SharedHeldOrderAssociation]
        WHERE [OrderGuid] = @OrderGuid;
        """;

    internal const string HasPrimarySql = """
        SELECT TOP 1 [OrderGuid]
        FROM [dbo].[POSM_SharedHeldOrderAssociation] WITH (UPDLOCK)
        WHERE [HoldGuid] = @HoldGuid AND [Disposition] = N'Primary';
        """;

    internal const string InsertAssociationSql = """
        INSERT INTO [dbo].[POSM_SharedHeldOrderAssociation]
        ([OrderGuid], [HoldGuid], [StoreCode], [ClaimGuid], [Disposition], [CreatedAtUtc])
        VALUES
        (@OrderGuid, @HoldGuid, @StoreCode, @ClaimGuid, @Disposition, @CreatedAtUtc);
        """;

    internal const string CompleteHoldSql = """
        UPDATE [dbo].[POSM_SharedHeldOrder]
        SET [Status] = N'Completed', [UpdatedAtUtc] = @UpdatedAtUtc, [Revision] = [Revision] + 1
        WHERE [HoldGuid] = @HoldGuid AND [Revision] = @ExpectedRevision
          AND [Status] IN (N'Pending', N'Claimed');
        """;

    internal const string CompleteClaimSql = """
        UPDATE [dbo].[POSM_SharedHeldOrderClaim]
        SET [Status] = N'Completed', [IsBlocking] = 0, [UpdatedAtUtc] = @UpdatedAtUtc,
            [Revision] = [Revision] + 1
        WHERE [ClaimGuid] = @ClaimGuid AND [HoldGuid] = @HoldGuid AND [Status] = N'Active';
        """;

    internal const string SupersedeBlockingClaimsSql = """
        UPDATE [dbo].[POSM_SharedHeldOrderClaim]
        SET [Status] = N'Superseded', [IsBlocking] = 0, [UpdatedAtUtc] = @UpdatedAtUtc,
            [Revision] = [Revision] + 1
        WHERE [HoldGuid] = @HoldGuid AND [IsBlocking] = 1;
        """;

    internal const string SupersedePreparedClaimSql = """
        UPDATE [dbo].[POSM_SharedHeldOrderClaim]
        SET [Status] = N'Superseded', [IsBlocking] = 0, [UpdatedAtUtc] = @UpdatedAtUtc,
            [Revision] = [Revision] + 1
        WHERE [ClaimGuid] = @ClaimGuid AND [HoldGuid] = @HoldGuid
          AND [Status] = N'Prepared' AND [IsBlocking] = 1;
        """;

    // 非 SQL Server（SQLite 等测试/开发 provider）下复用 mutation lock 的方言清理，
    // 并去掉 SQL Server 的 N'' 字符串前缀（SQLite 不接受该写法）；SQL Server 保持原契约不变。
    private static string ForProvider(DbType dbType, string sqlServerSql) =>
        dbType == DbType.SqlServer
            ? sqlServerSql
            : SharedHeldOrderMutationLock.ToNonSqlServerSql(sqlServerSql)
                .Replace("N'", "'", StringComparison.Ordinal);

    internal static async Task<HeldOrderDisposition> GetDispositionAsync(
        ISqlSugarClient db,
        Guid orderGuid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = await db.Ado.SqlQuerySingleAsync<DispositionRow>(
            ForProvider(db.CurrentConnectionConfig.DbType, GetDispositionSql),
            new SugarParameter("@OrderGuid", orderGuid));
        return row is null
            ? HeldOrderDisposition.None
            : ParseDisposition(row.Disposition);
    }

    internal static async Task<HeldOrderDisposition> AssociateAsync(
        ISqlSugarClient db,
        Guid orderGuid,
        string storeCode,
        HeldOrderSourceDto source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dbType = db.CurrentConnectionConfig.DbType;
        // 调用方事务内执行：SQL Server 下必须先 Serializable，再以 applock/UPDLOCK 串行化。
        await SharedHeldOrderMutationLock.AcquireDatabaseAsync(db, source.HoldGuid);
        var hold = await SharedHeldOrderMutationLock.LockHoldAsync(
            db,
            source.HoldGuid,
            cancellationToken);

        var existing = await db.Ado.SqlQuerySingleAsync<DispositionRow>(
            ForProvider(dbType, GetDispositionSql),
            new SugarParameter("@OrderGuid", orderGuid));
        if (existing is not null)
        {
            return ParseDisposition(existing.Disposition);
        }

        // 严格来源组合：HoldGuid 非空；RemoteClaim 必须非空 ClaimGuid；OfflineOrigin 必须 ClaimGuid=null。
        // 无效来源不拒绝正式订单，只记录 Unmatched。
        if (!IsValidSource(source))
        {
            await InsertAssociationAsync(
                db,
                orderGuid,
                storeCode,
                source,
                HeldOrderDisposition.Unmatched,
                now,
                cancellationToken);
            return HeldOrderDisposition.Unmatched;
        }

        if (hold is null ||
            !string.Equals(hold.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase))
        {
            await InsertAssociationAsync(
                db,
                orderGuid,
                storeCode,
                source,
                HeldOrderDisposition.Unmatched,
                now,
                cancellationToken);
            return HeldOrderDisposition.Unmatched;
        }

        var hasPrimary = await db.Ado.SqlQuerySingleAsync<OrderGuidRow>(
            ForProvider(dbType, HasPrimarySql),
            new SugarParameter("@HoldGuid", source.HoldGuid)) is not null;

        // Remote claim 归属校验：claim 必须存在且归属同 hold/store。
        SharedHeldOrderMutationLock.ClaimLockRow? claim = null;
        if (source.Kind == HeldOrderSourceKind.RemoteClaim)
        {
            claim = await db.Ado.SqlQuerySingleAsync<SharedHeldOrderMutationLock.ClaimLockRow>(
                SharedHeldOrderMutationLock.GetClaimLockSql(dbType),
                new SugarParameter("@ClaimGuid", source.ClaimGuid));
            if (claim is null ||
                claim.HoldGuid != source.HoldGuid ||
                !string.Equals(claim.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase))
            {
                await InsertAssociationAsync(
                    db,
                    orderGuid,
                    storeCode,
                    source,
                    HeldOrderDisposition.Unmatched,
                    now,
                    cancellationToken);
                return HeldOrderDisposition.Unmatched;
            }
        }

        if (hasPrimary)
        {
            // 已有 Primary：只要 claim 存在且归属同 hold/store，不论 Active/Completed/Released/Superseded
            // （离线竞态）均 Duplicate；claim 不存在/归属错误已在上方判 Unmatched。
            await InsertAssociationAsync(
                db,
                orderGuid,
                storeCode,
                source,
                HeldOrderDisposition.Duplicate,
                now,
                cancellationToken);
            return HeldOrderDisposition.Duplicate;
        }

        // 创建 Primary 前锁定 hold 必须是 Pending/Claimed；否则先不改状态，记录 Unmatched 并接受订单。
        if (hold.Status is not ("Pending" or "Claimed"))
        {
            await InsertAssociationAsync(
                db,
                orderGuid,
                storeCode,
                source,
                HeldOrderDisposition.Unmatched,
                now,
                cancellationToken);
            return HeldOrderDisposition.Unmatched;
        }

        if (source.Kind == HeldOrderSourceKind.RemoteClaim)
        {
            // 合法 claim 两种：Active 走既有完成语义；Prepared 表示设备已 prepare 但尚未
            // activate，首笔真实订单仍必须赢下 Primary，并在同一事务内将 Prepared claim
            // 推进为 Superseded（解除 blocking），不得先记 Unmatched。
            // Released/Completed/Superseded 等终态不匹配，保持 Unmatched 且不改状态。
            if (string.Equals(
                    claim!.Status,
                    SharedHeldOrderClaimStatus.Active.ToString(),
                    StringComparison.Ordinal))
            {
                await ExecuteRequiredUpdateAsync(
                    db,
                    CompleteHoldSql,
                    now,
                    new SugarParameter("@HoldGuid", source.HoldGuid),
                    new SugarParameter("@ExpectedRevision", hold.Revision));
                await ExecuteRequiredUpdateAsync(
                    db,
                    CompleteClaimSql,
                    now,
                    new SugarParameter("@ClaimGuid", source.ClaimGuid),
                    new SugarParameter("@HoldGuid", source.HoldGuid));
                await InsertAssociationAsync(
                    db,
                    orderGuid,
                    storeCode,
                    source,
                    HeldOrderDisposition.Primary,
                    now,
                    cancellationToken);
                return HeldOrderDisposition.Primary;
            }

            if (string.Equals(
                    claim!.Status,
                    SharedHeldOrderClaimStatus.Prepared.ToString(),
                    StringComparison.Ordinal))
            {
                await ExecuteRequiredUpdateAsync(
                    db,
                    CompleteHoldSql,
                    now,
                    new SugarParameter("@HoldGuid", source.HoldGuid),
                    new SugarParameter("@ExpectedRevision", hold.Revision));
                await ExecuteRequiredUpdateAsync(
                    db,
                    SupersedePreparedClaimSql,
                    now,
                    new SugarParameter("@ClaimGuid", source.ClaimGuid),
                    new SugarParameter("@HoldGuid", source.HoldGuid));
                await InsertAssociationAsync(
                    db,
                    orderGuid,
                    storeCode,
                    source,
                    HeldOrderDisposition.Primary,
                    now,
                    cancellationToken);
                return HeldOrderDisposition.Primary;
            }

            await InsertAssociationAsync(
                db,
                orderGuid,
                storeCode,
                source,
                HeldOrderDisposition.Unmatched,
                now,
                cancellationToken);
            return HeldOrderDisposition.Unmatched;
        }

        // OfflineOrigin：Pending/Claimed hold 可直接赢并 supersede blocking claims。
        await ExecuteRequiredUpdateAsync(
            db,
            CompleteHoldSql,
            now,
            new SugarParameter("@HoldGuid", source.HoldGuid),
            new SugarParameter("@ExpectedRevision", hold.Revision));
        await ExecuteUpdateAsync(
            db,
            SupersedeBlockingClaimsSql,
            now,
            new SugarParameter("@HoldGuid", source.HoldGuid));
        await InsertAssociationAsync(
            db,
            orderGuid,
            storeCode,
            source,
            HeldOrderDisposition.Primary,
            now,
            cancellationToken);
        return HeldOrderDisposition.Primary;
    }

    private static bool IsValidSource(HeldOrderSourceDto source)
    {
        if (source is null || source.HoldGuid == Guid.Empty)
        {
            return false;
        }

        return source.Kind switch
        {
            HeldOrderSourceKind.RemoteClaim => source.ClaimGuid is not null,
            HeldOrderSourceKind.OfflineOrigin => source.ClaimGuid is null,
            _ => false
        };
    }

    private static async Task InsertAssociationAsync(
        ISqlSugarClient db,
        Guid orderGuid,
        string storeCode,
        HeldOrderSourceDto source,
        HeldOrderDisposition disposition,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inserted = await db.Ado.ExecuteCommandAsync(
            ForProvider(db.CurrentConnectionConfig.DbType, InsertAssociationSql),
            new SugarParameter("@OrderGuid", orderGuid),
            new SugarParameter("@HoldGuid", source.HoldGuid),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@ClaimGuid", source.ClaimGuid),
            new SugarParameter("@Disposition", disposition.ToString()),
            new SugarParameter("@CreatedAtUtc", now.UtcDateTime)) == 1;
        if (!inserted)
        {
            throw new SharedHeldOrderException(
                SharedHeldOrderErrorCodes.Busy,
                "Held order association could not be recorded.");
        }
    }

    private static Task<int> ExecuteUpdateAsync(
        ISqlSugarClient db,
        string sql,
        DateTimeOffset now,
        params SugarParameter[] parameters)
    {
        var all = parameters
            .Append(new SugarParameter("@UpdatedAtUtc", now.UtcDateTime))
            .ToArray();
        return db.Ado.ExecuteCommandAsync(
            ForProvider(db.CurrentConnectionConfig.DbType, sql),
            all);
    }

    private static async Task ExecuteRequiredUpdateAsync(
        ISqlSugarClient db,
        string sql,
        DateTimeOffset now,
        params SugarParameter[] parameters)
    {
        var affected = await ExecuteUpdateAsync(db, sql, now, parameters);
        // applock + 锁定预检后仍更新 0 行属于数据库不一致：抛错回滚，绝不能带着 0 行更新创建 Primary。
        if (affected != 1)
        {
            throw new SharedHeldOrderException(
                SharedHeldOrderErrorCodes.Invalid,
                "Held order completion update affected an unexpected number of rows; order insert rolled back.");
        }
    }

    private static HeldOrderDisposition ParseDisposition(string value) =>
        Enum.TryParse<HeldOrderDisposition>(value, ignoreCase: true, out var disposition)
            ? disposition
            : HeldOrderDisposition.Unmatched;

    private sealed class DispositionRow
    {
        public string Disposition { get; set; } = string.Empty;
    }

    private sealed class OrderGuidRow
    {
        public Guid OrderGuid { get; set; }
    }
}
