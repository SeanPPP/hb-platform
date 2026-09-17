using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed partial class BatchProductSalesAnalysisSqlServerIntegrationTests
{
    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer争抢仅一人领取_跨查询复用且限制分店()
    {
        await InstallDailySchemaAsync();
        var day = new DateTime(2026, 1, 2);
        var now = new DateTime(2026, 9, 14, 13, 0, 0, DateTimeKind.Utc);
        var store = new BatchProductSalesDiscountDailyStore(_catalog!);
        using var secondDb = Client(WithDatabase(_master!, CatalogName));
        var second = new BatchProductSalesDiscountDailyStore(secondDb);
        await Task.WhenAll(store.EnsureQueuedAsync([day], now, default), second.EnsureQueuedAsync([day], now, default));
        Assert.Equal(1, await _catalog!.Queryable<BatchProductSalesDiscountRefreshState>().CountAsync());
        var claims = await Task.WhenAll(store.ClaimNextAsync(now, [day], default), second.ClaimNextAsync(now, [day], default));
        var claim = Assert.Single(claims.Where(x => x != null))!;
        var rows = new List<BatchProductSalesAggregateRow>
        {
            DailyRow(day, "S1", 2, 30m), DailyRow(day, "S2", 1, 15m),
        };
        await store.PublishAsync(claim, "stats-1", "source-1", new Dictionary<string, List<BatchProductSalesAggregateRow>> { ["P1"] = rows }, now, default);
        var snapshot = Assert.Single(await _catalog.Queryable<BatchProductSalesDiscountSnapshot>().ToListAsync());
        Assert.Equal(2, snapshot.SnapshotFormat);
        var first = await new BatchProductSalesDiscountSnapshotReader(_catalog).ReadAsync("P1", day, day, ["S1"], [rows[0]], default);
        var both = await new BatchProductSalesDiscountSnapshotReader(secondDb).ReadAsync("P1", day, day, ["S1", "S2"], rows, default);
        Assert.Equal("Fresh", first.Status);
        Assert.Equal("S1", Assert.Single(first.Rows).BranchCode);
        Assert.Equal(2, both.Rows.Count);
        Assert.Equal(3m, both.Rows.Sum(x => x.DiscountQuantity));
        Assert.Equal(1, await _catalog.Queryable<BatchProductSalesDiscountSnapshot>().CountAsync());
        Assert.Equal(0, (await store.GetAsync(day))!.Attempts);
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer过期接管后拒绝旧执行者提交()
    {
        await InstallDailySchemaAsync();
        var day = new DateTime(2026, 1, 3);
        var now = new DateTime(2026, 9, 14, 13, 0, 0, DateTimeKind.Utc);
        var store = new BatchProductSalesDiscountDailyStore(_catalog!);
        await store.EnsureQueuedAsync([day], now, default);
        var oldClaim = (await store.ClaimNextAsync(now, [day], default))!;
        await _catalog!.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(x => x.LeaseUntilUtc == now.AddSeconds(-1)).Where(x => x.Date == day).ExecuteCommandAsync();
        using var secondDb = Client(WithDatabase(_master!, CatalogName));
        var second = new BatchProductSalesDiscountDailyStore(secondDb);
        var replacement = (await second.ClaimNextAsync(now, [day], default))!;
        Assert.NotEqual(oldClaim.LeaseToken, replacement.LeaseToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PublishAsync(oldClaim, "old-stats", "old-source", new Dictionary<string, List<BatchProductSalesAggregateRow>>(), now, default));
        await second.PublishAsync(replacement, "new-stats", "new-source", new Dictionary<string, List<BatchProductSalesAggregateRow>>(), now, default);
        var published = (await store.GetAsync(day))!;
        Assert.Equal("Fresh", published.Status);
        Assert.Equal("new-source", published.SourceVersion);
        Assert.Equal(0, published.SnapshotCount);
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer替换发布失败必须回滚全部商品并保留旧结果()
    {
        await InstallDailySchemaAsync();
        var day = new DateTime(2026, 1, 4);
        var now = new DateTime(2026, 9, 14, 13, 0, 0, DateTimeKind.Utc);
        var store = new BatchProductSalesDiscountDailyStore(_catalog!);
        await store.EnsureQueuedAsync([day], now, default);
        var first = await store.ClaimNextAsync(now, [day], default);
        Assert.NotNull(first);
        await store.PublishAsync(first, "stats-old", "source-old", new Dictionary<string, List<BatchProductSalesAggregateRow>> { ["P1"] = [DailyRow(day, "S1", 1, 15)] }, now, default);
        await _catalog!.Ado.ExecuteCommandAsync("""
            CREATE TRIGGER dbo.RejectInvalidDiscountSnapshot ON dbo.BatchProductSalesDiscountSnapshot AFTER INSERT AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE ProductCode = N'PFAIL')
                    THROW 51021, N'模拟发布中途失败', 1;
            END
            """);
        var retry = (await store.ClaimNextAsync(now.AddMinutes(6), [day], default))!;
        await Assert.ThrowsAnyAsync<Exception>(() => store.PublishAsync(retry, "stats-new", "source-new", new Dictionary<string, List<BatchProductSalesAggregateRow>>
        {
            ["P1"] = [DailyRow(day, "S1", 2, 30)],
            ["PFAIL"] = [new() { Date = day, ProductCode = "PFAIL", BranchCode = "S1", Quantity = 1, UnknownQuantity = 1, SalesAmount = 1 }],
        }, now.AddMinutes(6), default));
        var snapshot = Assert.Single(await _catalog.Queryable<BatchProductSalesDiscountSnapshot>().ToListAsync());
        Assert.Equal("source-old", snapshot.SourceVersion);
        Assert.Equal(1m, Assert.Single(JsonSerializer.Deserialize<List<BatchProductSalesAggregateRow>>(snapshot.PayloadJson!)!).Quantity);
        Assert.Equal("Running", (await store.GetAsync(day))!.Status);
        Assert.Equal("source-old", (await store.GetAsync(day))!.SourceVersion);
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer第二个插入批次失败必须回滚所有新快照并保留旧结果()
    {
        await InstallDailySchemaAsync();
        var day = new DateTime(2026, 1, 6);
        var now = new DateTime(2026, 9, 15, 3, 0, 0, DateTimeKind.Utc);
        var store = new BatchProductSalesDiscountDailyStore(_catalog!);
        await store.EnsureQueuedAsync([day], now, default);
        var initialClaim = (await store.ClaimNextAsync(now, [day], default))!;
        await store.PublishAsync(initialClaim, "stats-old", "source-old",
            new Dictionary<string, List<BatchProductSalesAggregateRow>> { ["OLD"] = [DailyRow(day, "S1", 1, 15)] }, now, default);
        await _catalog!.Ado.ExecuteCommandAsync("""
            CREATE TRIGGER dbo.RejectSecondDiscountSnapshotBatch ON dbo.BatchProductSalesDiscountSnapshot AFTER INSERT AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE ProductCode = N'P0100')
                    THROW 51022, N'模拟第二个快照插入批次失败', 1;
            END
            """);
        var claim = (await store.ClaimNextAsync(now.AddMinutes(6), [day], default))!;
        var payload = Enumerable.Range(0, BatchProductSalesDiscountDailyStore.SnapshotInsertBatchSize + 1)
            .ToDictionary(index => $"P{index:D4}", index => new List<BatchProductSalesAggregateRow>
            {
                DailyRow(day, "S1", index + 1, index + 1),
            });

        await Assert.ThrowsAnyAsync<Exception>(() => store.PublishAsync(claim, "stats-new", "source-new", payload,
            now.AddMinutes(6), default));

        var remaining = Assert.Single(await _catalog.Queryable<BatchProductSalesDiscountSnapshot>().ToListAsync());
        Assert.Equal("OLD", remaining.ProductCode);
        Assert.Equal("source-old", remaining.SourceVersion);
        var state = (await store.GetAsync(day))!;
        Assert.Equal("Running", state.Status);
        Assert.Equal("source-old", state.SourceVersion);
        Assert.Equal(1, state.SnapshotCount);
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer多个插入批次成功后快照数和状态计数一致()
    {
        await InstallDailySchemaAsync();
        var day = new DateTime(2026, 1, 7);
        var now = new DateTime(2026, 9, 15, 3, 0, 0, DateTimeKind.Utc);
        var store = new BatchProductSalesDiscountDailyStore(_catalog!);
        await store.EnsureQueuedAsync([day], now, default);
        var claim = (await store.ClaimNextAsync(now, [day], default))!;
        var snapshotCount = BatchProductSalesDiscountDailyStore.SnapshotInsertBatchSize * 2 + 1;
        var payload = Enumerable.Range(0, snapshotCount).ToDictionary(index => $"P{index:D4}", index =>
            new List<BatchProductSalesAggregateRow> { DailyRow(day, "S1", index + 1, index + 1) });

        await store.PublishAsync(claim, "stats-new", "source-new", payload, now, default);

        var snapshots = await _catalog!.Queryable<BatchProductSalesDiscountSnapshot>()
            .Where(snapshot => snapshot.StartDate == day && snapshot.EndDate == day).ToListAsync();
        Assert.Equal(snapshotCount, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.Equal("source-new", snapshot.SourceVersion));
        var state = (await store.GetAsync(day))!;
        Assert.Equal("Fresh", state.Status);
        Assert.Equal(snapshotCount, state.SnapshotCount);
        Assert.Null(state.LeaseToken);
        Assert.Null(state.LeaseUntilUtc);
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer第二批执行前取消令牌必须回滚并保留旧结果()
    {
        await InstallDailySchemaAsync();
        var day = new DateTime(2026, 1, 7);
        var now = new DateTime(2026, 9, 15, 3, 0, 0, DateTimeKind.Utc);
        var store = new BatchProductSalesDiscountDailyStore(_catalog!);
        await store.EnsureQueuedAsync([day], now, default);
        var oldClaim = (await store.ClaimNextAsync(now, [day], default))!;
        await store.PublishAsync(oldClaim, "stats-old", "source-old",
            new Dictionary<string, List<BatchProductSalesAggregateRow>> { ["OLD"] = [DailyRow(day, "S1", 1, 15)] }, now, default);
        var claim = (await store.ClaimNextAsync(now.AddMinutes(6), [day], default))!;
        var payload = Enumerable.Range(0, BatchProductSalesDiscountDailyStore.SnapshotInsertBatchSize + 1)
            .ToDictionary(index => $"P{index:D4}", index => new List<BatchProductSalesAggregateRow>
            {
                DailyRow(day, "S1", index + 1, index + 1),
            });
        using var cancelled = new CancellationTokenSource();
        var snapshotInsertCommands = 0;
        _catalog.Aop.OnLogExecuting = (sql, _) =>
        {
            if (sql.Contains("BatchProductSalesDiscountSnapshot", StringComparison.OrdinalIgnoreCase)
                && sql.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                && ++snapshotInsertCommands == 2)
                cancelled.Cancel();
        };
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PublishAsync(claim, "stats-new", "source-new", payload,
                now.AddMinutes(6), cancelled.Token));
        }
        finally
        {
            _catalog.Aop.OnLogExecuting = null;
            _catalog.Ado.RemoveCancellationToken();
        }

        Assert.Equal(2, snapshotInsertCommands);
        var remaining = Assert.Single(await _catalog.Queryable<BatchProductSalesDiscountSnapshot>().ToListAsync());
        Assert.Equal("OLD", remaining.ProductCode);
        Assert.Equal("source-old", remaining.SourceVersion);
        var state = (await store.GetAsync(day))!;
        Assert.Equal("Running", state.Status);
        Assert.Equal("source-old", state.SourceVersion);
        Assert.Equal(1, state.SnapshotCount);
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer已取消的Ado令牌不能阻止None令牌记录失败并归还当天租约()
    {
        await InstallDailySchemaAsync();
        var day = new DateTime(2026, 1, 8);
        var now = new DateTime(2026, 9, 15, 3, 0, 0, DateTimeKind.Utc);
        var store = new BatchProductSalesDiscountDailyStore(_catalog!);
        await store.EnsureQueuedAsync([day], now, default);
        var claim = (await store.ClaimNextAsync(now, [day], default))!;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        _catalog!.Ado.CancellationToken = cancelled.Token;
        try
        {
            // 旧实现直接在复用 client 上执行终态 UPDATE；已取消的 ADO token 会使该写入失败。
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _catalog.Updateable<BatchProductSalesDiscountRefreshState>()
                .SetColumns(state => state.LastError == "旧实现不应写入")
                .Where(state => state.Date == day).ExecuteCommandAsync());

            await store.FinishFailureAsync(claim, "模拟预算取消", now, false, CancellationToken.None);
            // 收尾写入临时替换为 None，但调用者自己的已取消令牌仍须原样恢复。
            Assert.Equal(cancelled.Token, _catalog.Ado.CancellationToken);
        }
        finally
        {
            _catalog.Ado.RemoveCancellationToken();
        }

        var failed = (await store.GetAsync(day))!;
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("模拟预算取消", failed.LastError);
        Assert.Null(failed.LeaseToken);
        Assert.Null(failed.LeaseUntilUtc);
        Assert.Equal(now.AddMinutes(3), failed.NextAttemptAtUtc);
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer计算作用域Ado已取消时独立终态作用域仍完成全局租约()
    {
        _catalog!.CodeFirst.InitTables<ScheduledTaskLease>();
        const string taskType = "BatchProductSalesDiscountWorker";
        const string scopeKey = "daily-format-2";
        const string instanceId = "discount-cancellation-test";
        var options = Options.Create(new ScheduledTaskOptions { InstanceId = instanceId });
        var runningLeaseService = new ScheduledTaskLeaseService(CreateSqlSugarContext(_catalog), options,
            NullLogger<ScheduledTaskLeaseService>.Instance);
        var acquired = await runningLeaseService.TryAcquireAsync(taskType, scopeKey, TimeSpan.FromMinutes(15));
        Assert.True(acquired.Acquired);
        var leaseToken = Assert.IsType<string>(acquired.Lease?.LeaseToken);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        _catalog.Ado.CancellationToken = cancelled.Token;
        try
        {
            // 复现单日计算 scope 已带取消 ADO token；worker 收尾必须换用独立 db scope。
            using var terminalDb = Client(WithDatabase(_master!, CatalogName));
            var terminalLeaseService = new ScheduledTaskLeaseService(CreateSqlSugarContext(terminalDb), options,
                NullLogger<ScheduledTaskLeaseService>.Instance);

            Assert.True(await terminalLeaseService.CompleteAsync(taskType, scopeKey, leaseToken, success: false,
                errorMessage: "simulated day-budget cancellation"));
            var completed = await terminalDb.Queryable<ScheduledTaskLease>()
                .SingleAsync(lease => lease.TaskType == taskType && lease.ScopeKey == scopeKey);
            Assert.Equal(ScheduledTaskLeaseStatus.Failed, completed.Status);
            Assert.Null(completed.LeaseUntilUtc);
            Assert.Equal("simulated day-budget cancellation", completed.LastError);
        }
        finally
        {
            _catalog.Ado.RemoveCancellationToken();
        }
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer_Worker终态持久化使用独立Scope归还全局租约()
    {
        _catalog!.CodeFirst.InitTables<ScheduledTaskLease>();
        const string taskType = "BatchProductSalesDiscountWorker";
        const string scopeKey = "daily-format-2";
        const string instanceId = "discount-worker-terminal-scope-test";
        var options = Options.Create(new ScheduledTaskOptions { InstanceId = instanceId });
        var runningLeases = new ScheduledTaskLeaseService(CreateSqlSugarContext(_catalog), options,
            NullLogger<ScheduledTaskLeaseService>.Instance);
        var acquired = await runningLeases.TryAcquireAsync(taskType, scopeKey, TimeSpan.FromMinutes(15));
        var leaseToken = Assert.IsType<string>(acquired.Lease?.LeaseToken);
        using var cancelledComputation = new CancellationTokenSource();
        cancelledComputation.Cancel();
        _catalog.Ado.CancellationToken = cancelledComputation.Token;

        using var terminalDb = Client(WithDatabase(_master!, CatalogName));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<SqlSugarContext>(_ => CreateSqlSugarContext(terminalDb));
        services.AddScoped<ScheduledTaskLeaseService>();
        services.AddSingleton<IOptions<ScheduledTaskOptions>>(options);
        using var provider = services.BuildServiceProvider();
        var worker = new BatchProductSalesDiscountWorker(provider.GetRequiredService<IServiceScopeFactory>(), options,
            NullLogger<BatchProductSalesDiscountWorker>.Instance);
        var persisted = false;
        try
        {
            var persist = new Func<IServiceProvider, CancellationToken, Task>(async (terminalServices, token) =>
            {
                var scopedTerminalDb = terminalServices.GetRequiredService<SqlSugarContext>().Db;
                Assert.Same(terminalDb, scopedTerminalDb);
                Assert.NotSame(_catalog, scopedTerminalDb);
                Assert.False(token.IsCancellationRequested);
                persisted = await terminalServices.GetRequiredService<ScheduledTaskLeaseService>()
                    .CompleteAsync(taskType, scopeKey, leaseToken, success: false, errorMessage: "worker terminal cancellation");
            });
            var method = typeof(BatchProductSalesDiscountWorker).GetMethod("PersistTerminalAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(worker, [persist])!;
        }
        finally
        {
            _catalog.Ado.RemoveCancellationToken();
        }

        Assert.True(persisted);
        var completed = await _catalog.Queryable<ScheduledTaskLease>()
            .SingleAsync(lease => lease.TaskType == taskType && lease.ScopeKey == scopeKey);
        Assert.Equal(ScheduledTaskLeaseStatus.Failed, completed.Status);
        Assert.Null(completed.LeaseUntilUtc);
        Assert.Equal("worker terminal cancellation", completed.LastError);
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer失败后可重试且按参数保留既有重算标记()
    {
        await InstallDailySchemaAsync();
        var day = new DateTime(2026, 1, 5);
        var now = new DateTime(2026, 9, 15, 2, 0, 0, DateTimeKind.Utc);
        var store = new BatchProductSalesDiscountDailyStore(_catalog!);
        var cases = new[]
        {
            new { Existing = false, PreserveExisting = false, Requested = false, Expected = false },
            new { Existing = false, PreserveExisting = false, Requested = true, Expected = true },
            new { Existing = false, PreserveExisting = true, Requested = false, Expected = false },
            new { Existing = false, PreserveExisting = true, Requested = true, Expected = true },
            new { Existing = true, PreserveExisting = false, Requested = false, Expected = false },
            new { Existing = true, PreserveExisting = false, Requested = true, Expected = true },
            new { Existing = true, PreserveExisting = true, Requested = false, Expected = true },
            new { Existing = true, PreserveExisting = true, Requested = true, Expected = true },
        };

        for (var index = 0; index < cases.Length; index++)
        {
            var @case = cases[index];
            var caseDay = day.AddDays(index);
            await store.EnsureQueuedAsync([caseDay], now, default);
            await _catalog!.Updateable<BatchProductSalesDiscountRefreshState>()
                .SetColumns(x => x.ReconcileRequested == @case.Existing)
                .Where(x => x.Date == caseDay)
                .ExecuteCommandAsync();
            var claim = (await store.ClaimNextAsync(now, [caseDay], default))!;

            await store.FinishFailureAsync(claim, $"模拟失败-{index}", now, @case.Requested, default,
                @case.PreserveExisting);

            var failed = (await store.GetAsync(caseDay))!;
            Assert.Equal("Failed", failed.Status);
            Assert.Equal(@case.Expected, failed.ReconcileRequested);
            Assert.Null(failed.LeaseToken);
            Assert.Null(failed.LeaseUntilUtc);
            Assert.Equal(now.AddMinutes(3), failed.NextAttemptAtUtc);

            var retry = await store.ClaimNextAsync(now.AddMinutes(3), [caseDay], default);
            Assert.NotNull(retry);
            Assert.Equal("Running", (await store.GetAsync(caseDay))!.Status);
        }
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer一年范围外Queued保留但不领取也不触发续跑()
    {
        await InstallDailySchemaAsync();
        var now = new DateTime(2026, 9, 15, 2, 0, 0, DateTimeKind.Utc);
        var coverageStart = now.Date.AddYears(-1);
        var outsideCoverage = coverageStart.AddDays(-1);
        var store = new BatchProductSalesDiscountDailyStore(_catalog!);
        await store.EnsureQueuedAsync([outsideCoverage], now, default);

        var claim = await store.ClaimNextAsync(now, Array.Empty<DateTime>(), coverageStart, now.Date,
            TimeSpan.FromMinutes(5), default);

        Assert.Null(claim);
        Assert.False(await store.HasDueBackfillAsync(coverageStart, now.Date, now));
        var preserved = await store.GetAsync(outsideCoverage);
        Assert.NotNull(preserved);
        Assert.Equal("Queued", preserved.Status);
    }

    [BatchSalesSqlServerFact]
    public async Task DailySnapshot_SQLServer迁移可重复执行且旧快照完整保留()
    {
        var original = ReadDiscountMigration("BatchProductSalesDiscountSnapshot.sql");
        var migration = ReadDiscountMigration("BatchProductSalesDiscountScheduledSnapshots.sql");
        // 原始脚本必须拒绝非 HBweb；仅隔离 loopback 测试库替换目标名称，DDL 本身原样运行。
        await Assert.ThrowsAnyAsync<Exception>(() => _catalog!.Ado.ExecuteCommandAsync(migration));
        await _catalog!.Ado.ExecuteCommandAsync(original.Replace("N'HBweb'", $"N'{CatalogName}'", StringComparison.Ordinal));
        await _catalog.Ado.ExecuteCommandAsync("""
            INSERT dbo.BatchProductSalesDiscountSnapshot
              (Id, SourceVersion, ProductCode, StartDate, EndDate, StoreCodesJson, Status, Attempts, RequestedAtUtc, NextAttemptAtUtc, PayloadJson)
            VALUES ('legacy-1', 'legacy-source', 'P1', '2025-09-01', '2025-12-31', '[]', 'Fresh', 1, SYSUTCDATETIME(), SYSUTCDATETIME(), '[]')
            """);
        var isolatedMigration = migration.Replace("N'HBweb'", $"N'{CatalogName}'", StringComparison.Ordinal);
        await _catalog.Ado.ExecuteCommandAsync(isolatedMigration);
        await _catalog.Ado.ExecuteCommandAsync(isolatedMigration);
        var legacy = Assert.Single(await _catalog.Queryable<BatchProductSalesDiscountSnapshot>().ToListAsync());
        Assert.Equal(1, legacy.SnapshotFormat);
        Assert.Equal("legacy-source", legacy.SourceVersion);
        Assert.True(new BatchProductSalesDiscountDailyStore(_catalog).SchemaReady);
        Assert.Equal(1, await _catalog.Ado.GetIntAsync("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.BatchProductSalesDiscountSnapshot') AND name = 'IX_BatchSalesDiscount_DailyProduct'"));
    }

    private async Task InstallDailySchemaAsync()
    {
        await _catalog!.Ado.ExecuteCommandAsync(ReadDiscountMigration("BatchProductSalesDiscountSnapshot.sql").Replace("N'HBweb'", $"N'{CatalogName}'", StringComparison.Ordinal));
        await _catalog.Ado.ExecuteCommandAsync(ReadDiscountMigration("BatchProductSalesDiscountScheduledSnapshots.sql").Replace("N'HBweb'", $"N'{CatalogName}'", StringComparison.Ordinal));
    }

    private static string ReadDiscountMigration(string filename)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "SqlScripts", filename);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(filename);
    }

    private static BatchProductSalesAggregateRow DailyRow(DateTime day, string store, decimal quantity, decimal amount) =>
        new() { Date = day, ProductCode = "P1", BranchCode = store, Quantity = quantity, DiscountQuantity = quantity, SalesAmount = amount };

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }
}
