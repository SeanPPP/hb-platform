using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PosmSalesOrderListSqlServerFactAttribute : FactAttribute
{
    public PosmSalesOrderListSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PosmSalesOrderListSqlServerIntegrationTests.ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {PosmSalesOrderListSqlServerIntegrationTests.ConnectionEnvironmentVariable}，跳过真实 SQL Server 收银记录查询验证。";
        }
    }
}

/// <summary>
/// 在真实 SQL Server 上执行收银记录列表批处理：库排序规则与生产 POSM 一致（Chinese_PRC_90_CI_AS），
/// 表结构按生产的 varchar / decimal(18,4) 建立，索引用仓库里的 POSMSqlSugarContext.CreateIndexes 创建。
/// 覆盖普通路径、商品侧与逐单两条关键词路径、订单号大小写、件数的范围扫描与按单补齐。
/// </summary>
public sealed class PosmSalesOrderListSqlServerIntegrationTests
{
    public const string ConnectionEnvironmentVariable = "HB_TEST_SQLSERVER_CONNECTION";

    private const string O1 = "019FCEED-0001-7000-8000-000000000001";
    private const string O2 = "019FCEED-0002-7000-8000-000000000002";
    private const string O3 = "019FCEED-0003-7000-8000-000000000003";
    private const string O4 = "019FCEED-0004-7000-8000-000000000004";
    // 旧客户端写入的随机小写 GUID：订单号大小写匹配与件数按单补齐都要覆盖它。
    private const string O5 = "e7c1b4d2-1111-4222-8333-aaaaaaaaaaaa";
    private const string O6 = "019FCEED-0006-7000-8000-000000000006";
    private const string History = "019F0000-0000-7000-8000-000000000000";

    [PosmSalesOrderListSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 普通路径按状态汇总且当前页补齐种数件数支付与分店()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService();

        var all = await service.GetSalesOrderListAsync(TwoDays());
        Assert.Equal(new[] { O5, O4, O3, O2, O1 }, all.Items.Select(item => item.OrderGuid));
        Assert.Equal(5, all.Total);
        var paid = all.Summary.Single(row => row.Status == 1);
        Assert.Equal(3, paid.OrderCount);
        Assert.Equal(23m, paid.TotalAmount);
        Assert.Equal(1m, paid.DiscountAmount);
        Assert.Equal(-5m, all.Summary.Single(row => row.Status == 3).TotalAmount);
        var first = all.Items.Single(item => item.OrderGuid == O1);
        Assert.Equal(2, first.SkuCount);
        Assert.Equal(3, first.QuantityTotal);
        Assert.Equal(new[] { 2 }, first.PaymentMethods);
        Assert.Equal("Charlestown Square", first.BranchName);

        var refunds = TwoDays();
        refunds.OrderType = OrderType.Refunded;
        var refundResult = await service.GetSalesOrderListAsync(refunds);
        Assert.Equal(O3, Assert.Single(refundResult.Items).OrderGuid);
        Assert.Equal(1, refundResult.Total);
        Assert.Equal(3, refundResult.Summary.Single(row => row.Status == 1).OrderCount);
    }

    [PosmSalesOrderListSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 商品关键词经主档解析后命中订单并带回命中商品()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService();

        var query = TwoDays();
        query.Keyword = "squishy";
        var result = await service.GetSalesOrderListAsync(query);
        Assert.Equal(new[] { O3, O1 }, result.Items.Select(item => item.OrderGuid));
        var hit = Assert.Single(result.Items.Single(item => item.OrderGuid == O1).MatchedProducts!);
        Assert.Equal("P-SQUISHY", hit.ProductCode);
        Assert.Equal("HB022-249", hit.ItemNumber);
        Assert.Equal(2, hit.Quantity);
        // 商品名、条码按命中明细主键单独读取（种数件数只读订单号索引覆盖列）。
        Assert.Equal("P-SQUISHY", hit.ProductName);
        Assert.Equal("P-SQUISHY", hit.Barcode);

        // 同一关键词在 1 天范围内：历史明细（62 行）远多于范围内订单数（1 单），走逐单路径，结果口径一致。
        var cardDay = new PosmSalesOrderQueryParams
        {
            StartDate = new DateTime(2026, 7, 3), EndDate = new DateTime(2026, 7, 3),
            Keyword = "BINE1630", SortField = "orderTime", SortDirection = "desc",
        };
        Assert.Equal(O6, Assert.Single((await service.GetSalesOrderListAsync(cardDay)).Items).OrderGuid);
        var cardTwoDays = TwoDays();
        cardTwoDays.Keyword = "birthday";
        Assert.Equal(new[] { O2, O1 }, (await service.GetSalesOrderListAsync(cardTwoDays)).Items.Select(item => item.OrderGuid));
    }

