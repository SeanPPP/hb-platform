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
        _db.CodeFirst.InitTables<Product, Store, SalesStatisticRefreshState, ProductStoreDailySalesStatistic>();
        // SQLite 测试使用 TEXT 承载 SQL Server nvarchar(max)，业务字段保持一致。
        _db.Ado.ExecuteCommand("""
            CREATE TABLE BatchProductSalesDiscountSnapshot (
                Id TEXT PRIMARY KEY, SourceVersion TEXT NOT NULL, ProductCode TEXT NOT NULL,
                StartDate DATETIME NOT NULL, EndDate DATETIME NOT NULL, StoreCodesJson TEXT NOT NULL,
                Status TEXT NOT NULL, Attempts INTEGER NOT NULL, RequestedAtUtc DATETIME NOT NULL,
                NextAttemptAtUtc DATETIME NOT NULL, LeaseToken TEXT, LeaseUntilUtc DATETIME,
                CompletedAtUtc DATETIME, PayloadJson TEXT);
            """);
        _db.Insertable(new Product { ProductCode = "P1", ItemNumber = "001", ProductName = "测试", IsDeleted = false }).ExecuteCommand();
        _db.Insertable(new[] { new Store { StoreCode = "S1", StoreName = "一店", IsDeleted = false }, new Store { StoreCode = "S2", StoreName = "二店", IsDeleted = false } }).ExecuteCommand();
        _db.Insertable(new SalesStatisticRefreshState { StatisticType = SalesStatisticType.ProductStoreDaily, Date = _day,
            Status = "Fresh", CompletedAtUtc = _day }).ExecuteCommand();
        _db.Insertable(new[]
        {
            new ProductStoreDailySalesStatistic { Date = _day, ProductCode = "P1", BranchCode = "S1", SupplierCode = "A", TotalQuantity = 2, TotalAmount = 20 },
            new ProductStoreDailySalesStatistic { Date = _day, ProductCode = "P1", BranchCode = "S1", SupplierCode = "B", TotalQuantity = -1, TotalAmount = -10 },
            new ProductStoreDailySalesStatistic { Date = _day, ProductCode = "P1", BranchCode = "S2", SupplierCode = "A", TotalQuantity = 7, TotalAmount = 70 },
        }).ExecuteCommand();
        _store = new(_db);
    }

    [Fact]
    public async Task Detail_只用中间表且等待分类不会伪装未知或零_保持门店权限()
    {
        // 服务只接收日统计库；该 SQLite 中没有任何 POSM/HBS 表。
        var service = new BatchProductSalesAnalysisService(_db, Mock.Of<IProductStoreDailyStatisticQueueService>(),
            NullLogger<BatchProductSalesAnalysisService>.Instance);
        var request = new BatchProductSalesDetailRequestDto { ProductCode = "P1", StartDate = _day, EndDate = _day };
        var result = (await service.GetDetailAsync(request, ["S1"])).Data!;
        Assert.Equal(1m, result.Metrics.Quantity);
        Assert.Equal(10m, result.Metrics.SalesAmount);
        Assert.Equal("pending", result.Metrics.DiscountStatus);
        Assert.Equal("Queued", result.DiscountStatisticStatus);
        Assert.Equal("S1", Assert.Single(result.Branches).BranchCode);
        Assert.Equal(1m, Assert.Single(result.Daily).Metrics.Quantity);
        await service.GetDetailAsync(request, ["S1"]);
        Assert.Equal(1, await _db.Queryable<BatchProductSalesDiscountSnapshot>().CountAsync());
        request.StoreCodes = ["S2"];
        await Assert.ThrowsAsync<BatchProductSalesAnalysisForbiddenException>(() => service.GetDetailAsync(request, ["S1"]));
    }

    [Fact]
    public async Task Detail_保留有效分店代码原有去空格规则()
    {
        await _db.Ado.ExecuteCommandAsync("UPDATE Store SET StoreCode = ' S1 ' WHERE StoreCode = 'S1'");
        var service = new BatchProductSalesAnalysisService(_db, Mock.Of<IProductStoreDailyStatisticQueueService>(), NullLogger<BatchProductSalesAnalysisService>.Instance);
        var result = (await service.GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, ["S1"])).Data!;
        Assert.Equal(["S1"], result.StoreCodes);
        Assert.Equal(1m, result.Metrics.Quantity);
    }

    [Fact]
    public void Reconcile_按日统计四位精度核对金额_不放宽数量或真实金额差异()
    {
        var expected = new[] { new BatchProductSalesAggregateRow { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 2, SalesAmount = 29.9921m } };
        var actual = new[] { new BatchProductSalesAggregateRow { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 2, DiscountQuantity = 2, SalesAmount = 29.99205m, DiscountPriceMin = 14.996025m } };
        Assert.True(BatchProductSalesStatisticReader.TotalsMatch(expected, actual));
        var published = Assert.Single(BatchProductSalesStatisticReader.PrepareSnapshot(expected, actual));
        Assert.Equal(29.9921m, published.SalesAmount);
        Assert.Equal(2, published.DiscountQuantity);
        Assert.Equal(14.996025m, published.DiscountPriceMin);
        actual[0].SalesAmount = 29.9922m;
        Assert.False(BatchProductSalesStatisticReader.TotalsMatch(expected, actual));
        actual[0].SalesAmount = 29.99205m;
        actual[0].Quantity = 3;
        Assert.False(BatchProductSalesStatisticReader.TotalsMatch(expected, actual));
    }

    [Fact]
    public async Task Detail_快照表未安装仍返回销量()
    {
        _db.DbMaintenance.DropTable<BatchProductSalesDiscountSnapshot>();
        var service = new BatchProductSalesAnalysisService(_db, Mock.Of<IProductStoreDailyStatisticQueueService>(), NullLogger<BatchProductSalesAnalysisService>.Instance);
        var result = (await service.GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, ["S1"])).Data!;
        Assert.Equal(1m, result.Metrics.Quantity);
        Assert.Equal("Unavailable", result.DiscountStatisticStatus);
        Assert.Equal("pending", result.Metrics.DiscountStatus);
    }

    [Theory]
    [InlineData(null, "OutOfSync")]
    [InlineData("invalid json", "Unavailable")]
    public async Task Detail_损坏快照不会污染已有销量(string? payload, string expectedStatus)
    {
        var job = await QueueAndClaim();
        await _db.Updateable<BatchProductSalesDiscountSnapshot>().SetColumns(s => s.Status == "Fresh")
            .SetColumns(s => s.PayloadJson == payload).Where(s => s.Id == job.Id).ExecuteCommandAsync();
        var service = new BatchProductSalesAnalysisService(_db, Mock.Of<IProductStoreDailyStatisticQueueService>(), NullLogger<BatchProductSalesAnalysisService>.Instance);
        var result = (await service.GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, ["S1"])).Data!;
        Assert.Equal(1m, result.Metrics.Quantity);
        Assert.Equal(expectedStatus, result.DiscountStatisticStatus);
        Assert.Equal("pending", result.Metrics.DiscountStatus);
    }

    [Fact]
    public async Task Version_仅检查时间变化不会重算但源水位变化失效()
    {
        var reader = new BatchProductSalesStatisticReader(_db);
        var before = await reader.StatusAsync(_day, _day, default);
        await _db.Updateable<SalesStatisticRefreshState>().SetColumns(s => s.LastCheckedAtUtc == DateTime.UtcNow).Where(s => s.Date == _day).ExecuteCommandAsync();
        Assert.Equal(before.Version, (await reader.StatusAsync(_day, _day, default)).Version);
        await _db.Updateable<SalesStatisticRefreshState>().SetColumns(s => s.LastSourceUploadTime == DateTime.UtcNow).Where(s => s.Date == _day).ExecuteCommandAsync();
        Assert.NotEqual(before.Version, (await reader.StatusAsync(_day, _day, default)).Version);
    }

    [Fact]
    public async Task Detail_缺失统计不补零且不排折扣任务()
    {
        var service = new BatchProductSalesAnalysisService(_db, Mock.Of<IProductStoreDailyStatisticQueueService>(),
            NullLogger<BatchProductSalesAnalysisService>.Instance);
        var result = (await service.GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = _day.AddDays(1) }, ["S1"])).Data!;
        Assert.Equal("Pending", result.StatisticStatus);
        Assert.Empty(result.Daily);
        Assert.Empty(result.Branches);
        Assert.Equal(0, await _db.Queryable<BatchProductSalesDiscountSnapshot>().CountAsync());
    }

    [Fact]
    public async Task Snapshot_后台完成发布后前台展示折扣与退货()
    {
        var job = await QueueAndClaim();
        Assert.Equal("Fresh", await _store.ComputeAsync(job, (_, _, _, _, _) => Task.FromResult(Facts()), default));
        var service = new BatchProductSalesAnalysisService(_db, Mock.Of<IProductStoreDailyStatisticQueueService>(), NullLogger<BatchProductSalesAnalysisService>.Instance);
        var result = (await service.GetDetailAsync(new() { ProductCode = "P1", StartDate = _day, EndDate = _day }, ["S1"])).Data!;
        Assert.Equal("Fresh", result.DiscountStatisticStatus);
        Assert.Equal("complete", result.Metrics.DiscountStatus);
        Assert.Equal(1, result.Metrics.DiscountQuantity);
        Assert.Equal(1, result.Metrics.ReturnQuantity);
        Assert.NotNull(result.DiscountUpdatedAt);
    }

    [Fact]
    public async Task Snapshot_计算期间源版本变化拒绝发布()
    {
        var job = await QueueAndClaim();
        var status = await _store.ComputeAsync(job, async (_, _, _, _, _) =>
        {
            await _db.Updateable<SalesStatisticRefreshState>().SetColumns(s => s.CompletedAtUtc == _day.AddMinutes(1))
                .Where(s => s.Date == _day).ExecuteCommandAsync();
            return Facts();
        }, default);
        Assert.Equal("Superseded", status);
        Assert.Null((await _store.FindAsync(job.Id))!.PayloadJson);
    }

    [Fact]
    public async Task Snapshot_分店日数量不符不发布折扣()
    {
        var job = await QueueAndClaim();
        var status = await _store.ComputeAsync(job, (_, _, _, _, _) => Task.FromResult(new List<BatchProductSalesAggregateRow>()), default);
        Assert.Equal("OutOfSync", status);
        Assert.Null((await _store.FindAsync(job.Id))!.PayloadJson);
    }

    [Fact]
    public async Task Lease_过期可接管且旧任务不能覆盖_崩溃重试有上限()
    {
        var first = await QueueAndClaim();
        Assert.Null(await _store.ClaimAsync(DateTime.UtcNow));
        var next = await _store.ClaimAsync(DateTime.UtcNow.AddMinutes(4));
        Assert.NotNull(next);
        Assert.NotEqual(first.LeaseToken, next!.LeaseToken);
        Assert.False(await _store.FinishAsync(first, "Fresh", Facts(), DateTime.UtcNow));
        Assert.Equal(2, next.Attempts);
        Assert.NotNull(await _store.ClaimAsync(DateTime.UtcNow.AddMinutes(8)));
        Assert.Null(await _store.ClaimAsync(DateTime.UtcNow.AddMinutes(12)));
        Assert.Equal("Failed", (await _store.FindAsync(first.Id))!.Status);
    }

    [Fact]
    public async Task Retry_失败退避且最多三次()
    {
        var job = await QueueAndClaim();
        var now = DateTime.UtcNow;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.True(await _store.FinishAsync(job, "Failed", null, now));
            Assert.Null(await _store.ClaimAsync(now));
            now = now.AddMinutes(attempt * 2 + 1);
            var retry = await _store.ClaimAsync(now);
            if (attempt == 3) Assert.Null(retry);
            else { job = retry!; Assert.Equal(attempt + 1, job.Attempts); }
        }
    }

    [Fact]
    public void Key_分店排序不影响键_权限日期版本均隔离()
    {
        string Key(string product, DateTime end, string[] stores, string version) => BatchProductSalesDiscountStore.Create(product, _day, end, stores, version).Id;
        var original = Key("p1", _day, ["s2", "s1"], "V1");
        Assert.Equal(original, Key("P1", _day, ["S1", "S2", "S1"], "V1"));
        Assert.NotEqual(original, Key("P1", _day, ["S1"], "V1"));
        Assert.NotEqual(original, Key("P1", _day, ["S1", "S2"], "V2"));
        Assert.NotEqual(original, Key("P1", _day.AddDays(1), ["S1", "S2"], "V1"));
    }

    private List<BatchProductSalesAggregateRow> Facts() => [new() { Date = _day, BranchCode = "S1", ProductCode = "P1", Quantity = 1, DiscountQuantity = 1, SalesAmount = 10, ReturnQuantity = 1 }];
    private async Task<BatchProductSalesDiscountSnapshot> QueueAndClaim()
    {
        var version = await new BatchProductSalesStatisticReader(_db).StatusAsync(_day, _day, default);
        await _store.FindOrQueueAsync(BatchProductSalesDiscountStore.Create("P1", _day, _day, ["S1"], version.Version), default);
        return (await _store.ClaimAsync(DateTime.UtcNow))!;
    }
    public void Dispose() { _db.Dispose(); if (File.Exists(_path)) File.Delete(_path); }
}
