using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesCostBackfillSqlServerFactAttribute : FactAttribute
{
    public SalesCostBackfillSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COST_BACKFILL_SQLSERVER_TEST_CONNECTION")))
            Skip = "未配置独立 SQL Server 测试实例";
    }
}

[Trait("Category", "SQL")]
public sealed class SalesCostBackfillSqlServerTests
{
    [SalesCostBackfillSqlServerFact]
    public async Task 停用商品使用可信主表进价回填且不重新启用()
    {
        await using var f = await Fixture.CreateAsync(); await f.SeedAsync();
        var row = await f.RowAsync(); row.UnitCostSnapshot = null; row.CostSource = "Missing";
        await f.Db.Updateable(row).ExecuteCommandAsync();
        await f.Db.Insertable(new Product { ProductCode = "P1", LocalSupplierCode = "240", ProductName = "停用测试商品",
            IsActive = false, IsDeleted = false, PurchasePrice = 1.94m }).ExecuteCommandAsync();
        await f.Db.Insertable(new SalesOrder { OrderGuid = "ORDER-1", Status = 1, BranchCode = "1015",
            OrderTime = Fixture.Date.AddHours(12), LastUploadTime = Fixture.Date.AddHours(12) }).ExecuteCommandAsync();
        await f.Db.Insertable(new SalesOrderDetail { OrderDetailGuid = "DETAIL-1", OrderGuid = "ORDER-1",
            ProductCode = "P1", SupplierCode = "240", Barcode = "72750", Quantity = 2,
            Price = 4.99m, Subtotal = 9.98m, ActualAmount = 9.98m, LastUploadTime = Fixture.Date.AddHours(12) }).ExecuteCommandAsync();
        await f.Db.Insertable(new PaymentDetail { PaymentGuid = "PAYMENT-1", OrderGuid = "ORDER-1",
            Amount = 9.98m, PaymentMethod = 1, LastUploadTime = Fixture.Date.AddHours(12) }).ExecuteCommandAsync();
        await f.RepublishAsync(); var batch = await f.PreviewAsync();
        var evidence = await f.Db.Queryable<SalesCostBackfillItem>().Where(x => x.BatchId == batch).ToListAsync();
        Assert.True(evidence.Count == 1 && evidence[0].Status == "Candidate",
            System.Text.Json.JsonSerializer.Serialize(evidence.Select(x => new { x.Status, x.Reason, x.ConflictReason })));
        await f.Service.RequestAsync(batch, false, "执行人"); await f.DrainAsync();
        Assert.Equal(3.88m, (await f.RowAsync()).TotalCost);
        Assert.False((await f.Db.Queryable<Product>().SingleAsync()).IsActive);
        await f.AssertPublishedAsync(3.88m);
    }

    [SalesCostBackfillSqlServerFact]
    public async Task 未完成日期恢复后可重试预览但已冻结日期不可重写()
    {
        await using var f = await Fixture.CreateAsync(); var before = await f.SeedAsync();
        await f.Db.Updateable<SalesStatisticRefreshState>().SetColumns(x => x.Status == "Failed")
            .Where(x => x.StatisticType == SalesStatisticType.ProductStoreDaily).ExecuteCommandAsync();
        var batch = await f.PreviewAsync();
        Assert.Equal("Deferred", (await f.Db.Queryable<SalesCostBackfillDay>().SingleAsync()).Status);
        Assert.Empty(await f.Db.Queryable<SalesCostBackfillItem>().ToListAsync());
        await f.RepublishAsync();
        Assert.True(await f.Service.RetryPreviewAsync(batch, "重试预览人")); await f.DrainAsync();
        var frozen = await f.Db.Queryable<SalesCostBackfillItem>().SingleAsync();
        Assert.Equal("Candidate", frozen.Status);
        Assert.Equal("重试预览人", (await f.Db.Queryable<SalesCostBackfillBatch>().InSingleAsync(batch)).PreviewRetriedBy);
        Assert.False(await f.Service.RetryPreviewAsync(batch, "重复预览人"));
        Assert.Equal(frozen.BeforeJson, (await f.Db.Queryable<SalesCostBackfillItem>().SingleAsync()).BeforeJson);
        Assert.Equal(SalesCostBackfillRules.Image(before), SalesCostBackfillRules.Image(await f.RowAsync()));
        Assert.True(await f.Service.RequestAsync(batch, false, "执行人")); await f.DrainAsync();
        Assert.Equal(3.88m, (await f.RowAsync()).TotalCost); await f.AssertPublishedAsync(3.88m);
    }