    [PosmSalesOrderListSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 订单号片段按大小写两种写法匹配()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService();

        var lower = TwoDays();
        lower.Keyword = "C1B4D2";
        Assert.Equal(O5, Assert.Single((await service.GetSalesOrderListAsync(lower)).Items).OrderGuid);

        var upper = TwoDays();
        upper.Keyword = "000000000003";
        Assert.Equal(O3, Assert.Single((await service.GetSalesOrderListAsync(upper)).Items).OrderGuid);
    }

    [PosmSalesOrderListSqlServerFact]
    [Trait("Category", "SQL")]
    public async Task 件数条件按订单号范围汇总并为旧GUID订单按单补齐()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService();

        var query = TwoDays();
        query.QuantityMin = 5;
        query.SortField = "quantity";
        query.SortDirection = "desc";
        var result = await service.GetSalesOrderListAsync(query);

        Assert.Equal(new[] { O4, O5 }, result.Items.Select(item => item.OrderGuid));
        Assert.Equal(new int?[] { 6, 5 }, result.Items.Select(item => item.QuantityTotal));
        Assert.Equal(2, result.Total);
    }

    private static PosmSalesOrderQueryParams TwoDays() =>
        new()
        {
            StartDate = new DateTime(2026, 7, 1),
            EndDate = new DateTime(2026, 7, 2),
            SortField = "orderTime",
            SortDirection = "desc",
            PageSize = 50,
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly SqlSugarClient _db;

        private Fixture(string masterConnectionString, string databaseName, string databaseConnectionString)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            _db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = databaseConnectionString,
                DbType = SqlSugar.DbType.SqlServer,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
                MoreSettings = new ConnMoreSettings { IsWithNoLockQuery = true },
            });
        }

        public static async Task<Fixture> CreateAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)!;
            EnsureLoopbackSqlServer(baseConnectionString);
            var databaseName = $"HbPosmList_{Guid.NewGuid():N}";
            var master = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" }.ConnectionString;
            var database = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = databaseName }.ConnectionString;
            // 与生产 POSM 相同的库排序规则；tempdb 通常不同，正好覆盖临时表排序规则冲突。
            await ExecuteAsync(master, $"CREATE DATABASE [{databaseName}] COLLATE Chinese_PRC_90_CI_AS;");
            var fixture = new Fixture(master, databaseName, database);
            try
            {
                await ExecuteAsync(database, SchemaSql);
                fixture._db.CodeFirst.InitTables(typeof(Product), typeof(Store));
                var posm = CreateContext<POSMSqlSugarContext>(fixture._db);
                posm.CreateIndexes();
                var indexCount = await fixture._db.Ado.GetIntAsync(
                    "SELECT COUNT(*) FROM sys.indexes WHERE name IN ('IX_sales_order_detail_ProductCode', 'IX_sales_order_OrderTime', 'IX_sales_order_detail_OrderGuid')");
                Assert.Equal(3, indexCount);
                await fixture.SeedAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public PosmSalesOrderReactService CreateService() =>
            new(
                CreateContext<POSMSqlSugarContext>(_db),
                CreateContext<SqlSugarContext>(_db),
                new MapperConfiguration(_ => { }, NullLoggerFactory.Instance).CreateMapper(),
                NullLogger<PosmSalesOrderReactService>.Instance
            );

        private async Task SeedAsync()
        {
            await _db.Insertable(new List<Store>
            {
                new() { StoreGUID = Guid.NewGuid().ToString(), StoreCode = "1005", StoreName = "Charlestown Square" },
                new() { StoreGUID = Guid.NewGuid().ToString(), StoreCode = "1033", StoreName = "Top Ryde" },
            }).ExecuteCommandAsync();
            await _db.Insertable(new List<Product>
            {
                new() { ProductCode = "P-SQUISHY", ItemNumber = "HB022-249", ProductName = "Mango Crunchy Squishy" },
                new() { ProductCode = "P-CARD", ItemNumber = "BINE1630", ProductName = "Birthday Card" },
                new() { ProductCode = "P-OTHER", ItemNumber = "OTHER-01", ProductName = "Other" },
            }).ExecuteCommandAsync();

            await Order(O1, "2026-07-01 10:00", "1005", 10m, 1m, 1);
            await Detail(O1, "P-SQUISHY", 2);
            await Detail(O1, "P-CARD", 1);
            await Payment(O1, 2);
            await Order(O2, "2026-07-01 11:00", "1033", 5m, 0m, 1);
            await Detail(O2, "P-CARD", 3);
            await Payment(O2, 1);
            await Order(O3, "2026-07-02 09:00", "1005", -5m, 0m, 3);
            await Detail(O3, "P-SQUISHY", -1);
            await Order(O4, "2026-07-02 12:00", "1005", 30m, 3m, 2);
            await Detail(O4, "P-OTHER", 6);
            await Order(O5, "2026-07-02 13:00", "1033", 8m, 0m, 1);
            await Detail(O5, "P-OTHER", 5);
            await Order(O6, "2026-07-03 10:00", "1005", 2m, 0m, 1);
            await Detail(O6, "P-CARD", 1);
            // 历史订单里大量卖过贺卡，让 1 天范围的关键词查询走逐单路径。
            await Order(History, "2026-01-01 10:00", "1005", 60m, 0m, 1);
            for (var index = 0; index < 60; index++)
            {
                await Detail(History, "P-CARD", 1);
            }
        }

        private Task Order(string guid, string time, string branch, decimal total, decimal discount, int status) =>
            _db.Ado.ExecuteCommandAsync(
                "INSERT INTO sales_order (OrderGuid, OrderTime, BranchCode, DeviceCode, TotalAmount, DiscountAmount, ActualAmount, ItemCount, Status) VALUES (@g, @t, @b, @d, @total, @discount, @actual, 1, @s)",
                new { g = guid, t = DateTime.Parse(time), b = branch, d = "POS_" + branch + "_1", total, discount, actual = total - discount, s = status });

        private Task Detail(string orderGuid, string productCode, int quantity) =>
            _db.Ado.ExecuteCommandAsync(
                "INSERT INTO sales_order_detail (OrderDetailGuid, OrderGuid, ProductCode, ProductName, Barcode, Quantity) VALUES (@id, @o, @p, @p, @p, @q)",
                new { id = Guid.NewGuid().ToString("D").ToUpperInvariant(), o = orderGuid, p = productCode, q = quantity });

        private Task Payment(string orderGuid, int method) =>
            _db.Ado.ExecuteCommandAsync(
                "INSERT INTO payment_detail (PaymentGuid, OrderGuid, PaymentMethod, Amount) VALUES (@id, @o, @m, 1)",
                new { id = Guid.NewGuid().ToString("D"), o = orderGuid, m = method });

        public async ValueTask DisposeAsync()
        {
            _db.Dispose();
            SqlConnection.ClearAllPools();
            await ExecuteAsync(_masterConnectionString, $"""
                IF DB_ID(N'{_databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_databaseName}];
                END;
                """);
        }

        private const string SchemaSql = """
            CREATE TABLE dbo.sales_order (
                OrderGuid varchar(255) NOT NULL PRIMARY KEY,
                OrderTime datetime NOT NULL,
                BranchCode varchar(20) NULL,
                DeviceCode varchar(20) NULL,
                TotalAmount decimal(18, 4) NULL,
                DiscountAmount decimal(18, 4) NULL,
                ActualAmount decimal(18, 4) NULL,
                ItemCount int NULL,
                Status int NULL
            );
            CREATE TABLE dbo.sales_order_detail (
                OrderDetailGuid varchar(50) NOT NULL PRIMARY KEY,
                OrderGuid varchar(50) NULL,
                ProductCode varchar(50) NULL,
                ProductName varchar(255) NULL,
                Barcode varchar(50) NULL,
                Quantity int NULL
            );
            CREATE TABLE dbo.payment_detail (
                PaymentGuid varchar(50) NOT NULL PRIMARY KEY,
                OrderGuid varchar(255) NULL,
                PaymentMethod int NOT NULL,
                Amount decimal(18, 4) NULL
            );
            """;

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }

        private static void EnsureLoopbackSqlServer(string connectionString)
        {
            var dataSource = new SqlConnectionStringBuilder(connectionString).DataSource.Trim();
            if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) dataSource = dataSource[4..];
            var host = dataSource.Split(',', 2, StringSplitOptions.TrimEntries)[0].Trim('[', ']');
            if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{ConnectionEnvironmentVariable} 必须指向 localhost 或 127.0.0.1。");
        }
    }

    private static T CreateContext<T>(ISqlSugarClient db)
    {
        var context = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        typeof(T).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }
}
