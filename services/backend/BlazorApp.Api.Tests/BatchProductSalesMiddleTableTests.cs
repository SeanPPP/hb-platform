using System.Text.Json;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class BatchProductSalesMiddleTableTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"batch-middle-{Guid.NewGuid():N}.db");
    private readonly SqlSugarClient _db;
    private readonly DateTime _day = new(2025, 9, 1);
    private readonly BatchProductSalesDiscountStore _store;

    public BatchProductSalesMiddleTableTests()
    {
        _db = new(new ConnectionConfig { DbType = DbType.Sqlite, ConnectionString = $"Data Source={_path}", IsAutoCloseConnection = true });
        _db.CodeFirst.InitTables<Product, Store, SalesStatisticRefreshState, ProductStoreDailySalesStatistic,
            BatchProductSalesDiscountRefreshState>();
        // SQLite 测试使用 TEXT 承载 SQL Server nvarchar(max)，业务字段保持一致。
        _db.Ado.ExecuteCommand("""
            CREATE TABLE BatchProductSalesDiscountSnapshot (
                Id TEXT PRIMARY KEY, SnapshotFormat INTEGER NOT NULL DEFAULT 1,
                SourceVersion TEXT NOT NULL, ProductCode TEXT NOT NULL,
                StartDate DATETIME NOT NULL, EndDate DATETIME NOT NULL, StoreCodesJson TEXT NOT NULL,
                Status TEXT NOT NULL, Attempts INTEGER NOT NULL, RequestedAtUtc DATETIME NOT NULL,
                NextAttemptAtUtc DATETIME NOT NULL, LeaseToken TEXT, LeaseUntilUtc DATETIME,
                CompletedAtUtc DATETIME, PayloadJson TEXT);
            """);
        _db.Insertable(new Product { ProductCode = "P1", ItemNumber = "001", ProductName = "测试", IsDeleted = false }).ExecuteCommand();
        _db.Insertable(new[] { new Store { StoreCode = "S1", StoreName = "一店", IsDeleted = false }, new Store { StoreCode = "S2", StoreName = "二店", IsDeleted = false } }).ExecuteCommand();
        AddStatisticState(_day);
        _db.Insertable(new[]
        {
            new ProductStoreDailySalesStatistic { Date = _day, ProductCode = "P1", BranchCode = "S1", SupplierCode = "A", TotalQuantity = 2, TotalAmount = 20 },
            new ProductStoreDailySalesStatistic { Date = _day, ProductCode = "P1", BranchCode = "S1", SupplierCode = "B", TotalQuantity = -1, TotalAmount = -10 },
            new ProductStoreDailySalesStatistic { Date = _day, ProductCode = "P1", BranchCode = "S2", SupplierCode = "A", TotalQuantity = 7, TotalAmount = 70 },
        }).ExecuteCommand();
        AddRefreshState(_day, "source-1");
        _store = new(_db);
    }

    [Fact]
    public async Task Detail_重查不同门店范围只读同一日快照且按授权范围过滤()
    {
        Publish(_day, "source-1", Facts());
        var service = CreateService();
        var request = new BatchProductSalesDetailRequestDto { ProductCode = "P1", StartDate = _day, EndDate = _day };

        var s1 = (await service.GetDetailAsync(request, ["S1"])).Data!;
        var snapshotCount = await _db.Queryable<BatchProductSalesDiscountSnapshot>().CountAsync();
        var stateCount = await _db.Queryable<BatchProductSalesDiscountRefreshState>().CountAsync();
        var all = (await service.GetDetailAsync(request, null)).Data!;
        var s2 = (await service.GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = _day, StoreCodes = ["S2"] }, null)).Data!;
        await service.GetDetailAsync(request, ["S1"]);

        Assert.Equal(1m, s1.Metrics.Quantity);
        Assert.Equal(1m, s1.Metrics.DiscountQuantity);
        Assert.Equal("S1", Assert.Single(s1.Branches).BranchCode);
        Assert.Equal(8m, all.Metrics.Quantity);
        Assert.Equal(2, all.Branches.Count);
        Assert.Equal(7m, s2.Metrics.Quantity);
        Assert.Equal(snapshotCount, await _db.Queryable<BatchProductSalesDiscountSnapshot>().CountAsync());
        Assert.Equal(stateCount, await _db.Queryable<BatchProductSalesDiscountRefreshState>().CountAsync());
    }

    [Fact]
    public async Task Detail_缺少非零商品快照不伪装为零且不创建任务行()
    {
        var result = (await CreateService().GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, ["S1"])).Data!;

        Assert.Equal(1m, result.Metrics.Quantity);
        Assert.Equal("unknown", result.Metrics.DiscountStatus);
        Assert.Equal("OutOfSync", result.DiscountStatisticStatus);
        Assert.Equal(0, await _db.Queryable<BatchProductSalesDiscountSnapshot>().CountAsync());
        Assert.Equal(1, await _db.Queryable<BatchProductSalesDiscountRefreshState>().CountAsync());
    }

    [Fact]
    public async Task Detail_缺快照且只有一店统计时仍为另一授权店保留未知日()
    {
        await _db.Deleteable<ProductStoreDailySalesStatistic>()
            .Where(row => row.ProductCode == "P1" && row.BranchCode == "S2")
            .ExecuteCommandAsync();

        var result = (await CreateService().GetDetailAsync(
            new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, null)).Data!;

        var s1 = Assert.Single(result.Branches, branch => branch.BranchCode == "S1");
        var s2 = Assert.Single(result.Branches, branch => branch.BranchCode == "S2");
        Assert.Equal(1m, s1.Metrics.Quantity);
        Assert.Equal("unknown", s1.Metrics.DiscountStatus);
        Assert.Equal(0m, s2.Metrics.Quantity);
        Assert.Equal("unknown", s2.Metrics.DiscountStatus);
        Assert.Equal("unknown", Assert.Single(s2.Daily).Metrics.DiscountStatus);
    }

    [Theory]
    [InlineData(0, "Fresh", "Fresh")]
    [InlineData(25, "Fresh", "Fresh")]
    [InlineData(25, "Running", "Refreshing")]
    public async Task Detail_日状态完整且商品零销量无快照仍显示为已完成零日(
        int otherProductSnapshotCount, string stateStatus, string expectedStatus)
    {
        var zeroDay = _day.AddDays(1);
        AddStatisticState(zeroDay);
        AddRefreshState(zeroDay, "source-2", snapshotCount: otherProductSnapshotCount);
        await _db.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(state => state.Status == stateStatus)
            .Where(state => state.Date == zeroDay)
            .ExecuteCommandAsync();
        Publish(_day, "source-1", Facts());

        var result = (await CreateService().GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = zeroDay }, ["S1"])).Data!;

        var zero = Assert.Single(result.Daily, day => day.Date == zeroDay);
        Assert.Equal(0m, zero.Metrics.Quantity);
        Assert.Equal("complete", zero.Metrics.DiscountStatus);
        Assert.Equal(expectedStatus, result.DiscountStatisticStatus);
    }

    [Theory]
    [InlineData("Running")]
    [InlineData("WaitingCanonical")]
    [InlineData("Failed")]
    public async Task Detail_维护状态仍展示已提交且核验通过的快照(string maintenanceStatus)
    {
        Publish(_day, "source-1", Facts());
        await _db.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(state => state.Status == maintenanceStatus)
            .Where(state => state.Date == _day)
            .ExecuteCommandAsync();

        var result = (await CreateService().GetDetailAsync(
            new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, ["S1"])).Data!;

        Assert.Equal("Refreshing", result.DiscountStatisticStatus);
        Assert.Equal(1m, result.Metrics.Quantity);
        Assert.Equal(1m, result.Metrics.DiscountQuantity);
        Assert.Equal("complete", result.Metrics.DiscountStatus);
        Assert.NotNull(result.DiscountUpdatedAt);
    }

    [Fact]
    public async Task Detail_Fresh与维护中已提交日都可用时保持Refreshing()
    {
        var refreshingDay = _day.AddDays(1);
        AddStatisticState(refreshingDay);
        AddRefreshState(refreshingDay, "source-2");
        _db.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = refreshingDay, ProductCode = "P1", BranchCode = "S1", SupplierCode = "A", TotalQuantity = 3, TotalAmount = 30,
        }).ExecuteCommand();
        Publish(_day, "source-1", Facts());
        Publish(refreshingDay, "source-2", [new()
        {
            Date = refreshingDay, ProductCode = "P1", BranchCode = "S1", Quantity = 3, DiscountQuantity = 3, SalesAmount = 30,
        }]);
        await _db.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(state => state.Status == "Running")
            .Where(state => state.Date == refreshingDay)
            .ExecuteCommandAsync();

        var result = (await CreateService().GetDetailAsync(
            new() { ProductCode = "P1", StartDate = _day, EndDate = refreshingDay }, ["S1"])).Data!;

        Assert.Equal("Refreshing", result.DiscountStatisticStatus);
        Assert.Equal(4m, result.Metrics.Quantity);
        Assert.All(result.Daily, day => Assert.Equal("complete", day.Metrics.DiscountStatus));
    }

    [Fact]
    public async Task Detail_首次回填没有已提交代际时零日仍标记未知()
    {
        await _db.Deleteable<ProductStoreDailySalesStatistic>().Where(row => row.ProductCode == "P1").ExecuteCommandAsync();
        await _db.Updateable<BatchProductSalesDiscountRefreshState>()
            .SetColumns(state => state.Status == "Running")
            .SetColumns(state => state.SourceVersion == "")
            .SetColumns(state => state.StatisticsVersion == "")
            .SetColumns(state => state.CompletedAtUtc == null)
            .SetColumns(state => state.SnapshotCount == 0)
            .Where(state => state.Date == _day)
            .ExecuteCommandAsync();

        var result = (await CreateService().GetDetailAsync(
            new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, null)).Data!;

        Assert.Equal("Backfilling", result.DiscountStatisticStatus);
        Assert.Equal(2, result.Branches.Count);
        Assert.All(result.Branches, branch => Assert.Equal("unknown", branch.Metrics.DiscountStatus));
        Assert.All(result.Branches.SelectMany(branch => branch.Daily), day => Assert.Equal("unknown", day.Metrics.DiscountStatus));
    }

    [Fact]
    public async Task Detail_部分日期完成时保留完成日并将坏日明确为未知()
    {
        var nextDay = _day.AddDays(1);
        AddStatisticState(nextDay);
        AddRefreshState(nextDay, "source-2");
        _db.Insertable(new ProductStoreDailySalesStatistic { Date = nextDay, ProductCode = "P1", BranchCode = "S1", SupplierCode = "A", TotalQuantity = 3, TotalAmount = 30 }).ExecuteCommand();
        Publish(_day, "source-1", Facts());

        var result = (await CreateService().GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = nextDay }, ["S1"])).Data!;

        Assert.Equal("Partial", result.DiscountStatisticStatus);
        Assert.Equal("partial", result.Metrics.DiscountStatus);
        Assert.Equal("complete", Assert.Single(result.Daily, day => day.Date == _day).Metrics.DiscountStatus);
        Assert.Equal("unknown", Assert.Single(result.Daily, day => day.Date == nextDay).Metrics.DiscountStatus);
        Assert.Equal(3m, Assert.Single(result.Daily, day => day.Date == nextDay).Metrics.Quantity);
    }

    [Theory]
    [InlineData("invalid json", "source-1")]
    [InlineData(null, "old-source")]
    public async Task Detail_损坏或过期源快照保留销量并标记不同步(string? payload, string sourceVersion)
    {
        Publish(_day, sourceVersion, Facts(), payload);
        var result = (await CreateService().GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, ["S1"])).Data!;

        Assert.Equal(1m, result.Metrics.Quantity);
        Assert.Equal("OutOfSync", result.DiscountStatisticStatus);
        Assert.Equal("unknown", result.Metrics.DiscountStatus);
    }

    [Fact]
    public async Task Detail_规则过期不能使用旧快照()
    {
        await _db.Updateable<BatchProductSalesDiscountRefreshState>().SetColumns(s => s.RuleVersion == 2).Where(s => s.Date == _day).ExecuteCommandAsync();
        Publish(_day, "source-1", Facts());
        var result = (await CreateService().GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, ["S1"])).Data!;

        Assert.Equal("OutOfSync", result.DiscountStatisticStatus);
        Assert.Equal("unknown", result.Metrics.DiscountStatus);
    }

    [Fact]
    public async Task Detail_发布元数据或分类行损坏不能借总量对账通过()
    {
        // 数量、金额仍与日统计相等，但分类合计不等于总数量，不能发布为可用折扣快照。
        PublishRaw(_day, "source-1", JsonSerializer.Serialize(new[]
        {
            new BatchProductSalesAggregateRow
            {
                Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 1,
                RegularQuantity = 1, DiscountQuantity = 1, SalesAmount = 10,
            },
        }), _day);

        var classificationBroken = (await CreateService().GetDetailAsync(
            new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, ["S1"])).Data!;

        Assert.Equal("OutOfSync", classificationBroken.DiscountStatisticStatus);
        Assert.Equal("unknown", classificationBroken.Metrics.DiscountStatus);
        Assert.Null(classificationBroken.DiscountUpdatedAt);

        await _db.Deleteable<BatchProductSalesDiscountSnapshot>().ExecuteCommandAsync();
        // 空 Payload 或未完成发布同样不能被当作已验证结果使用。
        PublishRaw(_day, "source-1", " ", null);
        var unpublished = (await CreateService().GetDetailAsync(
            new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, ["S1"])).Data!;

        Assert.Equal("OutOfSync", unpublished.DiscountStatisticStatus);
        Assert.Equal("unknown", unpublished.Metrics.DiscountStatus);
        Assert.Null(unpublished.DiscountUpdatedAt);
    }

    [Fact]
    public async Task SnapshotReader_旧表缺少格式列时安全降级为不可用()
    {
        var legacyPath = Path.Combine(Path.GetTempPath(), $"batch-legacy-{Guid.NewGuid():N}.db");
        try
        {
            using var legacyDb = new SqlSugarClient(new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = $"Data Source={legacyPath}",
                IsAutoCloseConnection = true,
            });
            legacyDb.Ado.ExecuteCommand("CREATE TABLE BatchProductSalesDiscountSnapshot (Id TEXT PRIMARY KEY)");
            legacyDb.Ado.ExecuteCommand("CREATE TABLE BatchProductSalesDiscountRefreshState (Date DATETIME PRIMARY KEY)");

            var result = await new BatchProductSalesDiscountSnapshotReader(legacyDb).ReadAsync(
                "P1", _day, _day, ["S1"],
                [new() { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 1, SalesAmount = 10 }],
                default);

            Assert.Equal("Unavailable", result.Status);
            Assert.Equal(1, Assert.Single(result.Rows).UnknownRowCount);
        }
        finally
        {
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
    }

    [Fact]
    public async Task SnapshotReader_旧表降级读取仍在商品日期边界响应取消()
    {
        var legacyPath = Path.Combine(Path.GetTempPath(), $"batch-legacy-cancel-{Guid.NewGuid():N}.db");
        try
        {
            using var legacyDb = new SqlSugarClient(new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = $"Data Source={legacyPath}",
                IsAutoCloseConnection = true,
            });
            legacyDb.Ado.ExecuteCommand("CREATE TABLE BatchProductSalesDiscountSnapshot (Id TEXT PRIMARY KEY)");
            legacyDb.Ado.ExecuteCommand("CREATE TABLE BatchProductSalesDiscountRefreshState (Date DATETIME PRIMARY KEY)");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => new BatchProductSalesDiscountSnapshotReader(legacyDb).ReadManyAsync(
                ["P1", "P2"], [_day, _day.AddDays(1)], ["S1"],
                [new() { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 1, SalesAmount = 10 }], cancellation.Token));
        }
        finally
        {
            if (File.Exists(legacyPath))
                File.Delete(legacyPath);
        }
    }

    [Fact]
    public async Task SnapshotReader_批量读取按商品日期精确匹配()
    {
        var nextDay = _day.AddDays(1);
        AddRefreshState(nextDay, "source-2");
        Publish(_day, "source-1", Facts());
        Publish(nextDay, "source-2", [new()
        {
            Date = nextDay, BranchCode = "S1", ProductCode = "P1", Quantity = 3, DiscountQuantity = 3, SalesAmount = 30,
        }]);
        var statistics = new List<BatchProductSalesAggregateRow>
        {
            new() { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 1, SalesAmount = 10 },
            new() { Date = nextDay, BranchCode = "S1", ProductCode = "P1", Quantity = 3, SalesAmount = 30 },
        };

        var result = await new BatchProductSalesDiscountSnapshotReader(_db).ReadManyAsync(
            ["P1"], [_day, nextDay], ["S1"], statistics, default);

        var read = Assert.Single(result).Value;
        Assert.Equal("Fresh", read.Status);
        Assert.Equal(4m, read.Rows.Sum(row => row.Quantity));
        Assert.All(read.Rows, row => Assert.Equal("S1", row.BranchCode));
    }

    [Fact]
    public async Task SnapshotReader_快照格式以字面量下发且连续日期合并为单个区间()
    {
        var nextDay = _day.AddDays(1);
        AddRefreshState(nextDay, "source-2");
        Publish(_day, "source-1", Facts());
        var snapshotSql = new List<(string Sql, int ParameterCount)>();
        _db.Aop.OnLogExecuting = (sql, parameters) =>
        {
            // SQLite 方言的表名引号与 SQL Server 不同；以读取 PayloadJson 的快照 SELECT 识别目标语句。
            if (sql.Contains("BatchProductSalesDiscountSnapshot", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("PayloadJson", StringComparison.OrdinalIgnoreCase)
                && sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                snapshotSql.Add((sql, parameters?.Length ?? 0));
        };
        try
        {
            await new BatchProductSalesDiscountSnapshotReader(_db).ReadManyAsync(
                ["P1"], [_day, nextDay, _day.AddDays(2)], ["S1"], [], default);
        }
        finally { _db.Aop.OnLogExecuting = null; }

        var (text, parameterCount) = Assert.Single(snapshotSql);
        // 参数化的 SnapshotFormat 会让 SQL Server 放弃过滤索引 IX_BatchSalesDiscount_DailyProduct，必须保持字面量。
        Assert.Contains("[SnapshotFormat] = 2", text);
        Assert.DoesNotContain("@SnapshotFormat", text, StringComparison.OrdinalIgnoreCase);
        // 三个连续日期只剩一组半开区间的起止两个参数。
        Assert.Equal(2, parameterCount);
    }

    [Fact]
    public async Task SnapshotReader_不连续日期之间的快照不会被读取()
    {
        var gapDay = _day.AddDays(1);
        var lastDay = _day.AddDays(2);
        AddRefreshState(gapDay, "source-gap");
        AddRefreshState(lastDay, "source-3");
        Publish(_day, "source-1", Facts());
        Publish(gapDay, "source-gap", [new() { Date = gapDay, BranchCode = "S1", ProductCode = "P1", Quantity = 100, DiscountQuantity = 100, SalesAmount = 1000 }]);
        Publish(lastDay, "source-3", [new() { Date = lastDay, BranchCode = "S1", ProductCode = "P1", Quantity = 3, DiscountQuantity = 3, SalesAmount = 30 }]);
        var statistics = new List<BatchProductSalesAggregateRow>
        {
            new() { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 1, SalesAmount = 10 },
            new() { Date = lastDay, BranchCode = "S1", ProductCode = "P1", Quantity = 3, SalesAmount = 30 },
        };

        var result = await new BatchProductSalesDiscountSnapshotReader(_db).ReadManyAsync(
            ["P1"], [_day, lastDay], ["S1"], statistics, default);

        var read = Assert.Single(result).Value;
        Assert.Equal("Fresh", read.Status);
        Assert.Equal(4m, read.Rows.Sum(row => row.Quantity));
        Assert.DoesNotContain(read.Rows, row => row.Date.Date == gapDay);
    }

    [Fact]
    public void CollapseContiguousDays_连续合并而断点分段()
    {
        var ranges = BatchProductSalesDiscountSnapshotReader.CollapseContiguousDays(
            [_day, _day.AddDays(1), _day.AddDays(3), _day.AddDays(4), _day.AddDays(7)]);
        Assert.Equal(
            [(_day, _day.AddDays(2)), (_day.AddDays(3), _day.AddDays(5)), (_day.AddDays(7), _day.AddDays(8))],
            ranges);
        Assert.Empty(BatchProductSalesDiscountSnapshotReader.CollapseContiguousDays([]));
    }

    [Fact]
    public async Task DailyStore_canonical重算已失败时按退避时间落库且到期前不再领取()
    {
        var day = _day.AddDays(3);
        var now = new DateTime(2026, 9, 18, 8, 40, 0, DateTimeKind.Utc);
        var failedAt = now.AddMinutes(-4);
        _db.Insertable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.ProductStoreDaily, Date = day, Status = "Failed", LastCheckedAtUtc = failedAt,
            ErrorMessage = "商品统计与分店营业额统计不一致",
        }).ExecuteCommand();
        _db.Insertable(new BatchProductSalesDiscountRefreshState
        {
            Date = day, Status = BatchProductSalesDiscountDailyStore.WaitingForCanonicalStatus, RuleVersion = 1,
            RequestedAtUtc = failedAt, NextAttemptAtUtc = now, ReconcileRequested = true,
        }).ExecuteCommand();
        var store = new BatchProductSalesDiscountDailyStore(_db);

        var claim = await store.ClaimNextAsync(now, [], default);
        Assert.NotNull(claim);
        Assert.Equal(day, claim!.State.Date.Date);
        var canonical = await store.ReadCanonicalStateAsync(day, default);
        Assert.Equal("Failed", canonical.Status);
        Assert.Equal(failedAt, canonical.CheckedAtUtc);
        var decision = BatchProductSalesDiscountDailyStore.DecideCanonicalReconciliation(
            claim.State.ReconcileRequested, canonical.Status, canonical.CheckedAtUtc, now);
        Assert.False(decision.Request);
        await store.WaitForCanonicalRefreshAsync(claim, now, default, "日统计重算失败", decision.NextAttemptAtUtc);

        var persisted = ReadDiscountState(day);
        Assert.Equal(BatchProductSalesDiscountDailyStore.WaitingForCanonicalStatus, persisted.Status);
        Assert.Equal(failedAt.Add(BatchProductSalesDiscountDailyStore.FailedCanonicalRetryDelay), persisted.NextAttemptAtUtc);
        Assert.True(persisted.ReconcileRequested);
        Assert.Equal("日统计重算失败", persisted.LastError);
        Assert.Equal(0, persisted.Attempts);
        // 退避期内即使 worker 每轮都来领取，也拿不到这一天。
        var early = await store.ClaimNextAsync(now.AddHours(1), [], default);
        Assert.NotEqual(day, early?.State.Date.Date);
    }

    [Fact]
    public async Task DailyStore_canonical等待不接受早于常规短等待的检查时间()
    {
        var day = _day.AddDays(4);
        var now = new DateTime(2026, 9, 18, 8, 40, 0, DateTimeKind.Utc);
        _db.Insertable(new BatchProductSalesDiscountRefreshState
        {
            Date = day, Status = "Queued", RuleVersion = 1, RequestedAtUtc = now, NextAttemptAtUtc = now,
        }).ExecuteCommand();
        var store = new BatchProductSalesDiscountDailyStore(_db);
        var claim = (await store.ClaimNextAsync(now, [], default))!;
        Assert.Equal(day, claim.State.Date.Date);

        await store.WaitForCanonicalRefreshAsync(claim, now, default, nextAttemptAtUtc: now.AddHours(-1));

        Assert.Equal(now.Add(BatchProductSalesDiscountDailyStore.CanonicalWaitDelay), ReadDiscountState(day).NextAttemptAtUtc);
    }

    [Fact]
    public async Task Detail_越权门店范围继续被拒绝()
    {
        Publish(_day, "source-1", Facts());
        await Assert.ThrowsAsync<BatchProductSalesAnalysisForbiddenException>(() => CreateService().GetDetailAsync(
            new() { ProductCode = "P1", StartDate = _day, EndDate = _day, StoreCodes = ["S2"] }, ["S1"]));
    }

    [Fact]
    public void Reconcile_按分店日四位金额精度核对且未知行抵消时仍保持未知()
    {
        var expected = new[] { new BatchProductSalesAggregateRow { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 2, SalesAmount = 29.9921m } };
        var actual = new[] { new BatchProductSalesAggregateRow { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 2, DiscountQuantity = 2, SalesAmount = 29.99205m, DiscountPriceMin = 14.996025m } };
        Assert.True(BatchProductSalesStatisticReader.TotalsMatch(expected, actual));
        Assert.False(BatchProductSalesStatisticReader.TotalsMatch(expected, [new BatchProductSalesAggregateRow { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 3, SalesAmount = 29.99205m }]));
        var metrics = BatchProductSalesAnalysisService.BuildAggregateMetrics([new() { UnknownQuantity = 2, UnknownRowCount = 1 }, new() { UnknownQuantity = -2, UnknownRowCount = 1 }]);
        Assert.Equal("unknown", metrics.DiscountStatus);
        var fallback = BatchProductSalesDiscountSnapshotReader.MarkUnknown(
            [new() { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 0, UnknownRowCount = 0 }],
            "P1", _day, _day, ["S1"]);
        Assert.Equal("unknown", BatchProductSalesAnalysisService.BuildAggregateMetrics(fallback).DiscountStatus);
    }

    // 格式 1 的租约行为仍由旧 worker 使用；查询端不会再创建这类任务。
    [Fact]
    public async Task LegacyStore_过期租约可接管且旧任务不能覆盖()
    {
        var first = await QueueAndClaim();
        Assert.Null(await _store.ClaimAsync(DateTime.UtcNow));
        var next = await _store.ClaimAsync(DateTime.UtcNow.AddMinutes(4));
        Assert.NotNull(next);
        Assert.NotEqual(first.LeaseToken, next!.LeaseToken);
        Assert.False(await _store.FinishAsync(first, "Fresh", Facts(), DateTime.UtcNow));
    }

    private BatchProductSalesAnalysisService CreateService() => new(_db, Mock.Of<IProductStoreDailyStatisticQueueService>(), NullLogger<BatchProductSalesAnalysisService>.Instance);
    // SQLite 上 DateTime 等值比较会读不到行，测试按单日半开区间读取折扣日状态。
    private BatchProductSalesDiscountRefreshState ReadDiscountState(DateTime date) => _db.Queryable<BatchProductSalesDiscountRefreshState>()
        .Where(x => x.Date >= date.Date && x.Date < date.Date.AddDays(1)).Single();
    private void AddStatisticState(DateTime date) => _db.Insertable(new SalesStatisticRefreshState { StatisticType = SalesStatisticType.ProductStoreDaily, Date = date, Status = "Fresh", CompletedAtUtc = date }).ExecuteCommand();
    private void AddRefreshState(DateTime date, string sourceVersion, int snapshotCount = 1) => _db.Insertable(new BatchProductSalesDiscountRefreshState { Date = date, Status = "Fresh", RuleVersion = 1, SourceVersion = sourceVersion, StatisticsVersion = "sales-only", RequestedAtUtc = date, NextAttemptAtUtc = date, CompletedAtUtc = date, SnapshotCount = snapshotCount }).ExecuteCommand();
    private void Publish(DateTime date, string sourceVersion, List<BatchProductSalesAggregateRow> rows, string? payload = null) => _db.Insertable(new BatchProductSalesDiscountSnapshot { Id = $"snapshot-{date:yyyyMMdd}-{Guid.NewGuid():N}", SnapshotFormat = 2, SourceVersion = sourceVersion, ProductCode = "P1", StartDate = date, EndDate = date, Status = "Fresh", StoreCodesJson = "[]", RequestedAtUtc = date, NextAttemptAtUtc = date, CompletedAtUtc = date, PayloadJson = payload ?? JsonSerializer.Serialize(rows) }).ExecuteCommand();
    private void PublishRaw(DateTime date, string sourceVersion, string? payload, DateTime? completedAtUtc) => _db.Insertable(new BatchProductSalesDiscountSnapshot { Id = $"snapshot-{date:yyyyMMdd}-{Guid.NewGuid():N}", SnapshotFormat = 2, SourceVersion = sourceVersion, ProductCode = "P1", StartDate = date, EndDate = date, Status = "Fresh", StoreCodesJson = "[]", RequestedAtUtc = date, NextAttemptAtUtc = date, CompletedAtUtc = completedAtUtc, PayloadJson = payload }).ExecuteCommand();
    private List<BatchProductSalesAggregateRow> Facts() => [new() { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 1, DiscountQuantity = 1, SalesAmount = 10, ReturnQuantity = 1 }, new() { Date = _day, BranchCode = "S2", ProductCode = "P1", Quantity = 7, RegularQuantity = 7, SalesAmount = 70 }];
    private async Task<BatchProductSalesDiscountSnapshot> QueueAndClaim()
    {
        var version = await new BatchProductSalesStatisticReader(_db).StatusAsync(_day, _day, default);
        await _store.FindOrQueueAsync(BatchProductSalesDiscountStore.Create("P1", _day, _day, ["S1"], version.Version), default);
        return (await _store.ClaimAsync(DateTime.UtcNow))!;
    }
    public void Dispose() { _db.Dispose(); if (File.Exists(_path)) File.Delete(_path); }
}
