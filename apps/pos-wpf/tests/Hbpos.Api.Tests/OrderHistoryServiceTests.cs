using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Shared.Models.POSM;
using Hbpos.Api.Data;
using Hbpos.Api.Services;
using Hbpos.Contracts.Orders;
using Microsoft.Data.Sqlite;
using SqlSugar;

namespace Hbpos.Api.Tests;

public sealed class OrderHistoryServiceTests
{
    private static readonly TimeSpan QueryBudget = TimeSpan.FromSeconds(2);

    [Fact]
    [Trait("Category", "Performance")]
    public async Task QueryAsync_matches_item_number_and_barcode_within_two_seconds_without_global_detail_materialization()
    {
        using var database = await OrderHistorySqliteFixture.CreateAsync(orderCount: 10_000);
        var repository = new SqlSugarOrderHistoryRepository(database.DbContext);

        await AssertFastLookupAsync(repository, database, "SKU-TARGET");
        await AssertFastLookupAsync(repository, database, "ITEM-TARGET");
        await AssertFastLookupAsync(repository, database, "930000000001");
    }

    [Fact]
    public async Task QueryAsync_does_not_partially_match_item_number_marker()
    {
        using var database = await OrderHistorySqliteFixture.CreateAsync(orderCount: 100);
        var repository = new SqlSugarOrderHistoryRepository(database.DbContext);

        var response = await repository.QueryAsync(
            new OrderHistoryQueryRequest(
                "S001",
                SoldFrom: DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
                SoldTo: DateTimeOffset.Parse("2026-08-25T23:59:59Z"),
                Keyword: "ITEM-TARGE",
                Take: 100),
            CancellationToken.None);

        Assert.Empty(response.Orders);
    }

    private static async Task AssertFastLookupAsync(
        SqlSugarOrderHistoryRepository repository,
        OrderHistorySqliteFixture database,
        string keyword)
    {
        var statements = database.CaptureSql();
        var stopwatch = Stopwatch.StartNew();

        var response = await repository.QueryAsync(
            new OrderHistoryQueryRequest(
                "S001",
                SoldFrom: DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
                SoldTo: DateTimeOffset.Parse("2026-08-25T23:59:59Z"),
                Keyword: keyword,
                Take: 100),
            CancellationToken.None);

        stopwatch.Stop();
        var order = Assert.Single(response.Orders);
        Assert.Equal(OrderHistorySqliteFixture.TargetOrderGuid, order.OrderGuid);
        Assert.True(
            stopwatch.Elapsed < QueryBudget,
            $"在线订单按 {keyword} 查询耗时 {stopwatch.Elapsed.TotalMilliseconds:F1} ms，超过 2 秒预算。");

        var selects = statements
            .Where(statement => statement.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(2, selects.Count);
        Assert.Contains("EXISTS", selects[0], StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OrderHistorySqliteFixture : IDisposable
    {
        private readonly string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"hbpos-order-history-{Guid.NewGuid():N}.db");
        private readonly SqlSugarClient client;

        private OrderHistorySqliteFixture()
        {
            client = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = $"Data Source={databasePath}",
                DbType = DbType.Sqlite,
                InitKeyType = InitKeyType.Attribute,
                IsAutoCloseConnection = true
            });
            client.CodeFirst.InitTables<SalesOrder, SalesOrderDetail, PaymentDetail>();
            DbContext = CreateDbContext(client);
        }

        public static Guid TargetOrderGuid { get; } = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        public HbposSqlSugarContext DbContext { get; }

        public static async Task<OrderHistorySqliteFixture> CreateAsync(int orderCount)
        {
            var fixture = new OrderHistorySqliteFixture();
            await fixture.SeedAsync(orderCount);
            return fixture;
        }

        public List<string> CaptureSql()
        {
            var statements = new List<string>();
            client.Aop.OnLogExecuting = (sql, _) => statements.Add(sql);
            return statements;
        }

        public void Dispose()
        {
            client.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                try
                {
                    File.Delete(databasePath);
                }
                catch (IOException)
                {
                    // SQLite 可能短暂占用测试数据库文件，不影响查询断言。
                }
            }
        }

