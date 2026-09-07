using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// 本地成本事务使用独立 SQL Server 数据库，HQ 使用独立 SQLite 文件。
/// 验证真实 applock 和业务入口的组合，不以 SQLite 的无锁分支代替并发验收。
/// </summary>
public sealed class LocalSupplierInvoiceHqConcurrencySqlServerTests
{
    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 已有六个单品二十八店_无关商品持锁时连续更新成功且字段准确()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = await fixture.SeedAsync("invoice-single", 6, 28);
        using var blocker = fixture.OpenLocal();
        await blocker.Ado.BeginTranAsync();
        await SetChildPurchasePriceMutationLock.AcquireProductsAsync(blocker, ["UNRELATED"]);
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var scope = fixture.OpenService();
                var response = await scope.Service.UpdateHqProductsAsync("invoice-single", request, null, "tester", 500);
                Assert.True(response.Success, response.Message);
                Assert.Equal(168, response.Data!.HqPurchasePricesUpdated);
                Assert.Equal(0, response.Data.Failed);
                Assert.Equal(0, response.Data.HbwebCreated);
            }
            Assert.Equal(6, await fixture.Local.Queryable<Product>().CountAsync());
            Assert.Equal(168, await fixture.Hq.Queryable<DIC_商品零售价表>().CountAsync());
            var prices = await fixture.Hq.Queryable<DIC_商品零售价表>().ToListAsync();
            Assert.All(prices, price => Assert.Equal(7m, price.H进货价));
            Assert.All(prices, price => Assert.Equal(11m, price.H分店零售价));
            Assert.All(prices, price => Assert.True(price.H是否自动定价));
            var details = await fixture.Local.Queryable<StoreLocalSupplierInvoiceDetails>().ToListAsync();
            Assert.All(details, detail => Assert.Equal(5m, detail.LastPurchasePrice));
            Assert.All(await fixture.Local.Queryable<Product>().ToListAsync(), product => Assert.Equal(5m, product.PurchasePrice));
        }
        finally { await blocker.Ado.RollbackTranAsync(); }
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 同商品锁超时_确认回滚且HQ零写入_释放后重试成功()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = await fixture.SeedAsync("invoice-busy", 1, 1);
        using var blocker = fixture.OpenLocal();
        await blocker.Ado.BeginTranAsync();
        await SetChildPurchasePriceMutationLock.AcquireProductsAsync(blocker, ["P0"]);
        using (var scope = fixture.OpenService())
        {
            var failed = await scope.Service.UpdateHqProductsAsync("invoice-busy", request, null, "tester", 100);
            Assert.False(failed.Success);
            Assert.Equal("HQ_UPDATE_COST_LOCK_BUSY", failed.Code);
            Assert.Null(scope.Local.Ado.Transaction);
            var failedResult = Assert.IsType<UpdateHqProductsResult>(failed.Details);
            Assert.Equal(1, failedResult.Total);
            Assert.Equal(0, failedResult.HqCreated);
            Assert.Equal(0, failedResult.HqPurchasePricesUpdated);
            Assert.Equal(0, await fixture.Hq.Queryable<DIC_商品信息字典表>().CountAsync());
        }
        await blocker.Ado.RollbackTranAsync();
        using var retryScope = fixture.OpenService();
        var succeeded = await retryScope.Service.UpdateHqProductsAsync("invoice-busy", request, null, "tester", 100);
        Assert.True(succeeded.Success, succeeded.Message);
        Assert.Equal(1, succeeded.Data!.HqPurchasePricesUpdated);
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 真实锁首次超时后_同一后台任务自动重试且重复提交复用任务()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = await fixture.SeedAsync("invoice-job", 1, 1);
        using var blocker = fixture.OpenLocal();
        await blocker.Ado.BeginTranAsync();
        await SetChildPurchasePriceMutationLock.AcquireProductsAsync(blocker, ["P0"]);
        var secondAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            var scope = fixture.OpenService();
            if (Interlocked.Increment(ref attempts) == 2) secondAttempt.TrySetResult();
            return scope;
        });
        services.AddScoped<ILocalSupplierInvoiceHqProductSyncService>(provider => provider.GetRequiredService<ServiceScope>().Service);
        using var provider = services.BuildServiceProvider();
        var jobs = new LocalSupplierInvoiceBatchUpdateJobService(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LocalSupplierInvoiceBatchUpdateJobService>.Instance);
        var started = await jobs.StartUpdateHqProductsJobAsync("invoice-job", request, "tester");
        try
        {
            // 第一轮必须经过真实 SQL Server 的 10 秒锁等待并安全回滚，才会创建第二个 scope。
            await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var running = await jobs.GetUpdateHqProductsJobAsync(started.JobId);
            Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Running, running!.Status);
            var duplicate = await jobs.StartUpdateHqProductsJobAsync("invoice-job", request, "tester");
            Assert.Equal(started.JobId, duplicate.JobId);
            Assert.Equal(started.OperationId, duplicate.OperationId);
            Assert.True(duplicate.IsDuplicateRequest);
            Assert.Equal(0, await fixture.Hq.Queryable<DIC_商品信息字典表>().CountAsync());
        }
        finally { await blocker.Ado.RollbackTranAsync(); }

        LocalSupplierInvoiceUpdateHqProductsJobDto? completed = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            completed = await jobs.GetUpdateHqProductsJobAsync(started.JobId);
            if (completed!.Status != LocalSupplierInvoiceBatchUpdateJobStatusConstants.Running) break;
            await Task.Delay(25);
        }
        Assert.Equal(LocalSupplierInvoiceBatchUpdateJobStatusConstants.Succeeded, completed!.Status);
        Assert.Equal(2, attempts);
        Assert.Equal(1, completed.Result!.Total);
        Assert.Equal(1, completed.Result.HqCreated);
        Assert.Equal(1, completed.Result.HqPurchasePricesUpdated);
        Assert.Equal(0, completed.Result.Failed);
        Assert.Equal(7m, (await fixture.Hq.Queryable<DIC_商品零售价表>().SingleAsync()).H进货价);
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 获锁期间商品匹配变化_释放商品锁后回退全局并复用新身份()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = await fixture.SeedAsync("invoice-change", 1, 1);
        using var blocker = fixture.OpenLocal();
        await blocker.Ado.BeginTranAsync();
        await SetChildPurchasePriceMutationLock.AcquireProductsAsync(blocker, ["P0"]);
        var acquiringProduct = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scope = fixture.OpenService();
        scope.Local.Aop.OnLogExecuting = (sql, parameters) =>
        {
            if (sql.Contains("sp_getapplock", StringComparison.OrdinalIgnoreCase)
                && parameters.Any(parameter => Convert.ToString(parameter.Value) == "HB:SetChildPurchasePrice:Product:P0"))
                acquiringProduct.TrySetResult();
        };
        var update = scope.Service.UpdateHqProductsAsync("invoice-change", request, null, "tester", 5_000);
        await acquiringProduct.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // 原身份在其商品锁内删除，替代身份保持相同供应商/货号/条码。
        await blocker.Updateable<Product>().SetColumns(product => product.IsDeleted == true)
            .Where(product => product.ProductCode == "P0").ExecuteCommandAsync();
        await blocker.Insertable(Fixture.Product("P-REPLACEMENT", "ITEM0", "9300000000000")).ExecuteCommandAsync();
        await blocker.Ado.CommitTranAsync();
        var result = await update.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Data!.HbwebCreated);
        Assert.Equal("P-REPLACEMENT", (await fixture.Hq.Queryable<DIC_商品信息字典表>().SingleAsync()).H商品编码);
        Assert.Equal(1, await fixture.Local.Queryable<Product>().Where(product => !product.IsDeleted).CountAsync());
        Assert.Null(scope.Local.Ado.Transaction);
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 新商品等待全局锁后重新匹配_另一个请求先建好时不得重复创建()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = await fixture.SeedAsync("invoice-new", 0, 1);
        await fixture.Local.Insertable(Fixture.Detail("invoice-new", "D0", null, "ITEM0", "9300000000000")).ExecuteCommandAsync();
        request.DetailGuids = ["D0"];
        using var blocker = fixture.OpenLocal();
        await blocker.Ado.BeginTranAsync();
        await SetChildPurchasePriceMutationLock.AcquireAllAsync(blocker);
        using var scope = fixture.OpenService();
        var waitingGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Local.Aop.OnLogExecuting = (sql, parameters) =>
        {
            if (sql.Contains("sp_getapplock", StringComparison.OrdinalIgnoreCase)
                && parameters.Any(parameter => Convert.ToString(parameter.Value) == "Exclusive"))
                waitingGate.TrySetResult();
        };
        var update = scope.Service.UpdateHqProductsAsync("invoice-new", request, null, "tester", 5_000);
        await waitingGate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await blocker.Insertable(Fixture.Product("P-CREATED-FIRST", "ITEM0", "9300000000000")).ExecuteCommandAsync();
        await blocker.Ado.CommitTranAsync();
        var result = await update.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.Data!.HbwebCreated);
        Assert.Equal(1, await fixture.Local.Queryable<Product>().CountAsync());
        Assert.Equal(1, await fixture.Hq.Queryable<DIC_商品信息字典表>().CountAsync());
    }

    [SetChildPurchasePriceSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 锁取消_不得标成可重试锁冲突()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = await fixture.SeedAsync("invoice-cancel", 1, 1);
        using var scope = fixture.OpenService();
        scope.Local.Aop.OnLogExecuting = (sql, _) =>
        {
            if (sql.Contains("sp_getapplock", StringComparison.OrdinalIgnoreCase))
                throw new OperationCanceledException("cancelled by test");
        };
        var cancelled = await scope.Service.UpdateHqProductsAsync("invoice-cancel", request, null, "tester", 100);
        Assert.False(cancelled.Success);
        Assert.NotEqual("HQ_UPDATE_COST_LOCK_BUSY", cancelled.Code);
        Assert.Null(scope.Local.Ado.Transaction);
        Assert.Equal(0, await fixture.Hq.Queryable<DIC_商品信息字典表>().CountAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _adminConnection;
        private readonly string _databaseName = "HbInvoiceHqConcurrency_" + Guid.NewGuid().ToString("N");
        private readonly string _hqFile = Path.Combine(Path.GetTempPath(), "hb-invoice-hq-" + Guid.NewGuid().ToString("N") + ".db");
        private string _localConnection = string.Empty;
        internal SqlSugarClient Local { get; private set; } = null!;
        internal SqlSugarClient Hq { get; private set; } = null!;

        private Fixture(string adminConnection) => _adminConnection = adminConnection;

        internal static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(Environment.GetEnvironmentVariable("SET_CHILD_PURCHASE_PRICE_SQLSERVER_TEST_CONNECTION")!);
            using var admin = new SqlConnection(fixture._adminConnection);
            await admin.OpenAsync();
            using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{fixture._databaseName}]";
            await create.ExecuteNonQueryAsync();
            fixture._localConnection = new SqlConnectionStringBuilder(fixture._adminConnection) { InitialCatalog = fixture._databaseName }.ConnectionString;
            fixture.Local = fixture.OpenLocal();
            fixture.Hq = fixture.OpenHq();
            try
            {
                fixture.Local.CodeFirst.InitTables(typeof(Store), typeof(Product), typeof(WarehouseProduct), typeof(DomesticProduct),
                    typeof(StoreRetailPrice), typeof(ProductSetCode), typeof(StoreMultiCodeProduct),
                    typeof(StoreLocalSupplierInvoice), typeof(StoreLocalSupplierInvoiceDetails));
                fixture.Hq.CodeFirst.InitTables(typeof(DIC_商品信息字典表), typeof(DIC_商品零售价表), typeof(DIC_一品多码表), typeof(DIC_分店一品多码表));
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal SqlSugarClient OpenLocal() => new(new ConnectionConfig
        {
            ConnectionString = _localConnection, DbType = SqlSugar.DbType.SqlServer,
            IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
        });
        private SqlSugarClient OpenHq() => new(new ConnectionConfig
        {
            ConnectionString = $"Data Source={_hqFile}", DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false, InitKeyType = InitKeyType.Attribute,
        });
        internal ServiceScope OpenService() => new(OpenLocal(), OpenHq());

        internal async Task<UpdateHqProductsRequest> SeedAsync(string invoiceGuid, int productCount, int storeCount)
        {
            var storeCodes = Enumerable.Range(0, storeCount).Select(index => $"S{index:00}").ToList();
            await Local.Insertable(storeCodes.Select(code => WithDates(new Store
            {
                StoreGUID = "store-" + code, StoreCode = code, StoreName = code, IsActive = true, IsDeleted = false,
            })).ToList()).ExecuteCommandAsync();
            await Local.Insertable(WithDates(new StoreLocalSupplierInvoice
            {
                InvoiceGUID = invoiceGuid, StoreCode = storeCodes[0], SupplierCode = "SUP", InvoiceNo = invoiceGuid, IsDeleted = false,
            })).ExecuteCommandAsync();
            var detailGuids = new List<string>();
            for (var index = 0; index < productCount; index++)
            {
                var productCode = "P" + index;
                var barcode = (9300000000000L + index).ToString();
                await Local.Insertable(Product(productCode, "ITEM" + index, barcode)).ExecuteCommandAsync();
                var detailGuid = "D" + index;
                await Local.Insertable(Detail(invoiceGuid, detailGuid, productCode, "ITEM" + index, barcode)).ExecuteCommandAsync();
                detailGuids.Add(detailGuid);
            }
            return new UpdateHqProductsRequest
            {
                DetailGuids = detailGuids, TargetStoreCodes = storeCodes,
                UpdateFields = new UpdateToStorePricesFields { UpdatePurchasePrice = true },
            };
        }
        internal static Product Product(string code, string item, string barcode) => WithDates(new Product
        {
            UUID = code, ProductCode = code, LocalSupplierCode = "SUP", ItemNumber = item, Barcode = barcode,
            ProductName = item, ProductType = 0, PurchasePrice = 5m, RetailPrice = 11m,
            IsAutoPricing = true, IsActive = true, IsDeleted = false,
        });
        internal static StoreLocalSupplierInvoiceDetails Detail(string invoice, string guid, string? code, string item, string barcode) => WithDates(new StoreLocalSupplierInvoiceDetails
        {
            InvoiceGUID = invoice, DetailGUID = guid, StoreCode = "S00", SupplierCode = "SUP", ProductCode = code,
            ItemNumber = item, Barcode = barcode, ProductName = item, PurchasePrice = 7m, LastPurchasePrice = 5m,
            RetailPrice = 11m, Quantity = 12, AutoPricing = true, IsDeleted = false,
        });
        private static T WithDates<T>(T value)
        {
            foreach (var property in typeof(T).GetProperties().Where(property => property.PropertyType == typeof(DateTime) && property.CanWrite))
                if ((DateTime)property.GetValue(value)! == default) property.SetValue(value, DateTime.UtcNow);
            return value;
        }
        public async ValueTask DisposeAsync()
        {
            Local?.Dispose(); Hq?.Dispose();
            SqlConnection.ClearAllPools();
            using var admin = new SqlConnection(_adminConnection);
            await admin.OpenAsync();
            using var cleanup = admin.CreateCommand();
            // 名称仅由本 fixture 生成；仅清理此独立测试数据库。
            if (!System.Text.RegularExpressions.Regex.IsMatch(_databaseName, "^HbInvoiceHqConcurrency_[a-f0-9]{32}$"))
                throw new InvalidOperationException("Invalid test database name");
            cleanup.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]";
            await cleanup.ExecuteNonQueryAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(_hqFile);
        }
    }

    private sealed class ServiceScope : IDisposable
    {
        internal SqlSugarClient Local { get; }
        private SqlSugarClient Hq { get; }
        internal LocalSupplierInvoiceHqProductSyncService Service { get; }
        internal ServiceScope(SqlSugarClient local, SqlSugarClient hq)
        {
            Local = local; Hq = hq;
            Service = new LocalSupplierInvoiceHqProductSyncService(Context<SqlSugarContext>(local), Context<HqSqlSugarContext>(hq),
                NullLogger<LocalSupplierInvoiceHqProductSyncService>.Instance, WarehouseProductChangeHistoryTestDouble.CreateNoop());
        }
        private static T Context<T>(ISqlSugarClient db)
        {
            var context = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(T).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
            return context;
        }
        public void Dispose() { Local.Dispose(); Hq.Dispose(); }
    }
}
