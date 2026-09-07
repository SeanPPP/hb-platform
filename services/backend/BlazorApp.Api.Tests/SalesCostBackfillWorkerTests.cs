using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesCostBackfillWorkerTests
{
    [Fact]
    public void 自动重试先选最近已完成日并保留历史名额()
    {
        var recent = new[]
        {
            new DateTime(2026, 9, 6),
            new DateTime(2026, 9, 5),
        };
        var history = new[]
        {
            new AutomaticRetryCandidate(new DateTime(2025, 1, 2), new DateTime(2026, 9, 5)),
            new AutomaticRetryCandidate(new DateTime(2025, 1, 1), null),
        };

        Assert.Equal(
            new DateTime(2026, 9, 6),
            SalesCostBackfillWorker.SelectAutomaticRetryDate(recent, history, 0, 31));
        Assert.Equal(
            new DateTime(2025, 1, 1),
            SalesCostBackfillWorker.SelectAutomaticRetryDate(recent, history, 30, 31));
    }

    [Fact]
    public void 自动重试历史日期按未检查及最久检查轮转()
    {
        var history = new[]
        {
            new AutomaticRetryCandidate(new DateTime(2025, 3, 2), new DateTime(2026, 9, 1)),
            new AutomaticRetryCandidate(new DateTime(2025, 3, 1), new DateTime(2026, 9, 2)),
            new AutomaticRetryCandidate(new DateTime(2025, 3, 3), null),
        };

        Assert.Equal(
            new DateTime(2025, 3, 3),
            SalesCostBackfillWorker.SelectAutomaticRetryDate([], history, 0, 31));
        Assert.Equal(
            new DateTime(2025, 3, 2),
            SalesCostBackfillWorker.SelectAutomaticRetryDate(
                [],
                history.Where(candidate => candidate.Date != new DateTime(2025, 3, 3)).ToArray(),
                30,
                31));
    }

    [Fact]
    public void 自动重试达到每日额度后不再选择日期()
    {
        var selected = SalesCostBackfillWorker.SelectAutomaticRetryDate(
            [new DateTime(2026, 9, 6)],
            [],
            checkedToday: 31,
            maxDatesPerDay: 31);

        Assert.Null(selected);
    }
}

