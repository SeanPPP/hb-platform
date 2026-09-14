using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesStatisticsDateExecutionGuardSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable =
        "SALES_STATISTICS_RECOVERY_SQLSERVER_TEST_CONNECTION";

    public SalesStatisticsDateExecutionGuardSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过销售统计日期会话锁 SQL Server 验证。";
        }
    }
}

/// <summary>
/// 真实 SQL Server 故障注入：guard 必须与 SqlSugar 写连接共用同一 Session，且 KILL 后
/// 任何下一次写入都只能失败，不能透明重连为旧 worker 重新获得写入机会。
/// </summary>
public sealed class SalesStatisticsDateExecutionGuardSqlServerTests
{
    private const string TaskType = "DailyStatisticsAlignmentFullRefresh";

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 同日Session锁忙时跳过_不同日期可并行_旧标记租约在旧Session退出后可接管()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 9, 14);
        var first = fixture.OpenContext("guard-a");
        await using var firstGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            first.Context,
            date,
            NullLogger.Instance
        );
        Assert.True(firstGuard.Acquired);
        var firstLease = await first.LeaseService.TryAcquireSessionGuardedAsync(
            "DailyStatisticsAlignmentFullRefresh",
            date.ToString("yyyy-MM-dd"),
            firstGuard
        );
        Assert.True(firstLease.Acquired);
        Assert.StartsWith(SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix, firstLease.Lease!.LeaseToken);

        var sameDate = fixture.OpenContext("guard-b");
        await using var sameDateGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            sameDate.Context,
            date,
            NullLogger.Instance
        );
        Assert.False(sameDateGuard.Acquired);

        var otherDate = fixture.OpenContext("guard-c");
        await using var otherDateGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            otherDate.Context,
            date.AddDays(1),
            NullLogger.Instance
        );
        Assert.True(otherDateGuard.Acquired);
        await otherDateGuard.DisposeAsync();

        await firstGuard.DisposeAsync();
        var successor = fixture.OpenContext("guard-successor");
        await using var successorGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            successor.Context,
            date,
            NullLogger.Instance
        );
        Assert.True(successorGuard.Acquired);
        var successorLease = await successor.LeaseService.TryAcquireSessionGuardedAsync(
            "DailyStatisticsAlignmentFullRefresh",
            date.ToString("yyyy-MM-dd"),
            successorGuard
        );
        Assert.True(successorLease.Acquired);
        Assert.NotEqual(firstLease.Lease!.LeaseToken, successorLease.Lease!.LeaseToken);
    }

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 写会话被终止后旧worker不能透明重连写入或覆盖继任租约()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 9, 14);
        var oldWorker = fixture.OpenContext("guard-old");
        await using var oldGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            oldWorker.Context,
            date,
            NullLogger.Instance
        );
        Assert.True(oldGuard.Acquired);
        var oldLease = await oldWorker.LeaseService.TryAcquireSessionGuardedAsync(
            "DailyStatisticsAlignmentFullRefresh",
            date.ToString("yyyy-MM-dd"),
            oldGuard
        );
        Assert.True(oldLease.Acquired);

        await fixture.KillAsync(oldGuard.ServerProcessIdForTest);

        await Assert.ThrowsAsync<SalesStatisticsDateExecutionGuardLostException>(() =>
            oldWorker.Context.Db.Insertable(new GuardProbe { Id = 1, Value = "old-first" })
                .ExecuteCommandAsync()
        );
        await Assert.ThrowsAsync<SalesStatisticsDateExecutionGuardLostException>(() =>
            oldWorker.Context.Db.Insertable(new GuardProbe { Id = 2, Value = "old-second" })
                .ExecuteCommandAsync()
        );
        Assert.False(oldGuard.IsActive);

        var successor = fixture.OpenContext("guard-new");
        await using var successorGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            successor.Context,
            date,
            NullLogger.Instance
        );
        Assert.True(successorGuard.Acquired);
        var successorLease = await successor.LeaseService.TryAcquireSessionGuardedAsync(
            "DailyStatisticsAlignmentFullRefresh",
            date.ToString("yyyy-MM-dd"),
            successorGuard
        );
        Assert.True(successorLease.Acquired);
        await successor.Context.Db.Insertable(new GuardProbe { Id = 3, Value = "successor" })
            .ExecuteCommandAsync();

        await Assert.ThrowsAsync<SalesStatisticsDateExecutionGuardLostException>(() =>
            oldWorker.LeaseService.CompleteAsync(
                "DailyStatisticsAlignmentFullRefresh",
                date.ToString("yyyy-MM-dd"),
                oldLease.Lease!.LeaseToken!,
                false,
                "old worker should not write"
            )
        );
        await Assert.ThrowsAsync<SalesStatisticsDateExecutionGuardLostException>(() =>
            oldWorker.LeaseService.CompleteAsync(
                "DailyStatisticsAlignmentFullRefresh",
                date.ToString("yyyy-MM-dd"),
                oldLease.Lease!.LeaseToken!,
                true,
                "old worker must not mark success"
            )
        );

        var rows = await fixture.ReadProbeRowsAsync();
        Assert.Single(rows);
        Assert.Equal("successor", rows[0].Value);
        var persistedLease = await fixture.ReadLeaseAsync(date);
        Assert.Equal(successorLease.Lease!.LeaseToken, persistedLease.LeaseToken);
        Assert.Equal(ScheduledTaskLeaseStatus.Running, persistedLease.Status);
    }

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 事务与FastestBulkCopy必须共用获锁SPID并保留Exclusive应用锁()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 9, 15);
        var writer = fixture.OpenContext("guard-bulk-transaction");
        await using var guard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            writer.Context, date, NullLogger.Instance);
        Assert.True(guard.Acquired);

        await writer.Context.Db.Ado.BeginTranAsync();
        try
        {
            var writerSpid = await writer.Context.Db.Ado.SqlQuerySingleAsync<int>("SELECT @@SPID;");
            Assert.Equal(guard.ServerProcessIdForTest, writerSpid);
            Assert.True(await fixture.HasExclusiveApplicationLockAsync(writerSpid));
            await writer.Context.Db.Fastest<GuardBulkProbe>().BulkCopyAsync(new List<GuardBulkProbe>
            {
                new GuardBulkProbe { Id = 101, Value = "bulk-in-transaction" },
            });
            Assert.True(writer.Context.Db.Ado.IsEnableLogEvent);
            Assert.Equal(1, await fixture.ReadBulkProbeRowCountNoLockAsync());
            Assert.True(await fixture.HasBulkProbeWriteLockAsync(writerSpid));
            await writer.Context.Db.Ado.CommitTranAsync();
        }
        finally
        {
            if (writer.Context.Db.Ado.Transaction != null)
                await writer.Context.Db.Ado.RollbackTranAsync();
        }

        var row = Assert.Single(await fixture.ReadBulkProbeRowsAsync());
        Assert.Equal("bulk-in-transaction", row.Value);
        await guard.DisposeAsync();
        Assert.False(await fixture.HasExclusiveApplicationLockAsync(guard.ServerProcessIdForTest));
    }

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task BulkCopy首次故障后失败状态写入被拦截且事务不遗留旧写入()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 9, 16);
        var oldWorker = fixture.OpenContext("guard-bulk-fault-old");
        await using var oldGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            oldWorker.Context, date, NullLogger.Instance);
        Assert.True(oldGuard.Acquired);
        var oldLease = await oldWorker.LeaseService.TryAcquireSessionGuardedAsync(
            TaskType, date.ToString("yyyy-MM-dd"), oldGuard);
        Assert.True(oldLease.Acquired);

        await oldWorker.Context.Db.Ado.BeginTranAsync();
        await fixture.KillAsync(oldGuard.ServerProcessIdForTest);
        var bulkFailure = await Record.ExceptionAsync(() =>
            oldWorker.Context.Db.Fastest<GuardBulkProbe>().BulkCopyAsync(new List<GuardBulkProbe>
            {
                new GuardBulkProbe { Id = 201, Value = "must-not-persist" },
            })
        );
        Assert.NotNull(bulkFailure);
        Assert.True(oldWorker.Context.Db.Ado.IsEnableLogEvent);
        // 最危险的窗口是 BulkCopy 绕过 AOP 后，内层 catch 在外层 RecordException 前先尝试写 Failed。
        // 这条 SQL 本身必须通过 guard 的原生会话校验被拒绝，不能依赖测试手动熔断。
        var failedWrite = await Record.ExceptionAsync(() =>
            oldWorker.LeaseService.CompleteSessionGuardedAsync(
                TaskType, date.ToString("yyyy-MM-dd"), oldLease.Lease!.LeaseToken!, false, "bulk failed", oldGuard));
        var ordinaryWrite = await Record.ExceptionAsync(() =>
            oldWorker.Context.Db.Insertable(new GuardProbe { Id = 202, Value = "must-not-reconnect" })
                .ExecuteCommandAsync());
        var leaseAfterFailedWrite = await fixture.ReadLeaseAsync(date);
        var probeRowsAfterOrdinaryWrite = await fixture.ReadProbeRowsAsync();
        Assert.Equal(ScheduledTaskLeaseStatus.Running, leaseAfterFailedWrite.Status);
        Assert.IsType<SalesStatisticsDateExecutionGuardLostException>(failedWrite);
        Assert.IsType<SalesStatisticsDateExecutionGuardLostException>(ordinaryWrite);
        Assert.Empty(probeRowsAfterOrdinaryWrite);
        Assert.False(oldGuard.IsActive);
        try
        {
            if (oldWorker.Context.Db.Ado.Transaction != null)
                await oldWorker.Context.Db.Ado.RollbackTranAsync();
        }
        catch
        {
            // KILL 后事务清理可再次失败，不能覆盖首次 BulkCopy 故障。
        }

        Assert.Empty(await fixture.ReadBulkProbeRowsAsync());
        var lease = await fixture.ReadLeaseAsync(date);
        Assert.Equal(oldLease.Lease!.LeaseToken, lease.LeaseToken);
        Assert.Equal(ScheduledTaskLeaseStatus.Running, lease.Status);
    }

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task Commit首次故障不能使旧上下文重连或覆盖未提交租约()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 9, 17);
        var oldWorker = fixture.OpenContext("guard-commit-fault");
        await using var guard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            oldWorker.Context, date, NullLogger.Instance);
        var lease = await oldWorker.LeaseService.TryAcquireSessionGuardedAsync(
            TaskType, date.ToString("yyyy-MM-dd"), guard);
        await oldWorker.Context.Db.Ado.BeginTranAsync();
        await oldWorker.Context.Db.Insertable(new GuardProbe { Id = 301, Value = "must-rollback-on-kill" })
            .ExecuteCommandAsync();
        await fixture.KillAsync(guard.ServerProcessIdForTest);
        var commitFailure = await Record.ExceptionAsync(() => oldWorker.Context.Db.Ado.CommitTranAsync());
        Assert.NotNull(commitFailure);
        var failedWrite = await Record.ExceptionAsync(() => oldWorker.LeaseService.CompleteSessionGuardedAsync(
            TaskType, date.ToString("yyyy-MM-dd"), lease.Lease!.LeaseToken!, false, "commit failed", guard));
        var ordinaryWrite = await Record.ExceptionAsync(() => oldWorker.Context.Db.Insertable(
            new GuardProbe { Id = 302, Value = "must-not-reconnect-after-commit" }).ExecuteCommandAsync());
        Assert.IsType<SalesStatisticsDateExecutionGuardLostException>(failedWrite);
        Assert.IsType<SalesStatisticsDateExecutionGuardLostException>(ordinaryWrite);
        Assert.Empty(await fixture.ReadProbeRowsAsync());
        Assert.Equal(ScheduledTaskLeaseStatus.Running, (await fixture.ReadLeaseAsync(date)).Status);
        Assert.False(guard.IsActive);

        var disposeFailure = await Record.ExceptionAsync(() =>
        {
            return DisposeLostContextAsync(guard, oldWorker.Context.Db);
        });
        Assert.Null(disposeFailure);
        var nextWorker = fixture.OpenContext("guard-after-transaction-dispose");
        await using var nextGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            nextWorker.Context, date.AddDays(1), NullLogger.Instance);
        Assert.True(nextGuard.Acquired);
    }

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task Rollback首次故障不能使旧上下文重连或提交事务行()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 9, 20);
        var oldWorker = fixture.OpenContext("guard-rollback-fault");
        await using var guard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            oldWorker.Context, date, NullLogger.Instance);
        var lease = await oldWorker.LeaseService.TryAcquireSessionGuardedAsync(
            TaskType, date.ToString("yyyy-MM-dd"), guard);
        await oldWorker.Context.Db.Ado.BeginTranAsync();
        await oldWorker.Context.Db.Insertable(new GuardProbe { Id = 303, Value = "must-rollback-on-kill" })
            .ExecuteCommandAsync();
        await fixture.KillAsync(guard.ServerProcessIdForTest);
        var rollbackFailure = await Record.ExceptionAsync(() => oldWorker.Context.Db.Ado.RollbackTranAsync());
        Assert.NotNull(rollbackFailure);
        var failedWrite = await Record.ExceptionAsync(() => oldWorker.LeaseService.CompleteSessionGuardedAsync(
            TaskType, date.ToString("yyyy-MM-dd"), lease.Lease!.LeaseToken!, false, "rollback failed", guard));
        var ordinaryWrite = await Record.ExceptionAsync(() => oldWorker.Context.Db.Insertable(
            new GuardProbe { Id = 304, Value = "must-not-reconnect-after-rollback" }).ExecuteCommandAsync());
        Assert.IsType<SalesStatisticsDateExecutionGuardLostException>(failedWrite);
        Assert.IsType<SalesStatisticsDateExecutionGuardLostException>(ordinaryWrite);
        Assert.Empty(await fixture.ReadProbeRowsAsync());
        Assert.Equal(ScheduledTaskLeaseStatus.Running, (await fixture.ReadLeaseAsync(date)).Status);
        Assert.False(guard.IsActive);
    }

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 有效旧租约不提前接管而过期或空TTL旧租约可在会话锁下接管()
    {
        await using var fixture = await Fixture.CreateAsync();
        var validDate = new DateTime(2026, 9, 18);
        await fixture.SeedLeaseAsync(validDate, "legacy-valid", DateTime.UtcNow.AddHours(2));
        var validContext = fixture.OpenContext("guard-legacy-valid");
        await using (var validGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            validContext.Context, validDate, NullLogger.Instance))
        {
            var valid = await validContext.LeaseService.TryAcquireSessionGuardedAsync(
                TaskType, validDate.ToString("yyyy-MM-dd"), validGuard);
            Assert.False(valid.Acquired);
            Assert.Equal("legacy-valid", valid.Lease!.LeaseToken);
        }

        var expiredDate = validDate.AddDays(1);
        await fixture.SeedLeaseAsync(expiredDate, "legacy-expired", DateTime.UtcNow.AddMinutes(-1));
        var expiredContext = fixture.OpenContext("guard-legacy-expired");
        await using (var expiredGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            expiredContext.Context, expiredDate, NullLogger.Instance))
        {
            var expired = await expiredContext.LeaseService.TryAcquireSessionGuardedAsync(
                TaskType, expiredDate.ToString("yyyy-MM-dd"), expiredGuard);
            Assert.True(expired.Acquired);
            Assert.StartsWith(SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix, expired.Lease!.LeaseToken);
        }

        var nullTtlDate = expiredDate.AddDays(1);
        await fixture.SeedLeaseAsync(nullTtlDate, "legacy-null", null);
        var nullTtlContext = fixture.OpenContext("guard-legacy-null");
        await using var nullTtlGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            nullTtlContext.Context, nullTtlDate, NullLogger.Instance);
        var nullTtl = await nullTtlContext.LeaseService.TryAcquireSessionGuardedAsync(
            TaskType, nullTtlDate.ToString("yyyy-MM-dd"), nullTtlGuard);
        Assert.True(nullTtl.Acquired);
        Assert.StartsWith(SalesStatisticsDateExecutionGuard.SessionLeaseTokenPrefix, nullTtl.Lease!.LeaseToken);
    }

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 普通TTL路径和旧二进制CAS都不能夺取sqlsess1哨兵租约()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 9, 21);
        var guardedContext = fixture.OpenContext("guard-sentinel-owner");
        var guard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            guardedContext.Context, date, NullLogger.Instance);
        Assert.True(guard.Acquired);
        var sessionLease = await guardedContext.LeaseService.TryAcquireSessionGuardedAsync(
            TaskType, date.ToString("yyyy-MM-dd"), guard);
        var sentinel = Assert.IsType<string>(sessionLease.Lease!.LeaseToken);
        await guard.DisposeAsync();

        var ordinaryContext = fixture.OpenContext("legacy-ttl-client");
        var ordinary = await ordinaryContext.LeaseService.TryAcquireAsync(
            TaskType, date.ToString("yyyy-MM-dd"), TimeSpan.FromHours(2));
        Assert.False(ordinary.Acquired);
        Assert.Equal(sentinel, ordinary.Lease!.LeaseToken);

        Assert.Equal(0, await fixture.TryLegacyTtlTakeoverAsync(date, "legacy-binary-token"));
        var persisted = await fixture.ReadLeaseAsync(date);
        Assert.Equal(sentinel, persisted.LeaseToken);
        Assert.Equal(ScheduledTaskLeaseStatus.Running, persisted.Status);
        Assert.Equal(new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc), persisted.LeaseUntilUtc);
    }

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 失效guard释放后同一context仍拒绝写入而终态释放后新日期可执行()
    {
        await using var fixture = await Fixture.CreateAsync();
        var lostDate = new DateTime(2026, 9, 22);
        var lostWorker = fixture.OpenContext("guard-disposed-invalid");
        var lostGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            lostWorker.Context, lostDate, NullLogger.Instance);
        await fixture.KillAsync(lostGuard.ServerProcessIdForTest);
        await Assert.ThrowsAsync<SalesStatisticsDateExecutionGuardLostException>(() =>
            lostWorker.Context.Db.Insertable(new GuardProbe { Id = 401, Value = "lost" }).ExecuteCommandAsync());
        await lostGuard.DisposeAsync();
        await Assert.ThrowsAsync<SalesStatisticsDateExecutionGuardLostException>(() =>
            lostWorker.Context.Db.Insertable(new GuardProbe { Id = 402, Value = "after-dispose" }).ExecuteCommandAsync());
        Assert.Empty(await fixture.ReadProbeRowsAsync());

        var successDate = lostDate.AddDays(1);
        var successWorker = fixture.OpenContext("guard-success");
        await using (var successGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            successWorker.Context, successDate, NullLogger.Instance))
        {
            var successLease = await successWorker.LeaseService.TryAcquireSessionGuardedAsync(
                TaskType, successDate.ToString("yyyy-MM-dd"), successGuard);
            Assert.True(await successWorker.LeaseService.CompleteSessionGuardedAsync(
                TaskType, successDate.ToString("yyyy-MM-dd"), successLease.Lease!.LeaseToken!, true,
                null, successGuard));
        }

        var nextDate = successDate.AddDays(1);
        var nextWorker = fixture.OpenContext("guard-after-terminal");
        await using var nextGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            nextWorker.Context, nextDate, NullLogger.Instance);
        var nextLease = await nextWorker.LeaseService.TryAcquireSessionGuardedAsync(
            TaskType, nextDate.ToString("yyyy-MM-dd"), nextGuard);
        Assert.True(await nextWorker.LeaseService.CompleteSessionGuardedAsync(
            TaskType, nextDate.ToString("yyyy-MM-dd"), nextLease.Lease!.LeaseToken!, false, "cancelled", nextGuard));
        Assert.Equal(ScheduledTaskLeaseStatus.Failed, (await fixture.ReadLeaseAsync(nextDate)).Status);
    }

    [SalesStatisticsDateExecutionGuardSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task sqlsess1读侧只报告仍持有Session锁的运行租约且继任后恢复可见()
    {
        await using var fixture = await Fixture.CreateAsync();
        var date = new DateTime(2026, 9, 23);
        var owner = fixture.OpenContext("guard-read-owner");
        var observer = fixture.OpenContext("guard-read-observer");
        var ownerGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            owner.Context, date, NullLogger.Instance);
        Assert.True(ownerGuard.Acquired);
        var ownerLease = await owner.LeaseService.TryAcquireSessionGuardedAsync(
            TaskType, date.ToString("yyyy-MM-dd"), ownerGuard);
        Assert.True(ownerLease.Acquired);

        // 独立观察连接和持锁连接都必须把仍持有 Session applock 的 sqlsess1 行报告为运行中。
        Assert.Equal(1, await observer.LeaseService.GetRunningLeaseCountAsync());
        var observedActive = await observer.LeaseService.GetRunningLeasesAsync(TaskType, date, date);
        Assert.Single(observedActive);
        Assert.Equal(ownerLease.Lease!.LeaseToken, observedActive[0].LeaseToken);
        Assert.Equal(1, await owner.LeaseService.GetRunningLeaseCountAsync());
        Assert.Single(await owner.LeaseService.GetRunningLeasesAsync(TaskType, date, date));

        await ownerGuard.DisposeAsync();
        // SQL Session 已释放，但保留的 sqlsess1 Running 实体是接管凭据，不能被读侧永远误报为活跃。
        Assert.Equal(0, await observer.LeaseService.GetRunningLeaseCountAsync());
        Assert.Empty(await observer.LeaseService.GetRunningLeasesAsync(TaskType, date, date));
        var ghost = await fixture.ReadLeaseAsync(date);
        Assert.Equal(ScheduledTaskLeaseStatus.Running, ghost.Status);
        Assert.Equal(ownerLease.Lease!.LeaseToken, ghost.LeaseToken);

        var successor = fixture.OpenContext("guard-read-successor");
        await using var successorGuard = await SalesStatisticsDateExecutionGuard.TryAcquireAsync(
            successor.Context, date, NullLogger.Instance);
        Assert.True(successorGuard.Acquired);
        var successorLease = await successor.LeaseService.TryAcquireSessionGuardedAsync(
            TaskType, date.ToString("yyyy-MM-dd"), successorGuard);
        Assert.True(successorLease.Acquired);
        Assert.NotEqual(ownerLease.Lease!.LeaseToken, successorLease.Lease!.LeaseToken);

        Assert.Equal(1, await observer.LeaseService.GetRunningLeaseCountAsync());
        var observedSuccessor = await observer.LeaseService.GetRunningLeasesAsync(TaskType, date, date);
        Assert.Single(observedSuccessor);
        Assert.Equal(successorLease.Lease!.LeaseToken, observedSuccessor[0].LeaseToken);
        Assert.Equal(1, await successor.LeaseService.GetRunningLeaseCountAsync());
        Assert.Single(await successor.LeaseService.GetRunningLeasesAsync(TaskType, date, date));
    }

    [SugarTable("GuardProbe")]
    private sealed class GuardProbe
    {
        [SugarColumn(IsPrimaryKey = true)]
        public int Id { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    [SugarTable("GuardBulkProbe")]
    private sealed class GuardBulkProbe
    {
        [SugarColumn(IsPrimaryKey = true)]
        public int Id { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    private sealed class ContextHandle
    {
        internal ContextHandle(SqlSugarContext context, ScheduledTaskLeaseService leaseService)
        {
            Context = context;
            LeaseService = leaseService;
        }

        internal SqlSugarContext Context { get; }
        internal ScheduledTaskLeaseService LeaseService { get; }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string ConnectionEnvironmentVariable =
            "SALES_STATISTICS_RECOVERY_SQLSERVER_TEST_CONNECTION";
        private readonly string _adminConnection;
        private readonly string _databaseName = "HbStatisticsGuard_" + Guid.NewGuid().ToString("N");
        private string _databaseConnection = string.Empty;

        private Fixture(string adminConnection) => _adminConnection = adminConnection;

        internal static async Task<Fixture> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
            Assert.False(string.IsNullOrWhiteSpace(configured));
            var configuredBuilder = new SqlConnectionStringBuilder(configured);
            Assert.Contains(
                configuredBuilder.DataSource,
                new[] { "127.0.0.1,15438", "localhost,15438" },
                StringComparer.OrdinalIgnoreCase
            );

            var fixture = new Fixture(configured!);
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = "master",
            }.ConnectionString);
            await master.OpenAsync();
            await using (var command = master.CreateCommand())
            {
                command.CommandText = $"CREATE DATABASE [{fixture._databaseName}]";
                await command.ExecuteNonQueryAsync();
            }

            fixture._databaseConnection = new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = fixture._databaseName,
                ConnectRetryCount = 0,
            }.ConnectionString;
            try
            {
                using var setup = fixture.OpenDatabase();
                setup.CodeFirst.InitTables(typeof(ScheduledTaskLease), typeof(GuardProbe), typeof(GuardBulkProbe));
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal ContextHandle OpenContext(string instanceId)
        {
            var context = CreateContext(OpenDatabase());
            var leaseService = new ScheduledTaskLeaseService(
                context,
                Options.Create(new ScheduledTaskOptions { InstanceId = instanceId }),
                NullLogger<ScheduledTaskLeaseService>.Instance
            );
            return new ContextHandle(context, leaseService);
        }

        internal async Task KillAsync(int serverProcessId)
        {
            await using var admin = new SqlConnection(new SqlConnectionStringBuilder(_adminConnection)
            {
                InitialCatalog = "master",
            }.ConnectionString);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"KILL {serverProcessId}";
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<List<GuardProbe>> ReadProbeRowsAsync()
        {
            using var observer = OpenDatabase();
            return await observer.Queryable<GuardProbe>().OrderBy(row => row.Id).ToListAsync();
        }

        internal async Task<List<GuardBulkProbe>> ReadBulkProbeRowsAsync()
        {
            using var observer = OpenDatabase();
            return await observer.Queryable<GuardBulkProbe>().OrderBy(row => row.Id).ToListAsync();
        }

        internal async Task<bool> HasExclusiveApplicationLockAsync(int serverProcessId)
        {
            using var observer = OpenDatabase();
            var count = await observer.Ado.SqlQuerySingleAsync<int>(
                "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_session_id = @SessionId AND request_mode IN (N'Exclusive', N'X') AND request_status = N'GRANT';",
                new SugarParameter("@SessionId", serverProcessId)
            );
            return count > 0;
        }

        internal async Task<bool> HasBulkProbeWriteLockAsync(int serverProcessId)
        {
            using var observer = OpenDatabase();
            var count = await observer.Ado.SqlQuerySingleAsync<int>(
                "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_database_id = DB_ID() AND request_session_id = @SessionId AND resource_type = N'OBJECT' AND resource_associated_entity_id = OBJECT_ID(N'dbo.GuardBulkProbe') AND request_mode IN (N'IX', N'X');",
                new SugarParameter("@SessionId", serverProcessId)
            );
            return count > 0;
        }

        internal async Task<int> ReadBulkProbeRowCountNoLockAsync()
        {
            using var observer = OpenDatabase();
            return await observer.Ado.SqlQuerySingleAsync<int>(
                "SELECT COUNT(*) FROM dbo.GuardBulkProbe WITH (NOLOCK);"
            );
        }

        internal async Task<ScheduledTaskLease> ReadLeaseAsync(DateTime date)
        {
            using var observer = OpenDatabase();
            return await observer.Queryable<ScheduledTaskLease>()
                .SingleAsync(row => row.TaskType == TaskType
                    && row.ScopeKey == date.ToString("yyyy-MM-dd"));
        }

        internal async Task SeedLeaseAsync(DateTime date, string leaseToken, DateTime? leaseUntilUtc)
        {
            using var db = OpenDatabase();
            await db.Insertable(new ScheduledTaskLease
            {
                TaskType = TaskType,
                ScopeKey = date.ToString("yyyy-MM-dd"),
                Status = ScheduledTaskLeaseStatus.Running,
                OwnerInstanceId = "legacy-binary",
                LeaseToken = leaseToken,
                LeaseUntilUtc = leaseUntilUtc,
                StartedAtUtc = DateTime.UtcNow.AddHours(-3),
                UpdatedAtUtc = DateTime.UtcNow.AddHours(-3),
            }).ExecuteCommandAsync();
        }

        internal async Task<int> TryLegacyTtlTakeoverAsync(DateTime date, string replacementToken)
        {
            using var db = OpenDatabase();
            return await db.Ado.ExecuteCommandAsync(
                "UPDATE dbo.ScheduledTaskLease SET LeaseToken = @ReplacementToken, LeaseUntilUtc = DATEADD(hour, 2, SYSUTCDATETIME()) WHERE TaskType = @TaskType AND ScopeKey = @ScopeKey AND Status = N'Running' AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc <= SYSUTCDATETIME());",
                new SugarParameter("@ReplacementToken", replacementToken),
                new SugarParameter("@TaskType", TaskType),
                new SugarParameter("@ScopeKey", date.ToString("yyyy-MM-dd"))
            );
        }

        private SqlSugarClient OpenDatabase() => new(new ConnectionConfig
        {
            ConnectionString = _databaseConnection,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

        public async ValueTask DisposeAsync()
        {
            if (string.IsNullOrWhiteSpace(_databaseConnection))
                return;
            await using var admin = new SqlConnection(new SqlConnectionStringBuilder(_adminConnection)
            {
                InitialCatalog = "master",
            }.ConnectionString);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        private static SqlSugarContext CreateContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
            typeof(SqlSugarContext)
                .GetField("_db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(context, db);
            return context;
        }
    }

    private static async Task DisposeLostContextAsync(
        SalesStatisticsDateExecutionGuard guard,
        ISqlSugarClient db)
    {
        await guard.DisposeAsync();
        db.Dispose();
    }
}