    [SalesCostBackfillSqlServerFact]
    public async Task 写入中途失败整天回滚且原冻结批次可重试()
    {
        await using var f = await Fixture.CreateAsync(); var before = await f.SeedAsync();
        var batch = await f.PreviewAsync();
        // 在商品已更新、供应商即将更新的事务中注入失败，验证真实数据库原子性。
        await f.Db.Ado.ExecuteCommandAsync("CREATE TRIGGER dbo.TestCostPublishFailure ON dbo.AustralianSupplierStoreSalesDetail AFTER UPDATE AS THROW 51091, 'isolated test failure', 1;");
        await f.Service.RequestAsync(batch, false, "执行人"); await f.DrainAsync();
        Assert.Equal(SalesCostBackfillRules.Image(before), SalesCostBackfillRules.Image(await f.RowAsync()));
        Assert.Equal("Candidate", (await f.Db.Queryable<SalesCostBackfillItem>().SingleAsync()).Status);
        var day = await f.Db.Queryable<SalesCostBackfillDay>().SingleAsync();
        Assert.Equal("Failed", day.Status); Assert.Equal("Applying", day.FailedOperation);
        await f.AssertPublishedAsync(null);
        await f.Db.Ado.ExecuteCommandAsync("DROP TRIGGER dbo.TestCostPublishFailure");
        Assert.True(await f.Service.RequestAsync(batch, false, "重试人")); await f.DrainAsync();
        Assert.Equal(3.88m, (await f.RowAsync()).TotalCost);
        await f.AssertPublishedAsync(3.88m);
    }

    [SalesCostBackfillSqlServerFact]
    public async Task 无候选日期幂等完成且成本事务锁跨连接互斥()
    {
        await using var f = await Fixture.CreateAsync(); await f.SeedAsync();
        var row = await f.RowAsync(); row.TotalCost = 3.88m; row.GrossProfit = 6.1m; row.GrossMarginRate = 6.1m / 9.98m;
        await f.Db.Updateable(row).ExecuteCommandAsync(); await f.RepublishAsync();
        var batch = await f.PreviewAsync(); await f.Service.RequestAsync(batch, false, "执行人"); await f.DrainAsync();
        Assert.Equal("Applied", (await f.Db.Queryable<SalesCostBackfillBatch>().InSingleAsync(batch)).Status);
        Assert.Empty(await f.Db.Queryable<SalesCostBackfillItem>().ToListAsync());
        using var second = new SqlSugarClient(new ConnectionConfig { ConnectionString = f.Db.CurrentConnectionConfig.ConnectionString,
            DbType = DbType.SqlServer, IsAutoCloseConnection = true });
        await f.Db.Ado.BeginTranAsync(); await second.Ado.BeginTranAsync();
        try
        {
            await SalesStatisticsCostWriteLock.AcquireAsync(f.Db, Fixture.Date);
            var result = await second.Ado.SqlQuerySingleAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'HB:SalesStatistics:CostWrite:date:2026-09-06', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; SELECT @r;");
            Assert.True(result < 0);
            await BlazorApp.Api.Services.React.SetChildPurchasePriceMutationLock.AcquireProductsAsync(f.Db, ["P1"]);
            result = await second.Ado.SqlQuerySingleAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'HB:SetChildPurchasePrice:Gate', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; SELECT @r;");
            Assert.True(result < 0);
        }
        finally { await second.Ado.RollbackTranAsync(); await f.Db.Ado.RollbackTranAsync(); }
    }

