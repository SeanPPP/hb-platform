using System.Text.Json;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
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
}