[Collection("SalesCostBackfillSqlServer")]
public sealed class SalesCostBackfillWorkerSqlServerTests
{
    [SalesCostBackfillSqlServerFact]
    public async Task 自动发现查询路径隔离手工批次并限制每日额度且排除业务日()
    {
        await using var fixture = await WorkerFixture.CreateAsync();

        // 手工批次的日期不应计入自动额度；即使存在手工检查，仍应登记一个自动批次。
        var manualDate = SalesStatisticsBusinessDate.Today().AddDays(-10);
        await fixture.InsertBatchAsync(manualDate, automatic: false, updatedAtUtc: DateTime.UtcNow);
        await fixture.InsertGapAsync(manualDate);
        Assert.True(await fixture.ScheduleAsync());
        Assert.Equal(1, await fixture.CountBatchesAsync(automatic: true));

        await fixture.ClearAsync();

        // 31 个不同日期已在本 UTC 日由自动批次检查后，不再创建第 32 个批次。
        var checkedAt = DateTime.UtcNow;
        for (var i = 0; i < 31; i++)
            await fixture.InsertBatchAsync(new DateTime(2025, 1, 1).AddDays(i), automatic: true, updatedAtUtc: checkedAt);
        await fixture.InsertGapAsync(new DateTime(2025, 3, 1));
        Assert.False(await fixture.ScheduleAsync());
        Assert.Equal(31, await fixture.CountBatchesAsync(automatic: true));

        await fixture.ClearAsync();

        // 同时存在昨天和业务今天的缺口时，只能为已完成的昨天创建同日 Previewing 批次。
        var today = SalesStatisticsBusinessDate.Today();
        var latestCompleted = today.AddDays(-1);
        await fixture.InsertGapAsync(latestCompleted);
        await fixture.InsertGapAsync(today);
        Assert.True(await fixture.ScheduleAsync());
        var created = await fixture.Db.Queryable<SalesCostBackfillBatch>()
            .Where(x => x.Automatic).SingleAsync();
        Assert.Equal(latestCompleted, created.StartDate.Date);
        Assert.Equal("Previewing", created.Status);
        Assert.Equal(1, await fixture.Db.Queryable<SalesCostBackfillDay>()
            .Where(x => x.BatchId == created.Id && x.Date == latestCompleted).CountAsync());
        Assert.Equal(0, await fixture.Db.Queryable<SalesCostBackfillDay>()
            .Where(x => x.BatchId == created.Id && x.Date == today).CountAsync());
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly SqlSugarClient master;
        private readonly string database;
        private readonly SqlSugarClient db;
        private readonly SqlSugarContext context;
        private readonly SalesCostBackfillService service;
        private readonly SalesCostBackfillWorker worker;

        internal ISqlSugarClient Db => db;

        private WorkerFixture(string connection, string database)
        {
            this.database = database;
            var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
            master = Client(builder.ConnectionString);
            builder.InitialCatalog = database;
            db = Client(builder.ConnectionString);
            context = Inject<SqlSugarContext>(db);
            service = new SalesCostBackfillService(context, null!, null!, null!,
                NullLogger<SalesCostBackfillService>.Instance);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SalesStatistics:CostBackfillAutoRetryMaxDatesPerUtcDay"] = "31",
                    ["SalesStatistics:CostBackfillAutoRetryRecentCompletedDays"] = "7",
                }).Build();
            worker = new SalesCostBackfillWorker(null!, configuration,
                NullLogger<SalesCostBackfillWorker>.Instance);
        }

        internal static async Task<WorkerFixture> CreateAsync()
        {
            var connection = Environment.GetEnvironmentVariable("COST_BACKFILL_SQLSERVER_TEST_CONNECTION")!;
            var builder = new SqlConnectionStringBuilder(connection);
            Assert.Equal("127.0.0.1,11439", builder.DataSource);
            var fixture = new WorkerFixture(connection, "HBcost_worker_test_" + Guid.NewGuid().ToString("N"));
            await fixture.master.Ado.ExecuteCommandAsync($"CREATE DATABASE [{fixture.database}]");
            fixture.db.CodeFirst.InitTables(typeof(ProductStoreDailySalesStatistic),
                typeof(SalesCostBackfillBatch), typeof(SalesCostBackfillDay), typeof(SalesCostBackfillItem));
            return fixture;
        }

        internal async Task<bool> ScheduleAsync()
        {
            var method = typeof(SalesCostBackfillWorker).GetMethod("ScheduleRetryAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            return await (Task<bool>)method.Invoke(worker, [context, service])!;
        }

        internal Task<int> CountBatchesAsync(bool automatic) => db.Queryable<SalesCostBackfillBatch>()
            .Where(x => x.Automatic == automatic).CountAsync();

        internal async Task InsertBatchAsync(DateTime date, bool automatic, DateTime updatedAtUtc)
        {
            var id = Guid.NewGuid();
            await db.Insertable(new SalesCostBackfillBatch
            {
                Id = id, StartDate = date.Date, EndDate = date.Date, Automatic = automatic,
                Status = "Applied", RequestedBy = "sql-test", CreatedAtUtc = updatedAtUtc,
                UpdatedAtUtc = updatedAtUtc,
            }).ExecuteCommandAsync();
            await db.Insertable(new SalesCostBackfillDay
            {
                BatchId = id, Date = date.Date, Status = "Applied", UpdatedAtUtc = updatedAtUtc,
            }).ExecuteCommandAsync();
        }

        internal Task InsertGapAsync(DateTime date) => db.Insertable(new ProductStoreDailySalesStatistic
        {
            Date = date.Date, BranchCode = "TEST", SupplierCode = "TEST", ProductCode = "TEST-" + date.ToString("yyyyMMdd"),
            TotalQuantity = 1, TotalAmount = 10m, OrderCount = 1, CostSource = "Missing", UpdateTime = DateTime.Now,
        }).ExecuteCommandAsync();

        internal async Task ClearAsync()
        {
            await db.Deleteable<SalesCostBackfillDay>().Where(x => true).ExecuteCommandAsync();
            await db.Deleteable<SalesCostBackfillBatch>().Where(x => true).ExecuteCommandAsync();
            await db.Deleteable<ProductStoreDailySalesStatistic>().Where(x => true).ExecuteCommandAsync();
        }

        public async ValueTask DisposeAsync()
        {
            db.Dispose(); SqlConnection.ClearAllPools();
            await master.Ado.ExecuteCommandAsync($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await master.Ado.ExecuteCommandAsync($"DROP DATABASE [{database}]");
            master.Dispose();
        }

        private static T Inject<T>(ISqlSugarClient value)
        {
            var instance = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(T).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
            return instance;
        }

        private static SqlSugarClient Client(string connection) => new(new ConnectionConfig
        {
            ConnectionString = connection, DbType = DbType.SqlServer, IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
        });
    }
}