        private async Task SeedAsync(int orderCount)
        {
            var orderTime = DateTime.Parse("2026-08-25T10:00:00Z").ToUniversalTime();
            var orders = Enumerable.Range(1, orderCount)
                .Select(index => new SalesOrder
                {
                    OrderGuid = Guid.Parse($"00000000-0000-0000-0000-{index:D12}").ToString("D"),
                    OrderTime = orderTime.AddSeconds(-index),
                    BranchCode = "S001",
                    DeviceCode = "POS01",
                    CashierName = "Cashier",
                    TotalAmount = 10m,
                    ActualAmount = 10m,
                    ItemCount = 1,
                    Status = 1
                })
                .Append(new SalesOrder
                {
                    OrderGuid = TargetOrderGuid.ToString("D"),
                    OrderTime = orderTime,
                    BranchCode = "S001",
                    DeviceCode = "POS01",
                    CashierName = "Cashier",
                    TotalAmount = 20m,
                    ActualAmount = 20m,
                    ItemCount = 1,
                    Status = 1
                })
                .Append(new SalesOrder
                {
                    OrderGuid = Guid.Parse("ffffffff-1111-2222-3333-444444444444").ToString("D"),
                    OrderTime = orderTime,
                    BranchCode = "S002",
                    DeviceCode = "POS02",
                    CashierName = "Other",
                    TotalAmount = 20m,
                    ActualAmount = 20m,
                    ItemCount = 1,
                    Status = 1
                })
                .ToList();
            var lines = orders.Select((order, index) => new SalesOrderDetail
            {
                OrderDetailGuid = Guid.NewGuid().ToString("D"),
                OrderGuid = order.OrderGuid!,
                ProductCode = order.OrderGuid == TargetOrderGuid.ToString("D") || order.BranchCode == "S002"
                    ? "SKU-TARGET"
                    : $"P-{index:D6}",
                ProductName = "Tea",
                Barcode = order.OrderGuid == TargetOrderGuid.ToString("D") || order.BranchCode == "S002"
                    ? "930000000001"
                    : $"9301{index:D8}",
                Quantity = 1,
                Price = 10m,
                ActualAmount = 10m,
                Remark = order.OrderGuid == TargetOrderGuid.ToString("D") || order.BranchCode == "S002"
                    ? "priceSource=1;itemNo=ITEM-TARGET"
                    : $"priceSource=1;itemNo=ITEM-{index:D6}"
            }).ToList();

            foreach (var batch in orders.Chunk(500))
            {
                await client.Insertable(batch).ExecuteCommandAsync();
            }

            foreach (var batch in lines.Chunk(500))
            {
                await client.Insertable(batch).ExecuteCommandAsync();
            }

            await client.Ado.ExecuteCommandAsync(
                "CREATE INDEX IX_Test_SalesOrder_Scope ON sales_order(BranchCode, OrderTime DESC);");
            await client.Ado.ExecuteCommandAsync(
                "CREATE INDEX IX_Test_SalesOrderDetail_OrderGuid ON sales_order_detail(OrderGuid);");
        }

        private static HbposSqlSugarContext CreateDbContext(ISqlSugarClient posmDb)
        {
            var context = (HbposSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HbposSqlSugarContext));
            SetAutoProperty(context, nameof(HbposSqlSugarContext.MainDb), posmDb);
            SetAutoProperty(context, nameof(HbposSqlSugarContext.PosmDb), posmDb);
            return context;
        }

        private static void SetAutoProperty(HbposSqlSugarContext context, string propertyName, ISqlSugarClient value)
        {
            var backingField = typeof(HbposSqlSugarContext).GetField(
                $"<{propertyName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(backingField);
            backingField!.SetValue(context, value);
        }
    }
}