    [SalesCostBackfillSqlServerFact]
    public async Task 回填重复执行回滚均保持事实并原子发布版本()
    {
        await using var f = await Fixture.CreateAsync();
        var before = await f.SeedAsync();
        var batch = await f.PreviewAsync();
        Assert.True(await f.Service.RequestAsync(batch, false, "执行人"));
        await f.DrainAsync();
        var after = await f.RowAsync();
        Assert.Equal(3.88m, after.TotalCost);
        Assert.Equal(6.10m, after.GrossProfit);
        Assert.Equal(SalesCostBackfillRules.Image(before) with { Cost = SalesCostBackfillRules.Cost(after), UpdateTime = after.UpdateTime }, SalesCostBackfillRules.Image(after));
        await f.AssertPublishedAsync(3.88m);
        Assert.True(await f.Service.RequestAsync(batch, false, "重复执行人"));
        await f.DrainAsync();
        Assert.Equal(SalesCostBackfillRules.Json(after), SalesCostBackfillRules.Json(await f.RowAsync()));
        Assert.True(await f.Service.RequestAsync(batch, true, "回滚人"));
        await f.DrainAsync();
        Assert.Equal(SalesCostBackfillRules.Cost(before), SalesCostBackfillRules.Cost(await f.RowAsync()));
        await f.AssertPublishedAsync(null);
        var audit = await f.Db.Queryable<SalesCostBackfillBatch>().InSingleAsync(batch);
        Assert.Equal("预览人", audit.RequestedBy);
        Assert.Equal("执行人", audit.AppliedBy);
        Assert.Equal("回滚人", audit.RolledBackBy);
        Assert.Equal("RolledBack", audit.Status);
    }

    [SalesCostBackfillSqlServerFact]
    public async Task 供应商对账失败回滚商品审计及版本()
    {
        await using var f = await Fixture.CreateAsync();
        var before = await f.SeedAsync();
        var batch = await f.PreviewAsync();
        await f.Db.Updateable<AustralianSupplierStoreSalesDetail>()
            .SetColumns(x => x.TotalAmount == 99m).Where(x => x.Date == Fixture.Date).ExecuteCommandAsync();
        Assert.True(await f.Service.RequestAsync(batch, false, "执行人"));
        await f.DrainAsync();
        Assert.Equal(SalesCostBackfillRules.Json(before), SalesCostBackfillRules.Json(await f.RowAsync()));
        Assert.Equal("Candidate", (await f.Db.Queryable<SalesCostBackfillItem>().SingleAsync()).Status);
        Assert.Equal("Conflict", (await f.Db.Queryable<SalesCostBackfillDay>().SingleAsync()).Status);
        Assert.All(await f.Db.Queryable<SalesStatisticRefreshState>().ToListAsync(),
            state => Assert.Equal(SupplierStatisticVersion.ComputeProductVersion([before]), state.SourceProductVersion));
    }

