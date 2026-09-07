using System.Reflection;
using DataTable = System.Data.DataTable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using System.Data.Common;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class RevenueReportSnapshotTests : IDisposable
{
    private readonly SqliteConnection _localConnection = new("Data Source=:memory:");
    private readonly SqliteConnection _posmConnection = new("Data Source=:memory:");
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;
    private readonly SalesDashboardReactService _service;

    public RevenueReportSnapshotTests()
    {
        _localConnection.Open();
        _posmConnection.Open();
        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _posmDb = new SqlSugarClient(CreateConnectionConfig(_posmConnection.ConnectionString));
        _localDb.CodeFirst.InitTables(
            typeof(StoreSalesStatistic),
            typeof(HourlySalesStatistic),
            typeof(SalesStatisticRefreshState)
        );
        _service = new SalesDashboardReactService(
            CreateSqlSugarContext(_localDb),
            CreatePosmSqlSugarContext(_posmDb),
            Mock.Of<IMapper>(),
            NullLogger<SalesDashboardReactService>.Instance,
            new MemoryCache(new MemoryCacheOptions())
        );
    }

    [Fact]
    public async Task SQLServer压缩结果保留中文金额精度日期和空值()
    {
        const string json = """[{"Date":"2026-08-01","BranchName":"一店","TotalAmount":123456789.12,"OrderCount":null,"Period":1}]""";
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(Encoding.Unicode.GetBytes(json));
        var table = new DataTable();
        table.Columns.Add("Data", typeof(byte[]));
        table.Rows.Add(buffer.ToArray());
        using var reader = table.CreateDataReader();
        var method = typeof(SalesDashboardReactService)
            .GetMethod("ReadCompressedRevenueRowsAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(CompressedRow));
        var rows = await (Task<List<CompressedRow>>)method.Invoke(null, new object[] { reader, CancellationToken.None })!;
        var row = Assert.Single(rows);
        Assert.Equal(new DateTime(2026, 8, 1), row.Date);
        Assert.Equal(DateTimeKind.Unspecified, row.Date.Kind);
        Assert.Equal("一店", row.BranchName);
        Assert.Equal(123456789.12m, row.TotalAmount);
        Assert.Null(row.OrderCount);
        Assert.Equal(1, row.Period);
    }

    [Fact]
    public async Task 统计水位按Utc序列化并保留Z后缀()
    {
        var day = new DateTime(2026, 9, 7);
        var completedAtUtc = new DateTime(2026, 9, 7, 3, 32, 59, DateTimeKind.Utc);
        await SeedStoreAsync(day, "S1", "一店", 100m, 5);
        await SeedHourlyAsync(day, 9, "S1", 100m, 5);
        await SeedFreshStateAsync(day, SalesStatisticType.StoreSales);
        await SeedFreshStateAsync(day, SalesStatisticType.HourlySales);
        await _localDb.Updateable<SalesStatisticRefreshState>()
            .SetColumns(state => new SalesStatisticRefreshState
            {
                LastAggregatedAtUtc = completedAtUtc,
                CompletedAtUtc = completedAtUtc,
            })
            .Where(state => state.Date == day)
            .ExecuteCommandAsync();

        var result = await _service.GetRevenueReportSnapshotAsync(
            new DateRangeDto { StartDate = day, EndDate = day },
            new List<string> { "S1" },
            new List<string> { "S1" });

        Assert.Equal(DateTimeKind.Utc, result.StatisticUpdatedAt!.Value.Kind);
        Assert.Contains("2026-09-07T03:32:59Z", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task SQLServer压缩结果缺失时拒绝返回空统计()
    {
        var table = new DataTable();
        table.Columns.Add("Data", typeof(byte[]));
        using var reader = table.CreateDataReader();
        var method = typeof(SalesDashboardReactService)
            .GetMethod("ReadCompressedRevenueRowsAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(CompressedRow));
        var task = (Task<List<CompressedRow>>)method.Invoke(null, new object[] { reader, CancellationToken.None })!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    private sealed class CompressedRow
    {
        public DateTime Date { get; set; }
        public string? BranchName { get; set; }
        public decimal TotalAmount { get; set; }
        public int? OrderCount { get; set; }
        public int? Period { get; set; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 空focus保留授权排行且与未筛选缓存隔离(bool emptyFirst)
    {
        var day = new DateTime(2026, 9, 7);
        await SeedStoreAsync(day, "S1", "一店", 100m, 5);
        await SeedHourlyAsync(day, 9, "S1", 100m, 5);
        await SeedFreshStateAsync(day, SalesStatisticType.StoreSales);
        await SeedFreshStateAsync(day, SalesStatisticType.HourlySales);
        var range = new DateRangeDto { StartDate = day, EndDate = day };

        foreach (var empty in new[] { emptyFirst, !emptyFirst, emptyFirst })
        {
            var result = await _service.GetRevenueReportSnapshotAsync(
                range, new List<string> { "S1" }, empty ? new List<string>() : null);
            Assert.Equal(100m, Assert.Single(result.Branches).Revenue);
            Assert.False(result.StatisticsPending);
            if (empty)
            {
                Assert.Empty(result.Hourly);
                Assert.Empty(result.Weekly);
            }
            else
            {
                Assert.Equal(100m, Assert.Single(result.Hourly).Revenue);
                Assert.NotEmpty(result.Weekly);
            }
        }
    }

    [Fact]
    public async Task SQLServer聚焦单店后新发布水位立即替换旧缓存()
    {
        var day = new DateTime(2026, 9, 7);
        var published = new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc);
        var revenue = 100m;
        // 通过真实批次读取代码消费五个压缩结果集，复现两家授权门店、只聚焦一家的 SQL Server 路径。
        var command = new Mock<DbCommand>();
        command.SetupAllProperties();
        using var parametersOwner = new Microsoft.Data.SqlClient.SqlCommand();
        command.Protected().Setup<DbParameterCollection>("DbParameterCollection").Returns(parametersOwner.Parameters);
        command.Protected().Setup<DbParameter>("CreateDbParameter").Returns(() => new Microsoft.Data.SqlClient.SqlParameter());
        command.Protected().Setup<Task<DbDataReader>>("ExecuteDbDataReaderAsync",
            ItExpr.IsAny<System.Data.CommandBehavior>(), ItExpr.IsAny<CancellationToken>())
            .Returns(() => Task.FromResult<DbDataReader>(CreateBatchReader()));
        var connection = new Mock<DbConnection>();
        connection.SetupGet(value => value.State).Returns(System.Data.ConnectionState.Open);
        connection.Protected().Setup<DbCommand>("CreateDbCommand").Returns(command.Object);
        var ado = new Mock<IAdo>();
        ado.SetupGet(value => value.Transaction).Returns(new Mock<DbTransaction>().Object);
        ado.SetupGet(value => value.Connection).Returns(connection.Object);
        var db = new Mock<ISqlSugarClient>();
        db.SetupGet(value => value.Ado).Returns(ado.Object);
        db.SetupGet(value => value.CurrentConnectionConfig).Returns(new ConnectionConfig { DbType = DbType.SqlServer });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new SalesDashboardReactService(CreateSqlSugarContext(db.Object),
            CreatePosmSqlSugarContext(_posmDb), Mock.Of<IMapper>(),
            NullLogger<SalesDashboardReactService>.Instance, cache);
        var range = new DateRangeDto { StartDate = day, EndDate = day };
        var branches = new List<string> { "S1", "S2" };
        var focus = new List<string> { "S1" };

        var first = await service.GetRevenueReportSnapshotAsync(range, branches, focus);
        Assert.False(first.StatisticsPending);
        Assert.Equal(100m, Assert.Single(first.Hourly).Revenue);
        published = published.AddMinutes(30);
        revenue = 150m;
        parametersOwner.Parameters.Clear();
        var updated = await service.GetRevenueReportSnapshotAsync(range, branches, focus);
        Assert.False(updated.StatisticsPending);
        Assert.NotEqual(first.CacheVersion, updated.CacheVersion);
        Assert.Equal(150m, Assert.Single(updated.Hourly).Revenue);
        Assert.Equal(150m, updated.Branches.Single(row => row.BranchCode == "S1").Revenue);

        DbDataReader CreateBatchReader()
        {
            var payloads = new object[]
            {
                new[] { SalesStatisticType.StoreSales, SalesStatisticType.HourlySales }.Select(type => new
                { StatisticType = type, Date = day, Status = "Fresh", LastAggregatedAtUtc = published, CompletedAtUtc = published }),
                new[] {
                    new { Date = day, BranchCode = "S1", BranchName = "一店", TotalAmount = revenue, OrderCount = 5 },
                    new { Date = day, BranchCode = "S2", BranchName = "二店", TotalAmount = 40m, OrderCount = 2 } },
                new[] { new { Date = day, BranchCode = "S1", Hour = 9, TotalAmount = revenue, OrderCount = 5, Period = 0 } },
                Array.Empty<object>(),
                new[] { new { Date = day, BranchCode = "S1" } },
            };
            var tables = payloads.Select(payload =>
            {
                using var buffer = new MemoryStream();
                using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
                    gzip.Write(Encoding.Unicode.GetBytes(JsonSerializer.Serialize(payload)));
                var table = new DataTable();
                table.Columns.Add("Data", typeof(byte[]));
                table.Rows.Add(buffer.ToArray());
                return table;
            }).ToArray();
            return new System.Data.DataTableReader(tables);
        }
    }

    [Fact]
    public async Task 聚合快照一次返回排行分时和完整周层级并区分focus范围()
    {
        var current = new DateTime(2026, 9, 7);
        var compare = new DateTime(2025, 9, 8);
        await SeedStoreAsync(current, "S1", "一店", 100m, 5);
        await SeedStoreAsync(current, "S2", "二店", 40m, 2);
        await SeedStoreAsync(compare, "S1", "一店", 80m, 4);
        await SeedStoreAsync(compare, "S2", "二店", 60m, 3);
        await SeedHourlyAsync(current, 9, "S1", 100m, 5);
        await SeedHourlyAsync(compare, 9, "S1", 80m, 4);
        await SeedFreshStateAsync(current, SalesStatisticType.StoreSales);
        await SeedFreshStateAsync(current, SalesStatisticType.HourlySales);
        await SeedFreshStateAsync(compare, SalesStatisticType.StoreSales);
        await SeedFreshStateAsync(compare, SalesStatisticType.HourlySales);

        var result = await _service.GetRevenueReportSnapshotAsync(
            new DateRangeDto
            {
                StartDate = current,
                EndDate = current,
                CompareStartDate = compare,
                CompareEndDate = compare,
            },
            new List<string> { "S1", "S2" },
            new List<string> { "S1" }
        );

        Assert.False(result.StatisticsPending);
        Assert.Equal("Fresh", result.StatisticStatus);
        Assert.Equal(2, result.Branches.Count);
        Assert.Single(result.Hourly);
        var week = Assert.Single(result.Weekly);
        Assert.Equal("week", week.Level);
        var branch = Assert.Single(week.Children!);
        Assert.Equal("S1", branch.Key.Split('-').Last());
        Assert.Single(branch.Children!);
        Assert.Equal(100m, result.Branches[0].Revenue);
        Assert.Equal(80m, result.Branches[0].RevenueLY);
    }

    [Fact]
    public void 分时预聚合行按Period独立归入当前期和同期()
    {
        var current = new DateTime(2026, 9, 7);
        var compare = new DateTime(2025, 9, 8);
        var rowType = typeof(SalesDashboardReactService).GetNestedType(
            "RevenueSnapshotHourlyRow",
            BindingFlags.NonPublic
        )!;
        var rows = Array.CreateInstance(rowType, 2);
        rows.SetValue(CreateHourlyRow(rowType, compare, 0, 100m, 5), 0);
        rows.SetValue(CreateHourlyRow(rowType, current, 1, 80m, 4), 1);

        var method = typeof(SalesDashboardReactService).GetMethod(
            "BuildRevenueHourly",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        var result = Assert.IsType<List<ExecutiveHourlyTrafficDto>>(method.Invoke(
            null,
            new object?[] { rows, current, current, compare, compare }
        ));

        var hourly = Assert.Single(result);
        Assert.Equal(100m, hourly.Revenue);
        Assert.Equal(80m, hourly.RevenueLY);
        Assert.Equal(5, hourly.OrderCount);
        Assert.Equal(4, hourly.OrderCountLY);

        static object CreateHourlyRow(
            Type rowType,
            DateTime date,
            int period,
            decimal revenue,
            int orders
        )
        {
            var row = Activator.CreateInstance(rowType)!;
            rowType.GetProperty("Date")!.SetValue(row, date);
            rowType.GetProperty("Period")!.SetValue(row, (int?)period);
            rowType.GetProperty("Hour")!.SetValue(row, 9);
            rowType.GetProperty("BranchCode")!.SetValue(row, "S1");
            rowType.GetProperty("BranchName")!.SetValue(row, "一店");
            rowType.GetProperty("TotalAmount")!.SetValue(row, revenue);
            rowType.GetProperty("OrderCount")!.SetValue(row, orders);
            return row;
        }
    }

    [Fact]
    public async Task focus分店超出授权范围时不返回越权分时数据()
    {
        var current = new DateTime(2026, 9, 7);
        await SeedStoreAsync(current, "S1", "一店", 100m, 5);
        await SeedStoreAsync(current, "S3", "三店", 300m, 9);
        await SeedHourlyAsync(current, 9, "S1", 100m, 5);
        await SeedHourlyAsync(current, 9, "S3", 300m, 9);
        await SeedFreshStateAsync(current, SalesStatisticType.StoreSales);
        await SeedFreshStateAsync(current, SalesStatisticType.HourlySales);

        var result = await _service.GetRevenueReportSnapshotAsync(
            new DateRangeDto { StartDate = current, EndDate = current },
            new List<string> { "S1" },
            new List<string> { "S3" }
        );

        Assert.Single(result.Branches);
        Assert.Equal("S1", result.Branches[0].BranchCode);
        Assert.DoesNotContain(result.Hourly, item => item.BranchCode == "S3");
    }

    [Fact]
    public async Task 缺少同期分时状态且分店覆盖不全时不能误报Fresh()
    {
        var current = new DateTime(2026, 9, 7);
        var compare = new DateTime(2025, 9, 8);
        await SeedStoreAsync(current, "S1", "一店", 100m, 5);
        await SeedStoreAsync(current, "S2", "二店", 40m, 2);
        await SeedStoreAsync(compare, "S1", "一店", 80m, 4);
        await SeedStoreAsync(compare, "S2", "二店", 60m, 3);
        await SeedHourlyAsync(current, 9, "S1", 100m, 5);
        await SeedHourlyAsync(current, 9, "S2", 40m, 2);
        await SeedHourlyAsync(compare, 9, "S1", 80m, 4);
        await SeedFreshStateAsync(current, SalesStatisticType.StoreSales);
        await SeedFreshStateAsync(current, SalesStatisticType.HourlySales);
        await SeedFreshStateAsync(compare, SalesStatisticType.StoreSales);

        var result = await _service.GetRevenueReportSnapshotAsync(
            new DateRangeDto
            {
                StartDate = current,
                EndDate = current,
                CompareStartDate = compare,
                CompareEndDate = compare,
            },
            new List<string> { "S1", "S2" },
            new List<string> { "S1", "S2" }
        );

        Assert.False(result.StatisticsPending);
        Assert.Equal("Fresh", result.StatisticStatus);
        Assert.True(result.ComparePeriodPending);
        Assert.True(result.HourlyComparePending);
    }

    [Fact]
    public async Task Running刷新期间复用上一版完整bundle而不把它标为Pending()
    {
        var current = new DateTime(2026, 9, 7);
        await SeedStoreAsync(current, "S1", "一店", 100m, 5);
        await SeedHourlyAsync(current, 9, "S1", 100m, 5);
        await SeedFreshStateAsync(current, SalesStatisticType.StoreSales);
        await SeedFreshStateAsync(current, SalesStatisticType.HourlySales);
        var range = new DateRangeDto { StartDate = current, EndDate = current };

        var first = await _service.GetRevenueReportSnapshotAsync(range, new List<string> { "S1" }, new List<string> { "S1" });
        Assert.False(first.StatisticsPending);

        await _localDb.Updateable<SalesStatisticRefreshState>()
            .SetColumns(state => new SalesStatisticRefreshState
            {
                Status = SalesStatisticRefreshStatus.Running,
                CompletedAtUtc = null,
            })
            .Where(state => state.Date == current && state.StatisticType == SalesStatisticType.HourlySales)
            .ExecuteCommandAsync();

        var duringRefresh = await _service.GetRevenueReportSnapshotAsync(range, new List<string> { "S1" }, new List<string> { "S1" });
        Assert.False(duringRefresh.StatisticsPending);
        Assert.True(duringRefresh.RefreshInProgress);
        Assert.Equal(first.Branches[0].Revenue, duringRefresh.Branches[0].Revenue);
    }

    private async Task SeedStoreAsync(DateTime date, string branchCode, string branchName, decimal revenue, int orders)
    {
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = date,
            BranchCode = branchCode,
            BranchName = branchName,
            TotalAmount = revenue,
            OrderCount = orders,
            TotalQuantity = orders,
            CustomerCount = orders,
            AverageOrderValue = orders > 0 ? revenue / orders : 0,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedHourlyAsync(DateTime date, int hour, string branchCode, decimal revenue, int orders)
    {
        await _localDb.Insertable(new HourlySalesStatistic
        {
            Date = date,
            Hour = hour,
            BranchCode = branchCode,
            BranchName = branchCode,
            TotalAmount = revenue,
            OrderCount = orders,
            TotalQuantity = orders,
            CustomerCount = orders,
            AverageOrderValue = orders > 0 ? revenue / orders : 0,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedFreshStateAsync(DateTime date, string type)
    {
        var completed = DateTime.UtcNow;
        await _localDb.Insertable(new SalesStatisticRefreshState
        {
            Date = date,
            StatisticType = type,
            Status = SalesStatisticRefreshStatus.Fresh,
            LastAggregatedAtUtc = completed,
            CompletedAtUtc = completed,
        }).ExecuteCommandAsync();
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) => new()
    {
        ConnectionString = connectionString,
        DbType = DbType.Sqlite,
        IsAutoCloseConnection = false,
        InitKeyType = InitKeyType.Attribute,
    };

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext CreatePosmSqlSugarContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _localConnection.Dispose();
        _posmConnection.Dispose();
    }
}