    [SalesCostBackfillSqlServerFact]
    public async Task 回滚不覆盖后续人工修改()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(); var batch = await f.PreviewAsync();
        await f.Service.RequestAsync(batch, false, "执行人"); await f.DrainAsync();
        var row = await f.RowAsync(); row.TotalCost = 4m; row.GrossProfit = 5.98m;
        row.UpdateTime = row.UpdateTime.AddMinutes(1);
        await f.Db.Updateable(row).ExecuteCommandAsync();
        await f.RepublishAsync();
        await f.Service.RequestAsync(batch, true, "回滚人"); await f.DrainAsync();
        Assert.Equal(SalesCostBackfillRules.Image(row), SalesCostBackfillRules.Image(await f.RowAsync()));
        Assert.Equal("RollbackConflict", (await f.Db.Queryable<SalesCostBackfillDay>().SingleAsync()).Status);
        await f.AssertPublishedAsync(4m);
    }

    [SalesCostBackfillSqlServerFact]
    public async Task 刷新日期租约互斥且预览后修改来源拒绝执行()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var batch = await f.Service.PreviewAsync(Fixture.Date, Fixture.Date, "预览人");
        var lease = await f.Leases.TryAcquireAsync(SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
            Fixture.Date.ToString("yyyy-MM-dd"), TimeSpan.FromMinutes(1));
        Assert.True(lease.Acquired);
        Assert.False(await f.Service.RunOneAsync(CancellationToken.None));
        await f.Leases.CompleteAsync(SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType,
            Fixture.Date.ToString("yyyy-MM-dd"), lease.Lease!.LeaseToken!, true);
        await f.DrainAsync();
        var row = await f.RowAsync(); row.TotalQuantity++;
        await f.Db.Updateable(row).ExecuteCommandAsync(); await f.RepublishAsync();
        await f.Service.RequestAsync(batch, false, "执行人"); await f.DrainAsync();
        Assert.Null((await f.RowAsync()).TotalCost);
        Assert.Equal("Conflict", (await f.Db.Queryable<SalesCostBackfillDay>().SingleAsync()).Status);
    }

    [SalesCostBackfillSqlServerFact]
    public async Task 未完成事实仅列待处理不改变状态()
    {
        await using var f = await Fixture.CreateAsync(); await f.SeedAsync();
        await f.Db.Updateable<SalesStatisticRefreshState>().SetColumns(x => x.Status == "Failed")
            .Where(x => x.StatisticType == SalesStatisticType.ProductStoreDaily).ExecuteCommandAsync();
        await f.PreviewAsync();
        Assert.Equal("Deferred", (await f.Db.Queryable<SalesCostBackfillDay>().SingleAsync()).Status);
        Assert.Null((await f.RowAsync()).TotalCost);
        Assert.Equal("Failed", (await f.Db.Queryable<SalesStatisticRefreshState>()
            .Where(x => x.StatisticType == SalesStatisticType.ProductStoreDaily).SingleAsync()).Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal static readonly DateTime Date = new(2026, 9, 6);
        internal readonly SqlSugarClient Db;
        private readonly SqlSugarClient posm;
        private readonly SqlSugarScope history;
        private readonly SqlSugarClient master;
        private readonly string database;
        private readonly ILoggerFactory logs = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        private readonly SqlSugarContext context;
        private readonly POSMSqlSugarContext posmContext;
        internal readonly SalesCostBackfillService Service;
        internal readonly ScheduledTaskLeaseService Leases;

        private Fixture(string connection, string database)
        {
            this.database = database;
            var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
            master = Client(builder.ConnectionString);
            builder.InitialCatalog = database;
            Db = Client(builder.ConnectionString); posm = Client(builder.ConnectionString);
            history = new SqlSugarScope(Config(builder.ConnectionString));
            context = Inject<SqlSugarContext>(Db); posmContext = Inject<POSMSqlSugarContext>(posm);
            Leases = new(context, Options.Create(new ScheduledTaskOptions()), NullLogger<ScheduledTaskLeaseService>.Instance);
            Service = new(context, posmContext, new HBSalesRecordSqlSugarContext(history), Leases,
                logs.CreateLogger<SalesCostBackfillService>());
        }
        internal static async Task<Fixture> CreateAsync()
        {
            var connection = Environment.GetEnvironmentVariable("COST_BACKFILL_SQLSERVER_TEST_CONNECTION")!;
            var builder = new SqlConnectionStringBuilder(connection);
            // 测试会建表及销毁数据库，只允许此任务专用的本机容器端口。
            Assert.Equal("127.0.0.1,11439", builder.DataSource);
            var f = new Fixture(connection, "HBcost_test_" + Guid.NewGuid().ToString("N"));
            await f.master.Ado.ExecuteCommandAsync($"CREATE DATABASE [{f.database}]");
            f.Db.CodeFirst.InitTables(typeof(Product), typeof(WarehouseProduct), typeof(StoreRetailPrice),
                typeof(HBLocalSupplier), typeof(ChinaSupplier), typeof(Store), typeof(ProductStoreDailySalesStatistic),
                typeof(AustralianSupplierStoreSalesDetail), typeof(ChinaSupplierStoreSalesDetail),
                typeof(SalesStatisticRefreshState), typeof(ScheduledTaskLease), typeof(ProductSetCode), typeof(StoreMultiCodeProduct),
                typeof(SalesCostBackfillBatch), typeof(SalesCostBackfillDay), typeof(SalesCostBackfillItem));
            f.posm.CodeFirst.InitTables(typeof(SalesOrder), typeof(SalesOrderDetail), typeof(SalesReturnRecord),
                typeof(PaymentDetail), typeof(PosmProductSupplierMapping), typeof(POSM_设备注册信息表));
            return f;
        }
        internal async Task<ProductStoreDailySalesStatistic> SeedAsync()
        {
            var row = SalesCostBackfillTests.Row(); row.UnitCostSnapshot = 1.94m; row.CostSource = "StoreRetailPrice";
            await Db.Insertable(row).ExecuteCommandAsync();
            await RepublishAsync(); return await RowAsync();
        }
        internal async Task RepublishAsync()
        {
            var rows = await Db.Queryable<ProductStoreDailySalesStatistic>().ToListAsync();
            var build = await new SalesStatisticsSupplierStoreSummaryService().BuildFromProductStatisticsAsync(context, posmContext, rows, DateTime.Now);
            await Db.Deleteable<AustralianSupplierStoreSalesDetail>().Where(x => x.Date == Date).ExecuteCommandAsync();
            if (build.Australian.Count > 0) await Db.Insertable(build.Australian).ExecuteCommandAsync();
            foreach (var type in new[] { SalesStatisticType.ProductStoreDaily, SalesStatisticType.AustralianSupplierStoreSales, SalesStatisticType.ChinaSupplierStoreSales })
            {
                var state = new SalesStatisticRefreshState { Date = Date, StatisticType = type, Status = "Fresh",
                    CompletedAtUtc = DateTime.UtcNow, LastAggregatedAtUtc = DateTime.UtcNow,
                    SourceProductVersion = SupplierStatisticVersion.ComputeProductVersion(rows), LastSourceUploadTime = Date.AddHours(12) };
                await Db.Storageable(state).ExecuteCommandAsync();
            }
        }
        internal async Task<Guid> PreviewAsync()
        {
            var id = await Service.PreviewAsync(Date, Date, "预览人"); await DrainAsync(); return id;
        }
        internal async Task DrainAsync()
        {
            for (var i = 0; i < 10; i++) if (!await Service.RunOneAsync(CancellationToken.None)) return;
            Assert.Fail("回填状态未在有限步骤内稳定");
        }
        internal Task<ProductStoreDailySalesStatistic> RowAsync() => Db.Queryable<ProductStoreDailySalesStatistic>().SingleAsync();
        internal async Task AssertPublishedAsync(decimal? cost)
        {
            var rows = await Db.Queryable<ProductStoreDailySalesStatistic>().ToListAsync();
            Assert.All(await Db.Queryable<SalesStatisticRefreshState>().ToListAsync(), state =>
                Assert.Equal(SupplierStatisticVersion.ComputeProductVersion(rows), state.SourceProductVersion));
            var supplier = await Db.Queryable<AustralianSupplierStoreSalesDetail>().SingleAsync();
            Assert.Equal(cost, supplier.TotalCost);
            Assert.Equal(cost.HasValue ? 1 : 0, supplier.CostedRowCount);
        }
        public async ValueTask DisposeAsync()
        {
            Db.Dispose(); posm.Dispose(); history.Dispose(); logs.Dispose(); SqlConnection.ClearAllPools();
            await master.Ado.ExecuteCommandAsync($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await master.Ado.ExecuteCommandAsync($"DROP DATABASE [{database}]"); master.Dispose();
        }
        private static T Inject<T>(ISqlSugarClient db)
        {
            var value = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(T).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, db); return value;
        }
        private static ConnectionConfig Config(string connection) => new()
        { ConnectionString = connection, DbType = DbType.SqlServer, IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true } };
        private static SqlSugarClient Client(string connection) => new(Config(connection));
    }
}
